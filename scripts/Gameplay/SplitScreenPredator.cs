using BmSDK.BmGame;

namespace Etkramer.SplitScreen.Gameplay;

/// <summary>
/// Fixes predator/stealth detection for extra players in split-screen.
///
/// Split-screen teleports extra players into predator volumes, bypassing the Touch
/// handler that normally initializes predator-room participation. That leaves P2+ without
/// the room attack coordinator and other setup needed for proper escalation even though
/// they can still be visible. This script restores that missing setup, retargets the
/// shared attack coordinator when a non-P1 player is actually spotted, and mirrors the
/// per-frame "seen" post-processing that GameInfoTick only performs for P1.
/// </summary>
[Script]
public sealed class SplitScreenPredator : Script
{
    private static bool IsSpotableRegistered(RBMRoomAIState roomState, RPawnPlayer pawn)
    {
        if (roomState == null || pawn == null)
        {
            return false;
        }

        for (var i = 0; i < roomState.SpotableList.Count; i++)
        {
            if (roomState.SpotableList[i] == pawn)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsurePredatorPlayerSetup(RPawnPlayer pawn, int playerIndex)
    {
        if (pawn == null || pawn.Health <= 0)
        {
            return;
        }

        var gameInfo = Game.GetGameInfo();
        var gri = Game.GetGameRI();
        if (gameInfo == null)
        {
            return;
        }

        var predVolume = gameInfo.FindPredatorVolumeFor(pawn);
        var roomState = predVolume?.RoomAIState;
        if (roomState == null)
        {
            return;
        }

        if (!IsSpotableRegistered(roomState, pawn))
        {
            roomState.RegisterSpotable(pawn);
        }

        var playerLevelVolume = gameInfo.GetLevelVolumeFor(predVolume);
        if (
            pawn.Controller is RPlayerController playerController
            && (gri?.IsOverworldGameplay() == true || gri?.CurrentLevelVolume == playerLevelVolume)
        )
        {
            playerController.SetDetectiveModeJammed(roomState.ActiveJammerCount > 0, true);
        }

        var attackCoordinator = roomState.CoordinatorParent?.AttackCoordinator;
        if (attackCoordinator != null)
        {
            pawn.V2AttackCoord = attackCoordinator;
            pawn.PlayerIndex = playerIndex;
        }

        if (gameInfo.CurrentRoomAIState == null)
        {
            gameInfo.SetNewRoomAIState(roomState);
        }
    }

    private static void RetargetAttackCoordinator(RPawnPlayer pawn)
    {
        if (pawn?.V2AttackCoord == null)
        {
            return;
        }

        if (pawn.V2AttackCoord.TargetPlayer != pawn)
        {
            pawn.V2AttackCoord.InitForPlayer(pawn);
        }
    }

    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.V2PredTriggerSpotted))]
    private static void V2PredTriggerSpottedRedirect(RPawnPlayer self, RBMAIController spotter)
    {
        var engine = Game.GetEngine();
        if (engine != null && engine.GamePlayers.Count >= 2)
        {
            RetargetAttackCoordinator(self);
        }

        self.V2PredTriggerSpotted(spotter);
    }

    /// <summary>
    /// Intercepts RBMRoomAIState.GameInfoTick to inject extra-player alerts before
    /// ProcessSpottableAlerts runs, and to handle detection events for those players
    /// afterward.
    /// </summary>
    [Redirect(typeof(RBMRoomAIState), nameof(RBMRoomAIState.GameInfoTick))]
    private static void GameInfoTickRedirect(RBMRoomAIState self, float deltaTime)
    {
        var engine = Game.GetEngine();
        if (engine == null || engine.GamePlayers.Count < 2)
        {
            self.GameInfoTick(deltaTime);
            return;
        }

        // --- Pre-processing: ensure every controller has an alert for each extra player ---
        for (var p = 1; p < engine.GamePlayers.Count; p++)
        {
            var pawn = engine.GamePlayers[p]?.Actor?.Pawn as RPawnPlayer;
            if (pawn == null || pawn.Health <= 0)
            {
                continue;
            }

            EnsurePredatorPlayerSetup(pawn, p);

            // Add missing alerts on each controller (AddAlert is a no-op if one exists)
            for (var c = 0; c < self.ControllerList.Count; c++)
            {
                var controller = self.ControllerList[c];
                if (controller?.FindAlertFor(pawn) == null)
                {
                    controller?.AddAlert(pawn, pawn.Location, AlertInstance.InterruptType.IN_Blank);
                }
            }

            // Reset per-frame tracker so we can detect new sightings
            pawn.MostRecentThugToSeePlayer = null;
        }

        // --- Run original GameInfoTick (handles P1 + now also processes extra-player alerts) ---
        self.GameInfoTick(deltaTime);

        // --- Post-processing: mirror what GameInfoTick does for P1 ---
        // The original only checks PP = GetPlayerPawn() (P1). We replicate the same
        // logic for extra players so that FireSeenEvents, PlayerSpotted, and the GRI
        // flags are updated when an enemy spots P2+.
        var gri = Game.GetGameRI();
        for (var p = 1; p < engine.GamePlayers.Count; p++)
        {
            var pawn = engine.GamePlayers[p]?.Actor?.Pawn as RPawnPlayer;
            if (pawn == null || pawn.Health <= 0)
            {
                continue;
            }

            var spotter = pawn.MostRecentThugToSeePlayer;
            if (spotter == null)
            {
                continue;
            }

            RetargetAttackCoordinator(pawn);

            // FireSeenEvents has its own 2-second debounce so repeated calls are safe
            spotter.FireSeenEvents();

            var playerController = pawn.Controller as RPlayerController;
            playerController?.PlayerSpotted(spotter.PawnVillain);

            if (gri != null)
            {
                gri.PlayerSpottedTimer = 0;
                gri.bLockoutSilentTakedownsDueToAttack = true;
            }

            pawn.HasBeenSeen_UpdateData();
        }
    }
}
