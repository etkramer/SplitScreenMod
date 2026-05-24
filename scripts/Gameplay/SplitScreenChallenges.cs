using BmSDK.BmGame;

namespace Etkramer.SplitScreen.Gameplay;

// Fixes scoring for challenge maps so that P1 and P2 share the same score
[Script]
public sealed class SplitScreenChallenges : Script
{
    // Fix gaining score as P2
    [Redirect(typeof(RPlayerControllerCombat), nameof(RPlayerControllerCombat.AddScore))]
    private static void AddScoreRedirect(RPlayerControllerCombat self, int amount)
    {
        self.AddScore(amount);

        var engine = Game.GetEngine();
        if (amount <= 0 || engine.GamePlayers.Count < 2)
        {
            return;
        }

        // Mirror score onto other local players so each HUD shows the combined total.
        foreach (var player in engine.GamePlayers)
        {
            if (player.Actor is not RPlayerControllerCombat rpcc || rpcc == self)
            {
                continue;
            }

            rpcc.CombatScore += amount;
            rpcc.PlayerReplicationInfo?.Score = rpcc.CombatScore;
            if (rpcc.bAutomaticallyDisplayScore)
            {
                rpcc.SetScoreOnHud();
            }
        }
    }
}
