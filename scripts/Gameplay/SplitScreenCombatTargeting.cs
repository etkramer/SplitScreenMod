using BmSDK.BmGame;

namespace Etkramer.SplitScreen.Gameplay;

// Allows hits to go through on ragdolled pawns, preventing accidental combo loss in split-screen
[Script]
public sealed class SplitScreenCombatTargeting : Script
{
    private static bool IsSplitScreenActive()
    {
        return Game.GetEngine().GamePlayers.Count >= 2;
    }

    [Redirect(typeof(RPawnCombat), nameof(RPawnCombat.IsVulnerableToPawn))]
    private static bool IsVulnerableToPawnRedirect(RPawnCombat self, RPawnCombat attacker)
    {
        if (
            IsSplitScreenActive()
            && self is RPawnVillain
            && attacker is RPawnPlayer
            && self.IsRagdoll()
            && !self.IsGettingUpFromRagdoll()
        )
        {
            return true;
        }

        return self.IsVulnerableToPawn(attacker);
    }
}
