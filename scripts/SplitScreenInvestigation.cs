using BmSDK;
using BmSDK.BmGame;
using BmSDK.GFxUI;

[Script]
public sealed class SplitScreenInvestigation : Script
{
    [Redirect(typeof(RPlayerController), nameof(RPlayerController.StartInvestigateMovie))]
    private static void StartInvestigateMovieRedirect(RPlayerController self)
    {
        if (self.HudMovieNew == null)
        {
            self.CreateBaseHud();
        }

        if (self.HudMovieNew == null)
        {
            return;
        }

        var currentForensicsDevice = self.CurrentForensicsDevice;
        var gri = self.GetGRI();
        if (currentForensicsDevice != null && currentForensicsDevice.bDisableForensicHUDMovie)
        {
            var specialForensicsHud = GetSpecialForensicsHud(gri, self.GetMultiplayerIndex());
            if (gri?.DetectiveModeJammed != 1 && specialForensicsHud != null)
            {
                self.StartInvestigateMovieAsExtraHud(specialForensicsHud);
            }

            self.HudMovieNew.SetInvestigateModeAuto();
            self.UpdateCrimeSceneInfo();
            return;
        }

        if (self.HudMovieNew.InvestigateMovie == null)
        {
            self.HudMovieNew.InvestigateMovie = GameObject.ConstructObject<RHudExtensionInvestigate>(self);
            self.HudMovieNew.InvestigateMovie?.Init(self, "", "");
            self.HudMovieNew.SetInvestigateModeAuto();
        }

        currentForensicsDevice?.SetInvestigateMovieInfo();
        self.HudMovieChallenge?.SetFocus(false, false);
        self.PauseMenuMovie?.SetFocus(false, false);
        self.UpdateCrimeSceneInfo();
    }

    private static SwfMovie GetSpecialForensicsHud(RGameRI gri, int multiplayerIndex)
    {
        if (gri == null)
        {
            return null;
        }

        var characterData = multiplayerIndex switch
        {
            0 => gri.PlayerCharacters_0.CharacterData,
            1 => gri.PlayerCharacters_1.CharacterData,
            2 => gri.PlayerCharacters_2.CharacterData,
            3 => gri.PlayerCharacters_3.CharacterData,
            4 => gri.PlayerCharacters_4.CharacterData,
            5 => gri.PlayerCharacters_5.CharacterData,
            6 => gri.PlayerCharacters_6.CharacterData,
            7 => gri.PlayerCharacters_7.CharacterData,
            _ => null,
        };

        return characterData?.SpecialForensicsHud;
    }
}
