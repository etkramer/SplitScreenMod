using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using BmSDK.GFxUI;

namespace Etkramer.SplitScreen.Rendering;

// Fixes a bug where P2 doesn't have any HUD
[Script]
public sealed class SplitScreenHUD : Script
{
    static bool s_hasEnteredGame = false;

    public override void OnEnterMenu()
    {
        s_hasEnteredGame = false;
    }

    public override void OnEnterGame()
    {
        s_hasEnteredGame = true;
    }

    public override void OnTick()
    {
        var engine = Game.GetEngine();
        if (engine.GamePlayers.Count < 2)
        {
            base.OnTick();
            return;
        }

        var gameViewport = Game.GetGameViewportClient();
        gameViewport.GetViewportSize(out var viewportSize);

        foreach (var player in engine.GamePlayers)
        {
            var controller = player.Actor as RPlayerController;
            if (controller == null)
            {
                continue;
            }

            var originPixels = player.Origin * viewportSize;
            var sizePixels = player.Size * viewportSize;

            var x = (int)Math.Round(originPixels.X);
            var y = (int)Math.Round(originPixels.Y);
            var w = (int)Math.Round(sizePixels.X);
            var h = (int)Math.Round(sizePixels.Y);

            controller.HudMovieNew?.SetViewport(x, y, w, h);

            if (controller.MinigameMovie != null)
            {
                controller.MinigameMovie.SetViewport(x, y, w, h);

                // Sequencer now renders flat over the player's viewport instead
                // of onto Batman's in-world hologram — keep that mesh hidden.
                var tuner = (controller.Pawn as RPawnPlayer)?.ResonatorTuner;
                tuner?.ScreenHologram?.SetHidden(true);
                tuner?.ScreenHologramMesh?.SetHidden(true);
            }
        }

        base.OnTick();
    }

    // Force the cryptographic sequencer to render as a flat overlay instead of
    // onto the in-world hologram texture — sidesteps splitscreen stage-size
    // issues that broke the password cracker for P2.
    [Redirect(typeof(RGFxMovieResonatorMinigame), nameof(RGFxMovieResonatorMinigame.Init))]
    private static void RGFxMovieResonatorMinigame_InitRedirect(
        RGFxMovieResonatorMinigame self,
        LocalPlayer locPlay
    )
    {
        self.bWorldRendered = false;
        self.RenderTexture = null!;
        self.Init(locPlay);
    }

    [Redirect(typeof(RGFxMovie), nameof(RGFxMovie.Init))]
    private static void RGFxMovie_InitRedirect(RGFxMovie self, LocalPlayer locPlay)
    {
        // Run original first.
        self.Init(locPlay);

        if (!s_hasEnteredGame || !(self is RGFxMovieHudExtendable))
        {
            return;
        }

        var rpc = locPlay?.Actor as RPlayerController ?? self.RPC;
        if (rpc == null)
        {
            return;
        }

        self.RPC = rpc;

        if (rpc.IsSplitscreenPlayer(out var splitIndex))
        {
            self.iSplitscreenIndex = splitIndex;
            self.SetViewportSplitscreenIndex(0);
        }
        else
        {
            self.iSplitscreenIndex = 0;
            self.SetViewportSplitscreenIndex(0);
        }
    }

    [Redirect(typeof(RHudExtensionHealth), nameof(RHudExtensionHealth.Init))]
    private static bool RHudExtensionHealth_InitRedirect(
        RHudExtensionHealth self,
        RPlayerController rpc,
        FString extensionName,
        FString extensionPath
    )
    {
        self.PlayerSideIndex = rpc.HudMovieSide;
        return self.Init(rpc, extensionName, extensionPath);
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.CreateBaseHud))]
    private static void CreateBaseHudRedirect(RPlayerController self)
    {
        // Let vanilla handle P1 and non-split cases.
        self.CreateBaseHud();

        if (
            !TryGetSplitHudContext(
                self,
                out var splitIndex,
                out var multiplayerIndex,
                out var localPlayer,
                out var gri,
                out var ownAcronym
            )
            || self.HudMovieNew != null
        )
        {
            return;
        }

        var movieInfo = Game.FindObject<SwfMovie>("ModularHudBase.HUD");
        if (ownAcronym == null || movieInfo == null)
        {
            return;
        }

        var hud = new RGFxMovieHudExtendable(self) { MovieInfo = movieInfo };
        hud.Start(false);
        hud.Init(localPlayer);
        hud.Advance(0.0f);
        hud.SetFocus(false, false);
        hud.SetViewportSplitscreenIndex(multiplayerIndex);

        self.HudMovieNew = hud;
        self.HudMovieSide = 0;

        hud.CharacterAcronyms[0] = ownAcronym;
        if (gri!.IsMultiplayer())
        {
            var otherAcronym = GetCharacterAcronym(gri, multiplayerIndex == 0 ? 1 : 0);
            if (otherAcronym != null)
            {
                hud.CharacterAcronyms[1] = otherAcronym;
            }
        }
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.InitStandardHUD))]
    private static void InitStandardHUDRedirect(RPlayerController self)
    {
        if (!TryGetSplitHudContext(self, out _, out _, out _, out _, out var ownAcronym))
        {
            self.InitStandardHUD();
            return;
        }

        var hud = self.HudMovieNew;
        if (hud == null)
        {
            self.InitStandardHUD();
            return;
        }

        if (hud.bCommonElementsInitialised)
        {
            return;
        }

        var acronym = hud.CharacterAcronyms[self.HudMovieSide].ToString();
        if (string.IsNullOrWhiteSpace(acronym))
        {
            acronym = ownAcronym;
            if (string.IsNullOrWhiteSpace(acronym))
            {
                return;
            }
        }

        hud.bCommonElementsInitialised = true;

        var general = new RHudExtensionGeneral(self);
        general.Init(self, "General", "StoryModeHUD.HUD");

        var health = new RHudExtensionHealth(self) { PlayerSideIndex = self.HudMovieSide };
        health.Init(self, "HealthBar", "ModuleHealthBar");

        if (!string.Equals(acronym, "BW", StringComparison.OrdinalIgnoreCase))
        {
            var gadgets = new RHudExtensionGadgets(self) { PlayerSideIndex = self.HudMovieSide };
            gadgets.Init(self, "GadgetSelect", $"ModuleGadgetSelect{acronym}");
        }

        var targets = new RHudExtensionTargets(self) { PlayerSideIndex = self.HudMovieSide };
        targets.Init(self, "Targets", $"ModuleTargets{acronym}");
    }

    private static bool TryGetSplitHudContext(
        RPlayerController self,
        out int splitIndex,
        out int multiplayerIndex,
        out LocalPlayer? localPlayer,
        out RGameRI? gri,
        out string? characterAcronym
    )
    {
        splitIndex = 0;
        multiplayerIndex = 0;
        localPlayer = null;
        gri = null;
        characterAcronym = null;

        if (!s_hasEnteredGame || self == null || !self.IsLocalPlayerController())
        {
            return false;
        }

        localPlayer = self.Player as LocalPlayer;
        if (localPlayer == null || !self.IsSplitscreenPlayer(out splitIndex))
        {
            return false;
        }

        gri = self.GetGRI();
        if (gri == null)
        {
            return false;
        }

        multiplayerIndex = self.GetMultiplayerIndex();
        characterAcronym = GetCharacterAcronym(gri, multiplayerIndex);
        return characterAcronym != null;
    }

    private static string? GetCharacterAcronym(RGameRI gri, int multiplayerIndex)
    {
        if (gri == null)
        {
            return null;
        }

        return multiplayerIndex switch
        {
            0 => (gri.PlayerCharacters_0.CharacterData?.CharacterAcronym)?.ToString(),
            1 => (gri.PlayerCharacters_1.CharacterData?.CharacterAcronym)?.ToString(),
            2 => (gri.PlayerCharacters_2.CharacterData?.CharacterAcronym)?.ToString(),
            3 => (gri.PlayerCharacters_3.CharacterData?.CharacterAcronym)?.ToString(),
            4 => (gri.PlayerCharacters_4.CharacterData?.CharacterAcronym)?.ToString(),
            5 => (gri.PlayerCharacters_5.CharacterData?.CharacterAcronym)?.ToString(),
            6 => (gri.PlayerCharacters_6.CharacterData?.CharacterAcronym)?.ToString(),
            7 => (gri.PlayerCharacters_7.CharacterData?.CharacterAcronym)?.ToString(),
            _ => null,
        };
    }
}
