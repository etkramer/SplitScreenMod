using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using ESplitScreenType = BmSDK.Engine.GameViewportClient.ESplitScreenType;

[Script]
public class SplitScreen : Script
{
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
        else if (key == Keys.L)
        {
            // SSwapControllers
            var engine = Game.GetEngine();
            foreach (var player in engine.GamePlayers)
            {
                player.ControllerId = (player.ControllerId + 1) % engine.GamePlayers.Count;
            }
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
        else if (key == Keys.N)
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
            2 => ESplitScreenType.eSST_2P_VERTICAL,
            3 => ESplitScreenType.eSST_3P_FAVOR_TOP,
            4 => ESplitScreenType.eSST_4P,
            _ => self.DesiredSplitscreenType
        };

        self.ActiveSplitscreenType = splitType;
    }

    // TODO: Fix bloom effect and remove.
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

        // Disable RockOn effect (broken in split-screen)
        foreach (var player in engine.GamePlayers)
        {
            var rockOnEffect = player.PlayerPostProcess.Effects.FirstOrDefault(o => o.Name == "RockOn");
            rockOnEffect?.bShowInGame = false;
        }
    }

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
        bool bForSavingOnly)
    {
        // Call base for P1
        self.SetPlayerLocation(PC, Pos, Rot, TellPlayerHesMoved, bForSavingOnly);

        // Manually move all other players
        var engine = Game.GetEngine();
        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            var pawn = Game.GetPlayerPawn(i);
            pawn.SetLocationIgnoringCollision(Pos);
            pawn.Velocity = default;
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
