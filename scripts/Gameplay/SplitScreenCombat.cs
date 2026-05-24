using System.Numerics;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.Gameplay;

// Adds custom combat behavior for split-screen, where enemies split into groups by player
[Script]
public sealed class SplitScreenCombat : Script
{
    private const float VisibleTargetBonus = 10000.0f;
    private const float CurrentTargetBonus = 1500.0f;
    private const float DistanceScoreScale = 0.01f;

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

    private static bool IsSplitScreenActive()
    {
        return Game.GetEngine()?.GamePlayers.Count >= 2;
    }

    private static bool IsRegisteredPlayer(RGameRI gri, RPawnPlayer player)
    {
        if (gri == null || player == null)
        {
            return false;
        }

        for (var i = 0; i < gri.PlayerList.Count; i++)
        {
            if (gri.PlayerList[i] == player)
            {
                return true;
            }
        }

        return false;
    }

    private static List<RPawnPlayer> GetLocalPlayers()
    {
        var players = new List<RPawnPlayer>();
        var engine = Game.GetEngine();
        if (engine == null)
        {
            return players;
        }

        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            if (engine.GamePlayers[i]?.Actor?.Pawn is not RPawnPlayer player || player.Health <= 0)
            {
                continue;
            }

            players.Add(player);
        }

        return players;
    }

    private static RPawnPlayer? ChooseBestLocalPlayer(
        Actor source,
        RBMAIController aiController,
        RPawnPlayer currentPlayer,
        bool requireVisible
    )
    {
        if (source == null)
        {
            return null;
        }

        var localPlayers = GetLocalPlayers();
        if (localPlayers.Count == 0)
        {
            return null;
        }

        RPawnPlayer? bestPlayer = null;
        var bestScore = float.MinValue;

        for (var i = 0; i < localPlayers.Count; i++)
        {
            var player = localPlayers[i];
            if (
                player.Controller is RPlayerController playerController
                && playerController.IsHidden()
            )
            {
                continue;
            }

            var visible =
                aiController != null
                && aiController.CalcVisibilityFor(player)
                    >= AlertInstance.VisibilityCategory.VISCat_SlowSpot;
            if (requireVisible && !visible)
            {
                continue;
            }

            var score =
                -Vector3.DistanceSquared(source.Location, player.Location) * DistanceScoreScale;
            if (visible)
            {
                score += VisibleTargetBonus;
            }

            if (player == currentPlayer)
            {
                score += CurrentTargetBonus;
            }

            if (bestPlayer == null || score > bestScore)
            {
                bestPlayer = player;
                bestScore = score;
            }
        }

        return bestPlayer
            ?? (
                currentPlayer is RPawnPlayer currentCombatPlayer
                    ? currentCombatPlayer
                    : localPlayers[0]
            );
    }

    private static RPawnPlayer? ChooseBestTarget(RPawnVillain villain, bool requireVisible)
    {
        return villain == null
            ? null
            : ChooseBestLocalPlayer(
                villain,
                villain.AIController,
                (RPawnPlayer)villain.GetTargetPlayer(),
                requireVisible
            );
    }

    private static AlertInstance? ChooseBestAlert(
        RBMAIController controller,
        RPawnPlayer currentPlayer
    )
    {
        if (controller?.Pawn == null)
        {
            return null;
        }

        var localPlayers = GetLocalPlayers();
        if (localPlayers.Count == 0)
        {
            return null;
        }

        AlertInstance? bestAlert = null;
        var bestScore = float.MinValue;

        for (var i = 0; i < localPlayers.Count; i++)
        {
            var player = localPlayers[i];
            var alert = controller.FindAlertFor(player);
            if (alert == null)
            {
                continue;
            }

            var score =
                (alert.SightLevel * 1000.0f)
                + ((int)alert.StoredVisibility * 100.0f)
                - (
                    Vector3.DistanceSquared(controller.Pawn.Location, player.Location)
                    * DistanceScoreScale
                );

            if (player == currentPlayer)
            {
                score += CurrentTargetBonus;
            }

            if (bestAlert == null || score > bestScore)
            {
                bestAlert = alert;
                bestScore = score;
            }
        }

        return bestAlert;
    }

    private static void RefreshBehaviourBatman(RBMBehaviour_MoveToBase? behaviour)
    {
        if (behaviour?.HostPawn == null)
        {
            return;
        }

        var bestPlayer = ChooseBestLocalPlayer(
            behaviour.HostPawn,
            behaviour.HostPawn.AIController,
            behaviour.Batman,
            requireVisible: false
        );
        if (bestPlayer != null && behaviour.Batman != bestPlayer)
        {
            behaviour.Batman = bestPlayer;
        }
    }

    private static void RetargetVillain(RPawnVillain? villain, bool requireVisible)
    {
        if (villain == null)
        {
            return;
        }

        var target = ChooseBestTarget(villain, requireVisible);
        if (target != null && villain.GetTargetPlayer() != target)
        {
            villain.SetTargetPlayer(target);
        }
    }

    private static void RebalanceCombatAssignmentsFor(RPawnVillain? villain)
    {
        if (!IsSplitScreenActive())
        {
            return;
        }

        SplitScreenCombatGrouping.RebalanceCombatAssignmentsFor(villain);
    }

    private static void RefreshIdleVillainTargets()
    {
        var aiManager = Game.GetGameInfo()?.AIManager;
        if (aiManager == null)
        {
            return;
        }

        for (var i = 0; i < aiManager.GlobalControllerInfoList.Count; i++)
        {
            var controller = aiManager.GlobalControllerInfoList[i].Controller;
            var villain = controller?.PawnVillain;
            if (controller == null || villain == null || villain.Health <= 0)
            {
                continue;
            }

            if (controller.bInCombat || villain.bIsSilentPredV2)
            {
                continue;
            }

            RetargetVillain(villain, requireVisible: false);
        }
    }

    private static void EnsureSplitScreenPlayerAlerts(RGameInfo gameInfo)
    {
        var aiManager = gameInfo.AIManager;
        if (aiManager == null)
        {
            return;
        }

        var localPlayers = GetLocalPlayers();
        if (localPlayers.Count < 2)
        {
            return;
        }

        for (var i = 0; i < aiManager.GlobalControllerInfoList.Count; i++)
        {
            var controller = aiManager.GlobalControllerInfoList[i].Controller;
            if (controller?.Pawn is not RBMPawnAI pawn || pawn.bFriendly)
            {
                continue;
            }

            for (var p = 1; p < localPlayers.Count; p++)
            {
                var player = localPlayers[p];
                if (controller.FindAlertFor(player) == null)
                {
                    controller.AddAlert(
                        player,
                        player.Location,
                        AlertInstance.InterruptType.IN_Blank
                    );
                }
            }
        }
    }

    private static void EnsureSplitScreenGameRIPlayers()
    {
        var gri = Game.GetGameRI();
        var localPlayers = GetLocalPlayers();
        if (gri == null || localPlayers.Count < 2)
        {
            return;
        }

        for (var p = 1; p < localPlayers.Count; p++)
        {
            var player = localPlayers[p];
            if (!IsRegisteredPlayer(gri, player))
            {
                gri.RegisterPlayer(player);
            }
        }
    }

    private static RBMRoomAIState? GetPlayerRoomState(RGameInfo gameInfo, RPawnPlayer player)
    {
        return gameInfo?.FindPredatorVolumeFor(player)?.RoomAIState;
    }

    private static void TickExtraPlayerRoomStates(RGameInfo gameInfo, float deltaTime)
    {
        var localPlayers = GetLocalPlayers();
        if (localPlayers.Count < 2)
        {
            return;
        }

        var roomStates = new List<RBMRoomAIState>();
        for (var p = 1; p < localPlayers.Count; p++)
        {
            var roomState = GetPlayerRoomState(gameInfo, localPlayers[p]);
            if (roomState == null || roomState == gameInfo.CurrentRoomAIState)
            {
                continue;
            }

            var seen = false;
            for (var i = 0; i < roomStates.Count; i++)
            {
                if (roomStates[i] == roomState)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                roomStates.Add(roomState);
            }
        }

        // Native GameInfo.Tick only advances CurrentRoomAIState. Extra local players can be
        // standing in a different AI room, so tick those room alert pipelines as well.
        for (var i = 0; i < roomStates.Count; i++)
        {
            roomStates[i].GameInfoTick(deltaTime);
        }
    }

    private static void EnsureSplitScreenRoomSetup(RGameInfo gameInfo)
    {
        var gri = Game.GetGameRI();
        var localPlayers = GetLocalPlayers();
        if (localPlayers.Count < 2)
        {
            return;
        }

        for (var p = 1; p < localPlayers.Count; p++)
        {
            var player = localPlayers[p];
            var predVolume = gameInfo.FindPredatorVolumeFor(player);
            var roomState = predVolume?.RoomAIState;
            if (roomState == null)
            {
                continue;
            }

            // P2 can be spawned directly inside the room volume, which skips the
            // Touch handler that normally registers the player as a spotable target.
            if (!IsSpotableRegistered(roomState, player))
            {
                roomState.RegisterSpotable(player);
            }

            var attackCoordinator = roomState.CoordinatorParent?.AttackCoordinator;
            if (attackCoordinator != null)
            {
                player.V2AttackCoord = attackCoordinator;
                player.PlayerIndex = p;
            }

            var playerLevelVolume = gameInfo.GetLevelVolumeFor(predVolume);
            if (
                player.Controller is RPlayerController playerController
                && (
                    gri?.IsOverworldGameplay() == true
                    || gri?.CurrentLevelVolume == playerLevelVolume
                )
            )
            {
                playerController.SetDetectiveModeJammed(roomState.ActiveJammerCount > 0, true);
            }
        }
    }

    private static void HandlePlayerNoiseForController(
        RBMAIController? controller,
        RPawnPlayer noiseMaker,
        float noiseRadius
    )
    {
        if (controller?.Pawn is not RBMPawnAI pawn || pawn.bFriendly || pawn.bIsValidCombatant)
        {
            return;
        }

        var distSq = Vector3.DistanceSquared(noiseMaker.Location, pawn.Location);
        if (distSq < noiseRadius * noiseRadius)
        {
            controller.HandleNoise(noiseMaker);
        }
    }

    [Redirect(typeof(RGameInfo), nameof(RGameInfo.Tick))]
    private static void GameInfoTickRedirect(RGameInfo self, float deltaTime)
    {
        if (IsSplitScreenActive())
        {
            EnsureSplitScreenGameRIPlayers();
            EnsureSplitScreenRoomSetup(self);
            EnsureSplitScreenPlayerAlerts(self);
        }

        self.Tick(deltaTime);

        if (!IsSplitScreenActive())
        {
            return;
        }

        TickExtraPlayerRoomStates(self, deltaTime);
        SplitScreenCombatGrouping.RebalanceActiveCombatAssignments();
        RefreshIdleVillainTargets();
    }

    [Redirect(typeof(RBMAIManager), nameof(RBMAIManager.CreatePlayerNoise))]
    private static void CreatePlayerNoiseRedirect(
        RBMAIManager self,
        RPawnPlayer noiseMaker,
        float noiseRadius
    )
    {
        if (!IsSplitScreenActive())
        {
            self.CreatePlayerNoise(noiseMaker, noiseRadius);
            return;
        }

        var gameInfo = Game.GetGameInfo();
        var roomState = GetPlayerRoomState(gameInfo, noiseMaker) ?? gameInfo?.CurrentRoomAIState;
        if (roomState != null)
        {
            for (var i = 0; i < roomState.ControllerList.Count; i++)
            {
                HandlePlayerNoiseForController(
                    roomState.ControllerList[i],
                    noiseMaker,
                    noiseRadius
                );
            }
        }
        else
        {
            var aiManager = gameInfo?.AIManager;
            if (aiManager == null)
            {
                self.CreatePlayerNoise(noiseMaker, noiseRadius);
                return;
            }

            for (var i = 0; i < aiManager.GlobalControllerInfoList.Count; i++)
            {
                HandlePlayerNoiseForController(
                    aiManager.GlobalControllerInfoList[i].Controller,
                    noiseMaker,
                    noiseRadius
                );
            }
        }

        gameInfo?.CombatManager.PlayerSeen(noiseMaker.Location);
        gameInfo?.CombatManager.TriggerCombatEvent(
            noiseMaker,
            noiseMaker.Location,
            noiseMaker.Location,
            noiseRadius,
            noiseRadius
        );
    }

    [Redirect(typeof(RBMAIController), nameof(RBMAIController.EnterCombat))]
    private static void EnterCombatRedirect(
        RBMAIController self,
        bool bCanReactFirst = true,
        bool bForceIntoCombat = false
    )
    {
        self.EnterCombat(bCanReactFirst, bForceIntoCombat);
        RebalanceCombatAssignmentsFor(self.PawnVillain);
    }

    [Redirect(typeof(RBMAIController), nameof(RBMAIController.FindAlertForPlayer))]
    private static AlertInstance FindAlertForPlayerRedirect(RBMAIController self)
    {
        return !IsSplitScreenActive() || self.PawnVillain?.bIsSilentPredV2 == true
            ? self.FindAlertForPlayer()
            : ChooseBestAlert(self, (RPawnPlayer)self.PawnVillain!.GetTargetPlayer())
                ?? self.FindAlertForPlayer();
    }

    [Redirect(typeof(RBMBehaviour_MoveToBase), nameof(RBMBehaviour_MoveToBase.OnActivate))]
    private static void MoveToOnActivateRedirect(RBMBehaviour_MoveToBase self)
    {
        if (IsSplitScreenActive())
        {
            RefreshBehaviourBatman(self);
        }

        self.OnActivate();
    }

    [Redirect(
        typeof(RBMBehaviour_MoveToBase),
        nameof(RBMBehaviour_MoveToBase.CheckForCloseProximity)
    )]
    private static bool MoveToCheckForCloseProximityRedirect(
        RBMBehaviour_MoveToBase self,
        float deltaTime
    )
    {
        if (IsSplitScreenActive())
        {
            RefreshBehaviourBatman(self);
        }

        return self.CheckForCloseProximity(deltaTime);
    }

    [Redirect(typeof(RBM2Behaviour_IdleConfig), nameof(RBM2Behaviour_IdleConfig.UpdateLookAt))]
    private static void UpdateLookAtRedirect(RBM2Behaviour_IdleConfig self, float deltaTime)
    {
        if (IsSplitScreenActive())
        {
            RefreshBehaviourBatman(self);
        }

        self.UpdateLookAt(deltaTime);
    }

    [Redirect(
        typeof(RBM2Behaviour_IdleConfig),
        nameof(RBM2Behaviour_IdleConfig.CheckForCloseProximity)
    )]
    private static bool IdleCheckForCloseProximityRedirect(
        RBM2Behaviour_IdleConfig self,
        float deltaTime
    )
    {
        if (IsSplitScreenActive())
        {
            RefreshBehaviourBatman(self);
        }

        var result = self.CheckForCloseProximity(deltaTime);

        if (
            !IsSplitScreenActive()
            || self.CSOptions.bStartCombatOnLOS == false
            || self.CSOptions.bCombatTriggered
            || self.HostController?.bInCombat == true
            || self.Villain == null
        )
        {
            return result;
        }

        var aiManager = Game.GetGameInfo()?.AIManager;
        if (aiManager == null || !aiManager.CanDoIdleConfigLOSCheck(self.HostController))
        {
            return result;
        }

        RetargetVillain(self.Villain, requireVisible: true);
        if (self.Villain.CanSeeTargetCombatPawn())
        {
            self.TriggerCombat(false);
        }

        return result;
    }

    [Redirect(typeof(RBM2Behaviour_IdleConfig), nameof(RBM2Behaviour_IdleConfig.TriggerCombat))]
    private static void TriggerCombatRedirect(
        RBM2Behaviour_IdleConfig self,
        bool bForceIntoCombat = false
    )
    {
        if (IsSplitScreenActive())
        {
            RefreshBehaviourBatman(self);
            RetargetVillain(self.Villain, requireVisible: false);
        }

        self.TriggerCombat(bForceIntoCombat);
        RebalanceCombatAssignmentsFor(self.Villain);
    }

    [Redirect(typeof(RPawnVillain), nameof(RPawnVillain.CanSeeTargetCombatPawn))]
    private static bool CanSeeTargetCombatPawnRedirect(RPawnVillain self)
    {
        if (!IsSplitScreenActive() || self.AIController?.bInCombat == true)
        {
            return self.CanSeeTargetCombatPawn();
        }

        RetargetVillain(self, requireVisible: true);
        return self.CanSeeTargetCombatPawn();
    }
}
