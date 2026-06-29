using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using EAspectRatioAxisConstraint = BmSDK.GameObject.EAspectRatioAxisConstraint;
using ESplitScreenType = BmSDK.Engine.GameViewportClient.ESplitScreenType;

namespace Etkramer.SplitScreen;

[Script]
public class SplitScreen : Script
{
    public static SplitScreen Instance { get; private set; } = null!;

    public TomlTable Options => (TomlTable)Mod.Config["options"];
    public bool UseHorizontalLayout => Convert.ToBoolean(Options["layout_horizontal"]);
    public bool UseLetterboxing => Convert.ToBoolean(Options["letterboxing"]);

    public SplitScreen()
    {
        Instance ??= this;
    }

    public override void Main()
    {
        // Allow up to 4 players
        Game.GetGameInfo().MaxPlayers = 4;
        RGameInfo.DefaultObject.MaxPlayers = 4;
    }

    public override void OnKeyDown(Keys key)
    {
        // Debug actions based on key press.
        if (key == Keys.G)
        {
            var engine = Game.GetEngine();

            // Spawn P2
            var gameViewport = Game.GetGameViewportClient();
            gameViewport.CreatePlayer(engine.GamePlayers.Count, out _, true);
        }
        else if (key == Keys.T)
        {
            var player1 = Game.GetPlayerPawn(0);

            // Teleport players to P1
            var engine = Game.GetEngine();
            foreach (var player in engine.GamePlayers)
            {
                var pawn = player.Actor.Pawn;
                pawn.Location = player1.Location;
                pawn.Rotation = player1.Rotation;
            }
        }
        else if (key == Keys.Y)
        {
            var player2 = Game.GetPlayerPawn(1);

            // Teleport players to P2
            var engine = Game.GetEngine();
            foreach (var player in engine.GamePlayers)
            {
                var pawn = player.Actor.Pawn;
                pawn.Location = player2.Location;
                pawn.Rotation = player2.Rotation;
            }
        }
        else if (key == Keys.V)
        {
            Debug.Log("Toggling ghost");

            var cheatManager = Game.GetCheatManager();
            cheatManager.ToggleGhost();
        }
        else if (key == Keys.O)
        {
            var engine = Game.GetEngine();

            // Remove P2
            var gameViewport = Game.GetGameViewportClient();
            gameViewport.RemovePlayer(engine.GamePlayers.LastOrDefault());
        }
    }

    // Ensure correct split type is used
    [Redirect(typeof(GameViewportClient), nameof(GameViewportClient.UpdateActiveSplitscreenType))]
    public static void UpdateActiveSplitscreenTypeRedirect(GameViewportClient self)
    {
        var engine = Game.GetEngine();

        var splitType = engine.GamePlayers.Count switch
        {
            1 => ESplitScreenType.eSST_NONE,
            2 => Instance.UseHorizontalLayout
                ? ESplitScreenType.eSST_2P_HORIZONTAL
                : ESplitScreenType.eSST_2P_VERTICAL,
            3 => ESplitScreenType.eSST_3P_FAVOR_TOP,
            4 => ESplitScreenType.eSST_4P,
            _ => self.DesiredSplitscreenType,
        };

        self.ActiveSplitscreenType = splitType;
    }

    [Redirect(typeof(GameViewportClient), nameof(GameViewportClient.LayoutPlayers))]
    public static void LayoutPlayersRedirect(GameViewportClient self)
    {
        // Run original
        self.LayoutPlayers();

        var engine = Game.GetEngine();
        if (engine.GamePlayers.Count < 2)
        {
            return;
        }

        // For 2-player horizontal, crop to maintain aspect ratio
        if (
            engine.GamePlayers.Count == 2
            && self.ActiveSplitscreenType == ESplitScreenType.eSST_2P_HORIZONTAL
        )
        {
            var p1 = engine.GamePlayers[0];
            var p2 = engine.GamePlayers[1];

            if (Instance.UseLetterboxing)
            {
                p1.Size.X = 0.5f;
                p1.Origin.X = 0.25f;
                p2.Size.X = 0.5f;
                p2.Origin.X = 0.25f;
            }
            else
            {
                p1.AspectRatioAxisConstraint = EAspectRatioAxisConstraint.AspectRatio_MaintainYFOV;
                p2.AspectRatioAxisConstraint = EAspectRatioAxisConstraint.AspectRatio_MaintainYFOV;
            }
        }
        // For 2-player vertical, crop to maintain aspect ratio
        else if (
            self.ActiveSplitscreenType == ESplitScreenType.eSST_2P_VERTICAL 
            && engine.GamePlayers.Count == 2
        ) 
        {
            var p1 = engine.GamePlayers[0];
            var p2 = engine.GamePlayers[1];

            if (Instance.UseLetterboxing) 
            {
                p1.Size.Y = 0.5f;
                p1.Origin.Y = 0.25f;
                p2.Size.Y = 0.5f;
                p2.Origin.Y = 0.25f;
            }
            else 
            {
                p1.AspectRatioAxisConstraint = EAspectRatioAxisConstraint.AspectRatio_MaintainYFOV;
                p2.AspectRatioAxisConstraint = EAspectRatioAxisConstraint.AspectRatio_MaintainYFOV;
            }
        }
    }

    // Force IsMultiplayer() to return false - fixes the level-up screen and possibly other bugs
    [Redirect(typeof(RGameRI), nameof(RGameRI.IsMultiplayer))]
    private static bool IsMultiplayerRedirect(RGameRI self) => false;

    // Ensure players aren't spawned in the void
    [Redirect(typeof(RPlayerStartInLevel), nameof(RPlayerStartInLevel.MovePlayerHere))]
    private static void MovePlayerHereRedirect(RPlayerStartInLevel self)
    {
        // Call base for P1
        self.MovePlayerHere();

        // Manually move all other players
        var engine = Game.GetEngine();
        for (var i = 1; i < engine.GamePlayers.Count; i++)
        {
            var pawn = Game.GetPlayerPawn(i);
            pawn.SetLocationIgnoringCollision(self.Location);
            pawn.SetRotation(self.Rotation);
            pawn.Velocity = default;
        }
    }

    // Ensure players aren't spawned in the void
    [Redirect(typeof(RLevelTransition), nameof(RLevelTransition.SetPlayerLocation))]
    private static void SetPlayerLocationRedirect(
        RLevelTransition self,
        RPlayerController PC,
        Vector3 Pos,
        Rotator Rot,
        bool TellPlayerHesMoved,
        bool bForSavingOnly
    )
    {
        // Call base for P1
        self.SetPlayerLocation(PC, Pos, Rot, TellPlayerHesMoved, bForSavingOnly);

        // Manually move all other players
        var engine = Game.GetEngine();
        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            var pawn = Game.GetPlayerPawn(i);
            pawn.SetLocationIgnoringCollision(Pos);
            pawn.SetRotation(Rot);
            pawn.Velocity = default;
        }
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.SetSaveLocation))]
    private static void SetSaveLocationRedirect(RPlayerController self, Vector3 Loc, Rotator Rot)
    {
        self.SetSaveLocation(Loc, Rot);

        var engine = Game.GetEngine();
        foreach (var player in engine.GamePlayers)
        {
            if (player?.Actor is not RPlayerController pc || ReferenceEquals(pc, self))
            {
                continue;
            }

            pc.SaveGameLocation = self.SaveGameLocation;
            pc.SaveGameRotation = self.SaveGameRotation;
            pc.SaveGameSetTime = self.SaveGameSetTime;
        }
    }

    // Fix local player checks for 3P/4P (resolves challenge map spawn issues)
    [Redirect(typeof(RPlayerController), nameof(RPlayerController.IsPrimaryLocalPlayer))]
    private static bool IsPrimaryLocalPlayerRedirect(RPlayerController self)
    {
        if (!self.IsLocalPlayerController())
        {
            return false;
        }

        if (self.IsSplitscreenPlayer(out var splitIndex))
        {
            return splitIndex == 0;
        }

        return true;
    }
}
