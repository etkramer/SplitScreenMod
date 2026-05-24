#nullable disable

using System.Numerics;
using BmSDK.BmGame;

namespace Etkramer.SplitScreen.Gameplay;

public static class SplitScreenCombatGrouping
{
    private const float DistanceScoreScale = 0.001f;
    private const float SamePylonBonus = 5000.0f;
    private const float CurrentTargetBonus = 1500.0f;
    private const float VisibleEntrantBonus = 10000.0f;
    private static readonly List<HashSet<RPawnPlayer>> SharedCombatPlayerGroups = [];

    private static bool IsSplitScreenActive()
    {
        return Game.GetEngine()?.GamePlayers.Count >= 2;
    }

    private static bool IsHiddenPlayer(RPawnPlayer player)
    {
        return player?.Controller is RPlayerController playerController
            && playerController.IsHidden();
    }

    private static bool IsActiveLocalPlayer(RPawnPlayer player)
    {
        return player != null && player.Health > 0 && !IsHiddenPlayer(player);
    }

    private static bool IsPlayerInCombat(RPawnPlayer player)
    {
        return player?.Controller is RPlayerControllerCombat playerController
            && playerController.IsInCombat();
    }

    private static bool SharesPylon(RPawnCombat left, RPawnCombat right)
    {
        return left?.CurrentPylon != null && left.CurrentPylon == right?.CurrentPylon;
    }

    private static HashSet<RPawnPlayer> FindSharedCombatPlayerGroup(RPawnPlayer player)
    {
        if (player == null)
        {
            return null;
        }

        for (var i = 0; i < SharedCombatPlayerGroups.Count; i++)
        {
            if (SharedCombatPlayerGroups[i].Contains(player))
            {
                return SharedCombatPlayerGroups[i];
            }
        }

        return null;
    }

    private static bool PlayersShareCombatGroup(RPawnPlayer left, RPawnPlayer right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (left == right)
        {
            return true;
        }

        var sharedGroup = FindSharedCombatPlayerGroup(left);
        return sharedGroup != null && sharedGroup.Contains(right);
    }

    private static void CleanupSharedCombatPlayerGroups(List<RPawnPlayer> localPlayers)
    {
        var localPlayerSet = new HashSet<RPawnPlayer>(localPlayers);

        for (var i = SharedCombatPlayerGroups.Count - 1; i >= 0; i--)
        {
            var sharedGroup = SharedCombatPlayerGroups[i];
            var removedPlayers = new List<RPawnPlayer>();

            foreach (var player in sharedGroup)
            {
                if (!localPlayerSet.Contains(player) || !IsPlayerInCombat(player))
                {
                    removedPlayers.Add(player);
                }
            }

            for (var j = 0; j < removedPlayers.Count; j++)
            {
                sharedGroup.Remove(removedPlayers[j]);
            }

            if (sharedGroup.Count < 2)
            {
                SharedCombatPlayerGroups.RemoveAt(i);
            }
        }
    }

    private static void RegisterSharedCombatPlayers(List<RPawnPlayer> players)
    {
        if (players.Count < 2)
        {
            return;
        }

        HashSet<RPawnPlayer> mergedGroup = null;

        for (var i = SharedCombatPlayerGroups.Count - 1; i >= 0; i--)
        {
            var sharedGroup = SharedCombatPlayerGroups[i];
            var intersects = false;

            for (var j = 0; j < players.Count; j++)
            {
                if (sharedGroup.Contains(players[j]))
                {
                    intersects = true;
                    break;
                }
            }

            if (!intersects)
            {
                continue;
            }

            mergedGroup ??= [];
            foreach (var player in sharedGroup)
            {
                mergedGroup.Add(player);
            }

            SharedCombatPlayerGroups.RemoveAt(i);
        }

        mergedGroup ??= [];
        for (var i = 0; i < players.Count; i++)
        {
            mergedGroup.Add(players[i]);
        }

        if (mergedGroup.Count >= 2)
        {
            SharedCombatPlayerGroups.Add(mergedGroup);
        }
    }

    private static void AddSharedCombatPlayers(List<RPawnPlayer> players)
    {
        for (var i = 0; i < players.Count; i++)
        {
            var sharedGroup = FindSharedCombatPlayerGroup(players[i]);
            if (sharedGroup == null)
            {
                continue;
            }

            foreach (var player in sharedGroup)
            {
                AddUniquePlayer(players, player);
            }
        }
    }

    private static bool CombatantsShareGroup(RPawnVillain left, RPawnVillain right)
    {
        return SharesPylon(left, right)
            || PlayersShareCombatGroup(
                (RPawnPlayer)left.GetTargetPlayer(),
                (RPawnPlayer)right.GetTargetPlayer()
            );
    }

    private static bool IsPlayerLinkedToGroup(RPawnPlayer player, List<RPawnVillain> group)
    {
        if (player == null || group.Count == 0)
        {
            return false;
        }

        var manager = group[0].CombatManager;
        if (
            player.CurrentPylon != null
            && manager?.IsCombatActiveInPylon(player.CurrentPylon) == true
        )
        {
            for (var i = 0; i < group.Count; i++)
            {
                if (SharesPylon(group[i], player))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < group.Count; i++)
        {
            if (PlayersShareCombatGroup(player, (RPawnPlayer)group[i].GetTargetPlayer()))
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
            var player = engine.GamePlayers[i]?.Actor?.Pawn as RPawnPlayer;
            if (!IsActiveLocalPlayer(player))
            {
                continue;
            }

            players.Add(player);
        }

        return players;
    }

    private static List<RPawnVillain> GetCombatantsForManager(RBMCombatManager manager)
    {
        var combatants = new List<RPawnVillain>();
        if (manager == null)
        {
            return combatants;
        }

        var aiManager = Game.GetGameInfo()?.AIManager;
        if (aiManager == null)
        {
            return combatants;
        }

        for (var i = 0; i < aiManager.GlobalControllerInfoList.Count; i++)
        {
            var controller = aiManager.GlobalControllerInfoList[i].Controller;
            var villain = controller?.PawnVillain;
            if (controller == null || villain == null)
            {
                continue;
            }

            if (
                villain.CombatManager != manager
                || villain.Health <= 0
                || !villain.bHostile
                || !villain.bIsValidCombatant
                || !controller.bInCombat
            )
            {
                continue;
            }

            combatants.Add(villain);
        }

        return combatants;
    }

    private static bool GroupMatches(List<RPawnVillain> group, RPawnVillain villain)
    {
        for (var i = 0; i < group.Count; i++)
        {
            var groupMember = group[i];
            if (CombatantsShareGroup(groupMember, villain))
            {
                return true;
            }
        }

        return false;
    }

    private static List<List<RPawnVillain>> GroupCombatants(List<RPawnVillain> combatants)
    {
        var groups = new List<List<RPawnVillain>>();

        for (var i = 0; i < combatants.Count; i++)
        {
            var villain = combatants[i];
            var foundGroup = false;

            for (var g = 0; g < groups.Count; g++)
            {
                if (!GroupMatches(groups[g], villain))
                {
                    continue;
                }

                groups[g].Add(villain);
                foundGroup = true;
                break;
            }

            if (!foundGroup)
            {
                groups.Add([villain]);
            }
        }

        return groups;
    }

    private static RPawnPlayer FindCombatEntrant(RPawnVillain source)
    {
        var localPlayers = GetLocalPlayers();
        if (source == null || localPlayers.Count == 0)
        {
            return null;
        }

        var aiController = source.AIController;
        RPawnPlayer bestPlayer = null;
        var bestScore = float.MinValue;

        for (var i = 0; i < localPlayers.Count; i++)
        {
            var player = localPlayers[i];
            var score =
                -Vector3.DistanceSquared(source.Location, player.Location) * DistanceScoreScale;

            if (
                aiController != null
                && aiController.CalcVisibilityFor(player)
                    >= AlertInstance.VisibilityCategory.VISCat_SlowSpot
            )
            {
                score += VisibleEntrantBonus;
            }

            if (bestPlayer == null || score > bestScore)
            {
                bestPlayer = player;
                bestScore = score;
            }
        }

        return bestPlayer;
    }

    private static void AddUniquePlayer(List<RPawnPlayer> players, RPawnPlayer player)
    {
        if (!IsActiveLocalPlayer(player))
        {
            return;
        }

        for (var i = 0; i < players.Count; i++)
        {
            if (players[i] == player)
            {
                return;
            }
        }

        players.Add(player);
    }

    private static Vector3 GetGroupCenter(List<RPawnVillain> group)
    {
        var center = Vector3.Zero;
        for (var i = 0; i < group.Count; i++)
        {
            center += group[i].Location;
        }

        return center / group.Count;
    }

    private static bool HasAssignedVillains(RPawnPlayer player, List<RPawnVillain> group)
    {
        for (var i = 0; i < group.Count; i++)
        {
            if (group[i].GetTargetPlayer() == player)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlayerStillInGroup(RPawnPlayer player, List<RPawnVillain> group)
    {
        return HasAssignedVillains(player, group) && player?.CurrentPylon == null
            ? true
            : IsPlayerLinkedToGroup(player, group);
    }

    private static List<RPawnPlayer> GetActivePlayers(
        List<RPawnPlayer> localPlayers,
        List<RPawnVillain> group,
        RPawnPlayer entrant
    )
    {
        var activePlayers = new List<RPawnPlayer>();

        AddUniquePlayer(activePlayers, entrant);

        for (var i = 0; i < localPlayers.Count; i++)
        {
            var player = localPlayers[i];
            if (HasAssignedVillains(player, group) && IsPlayerStillInGroup(player, group))
            {
                AddUniquePlayer(activePlayers, player);
            }
        }

        if (activePlayers.Count > 0)
        {
            AddSharedCombatPlayers(activePlayers);
            return activePlayers;
        }

        RPawnPlayer bestPlayer = null;
        var bestDistance = float.MaxValue;
        var center = GetGroupCenter(group);

        for (var i = 0; i < localPlayers.Count; i++)
        {
            var player = localPlayers[i];
            var distance = Vector3.DistanceSquared(center, player.Location);
            if (bestPlayer == null || distance < bestDistance)
            {
                bestPlayer = player;
                bestDistance = distance;
            }
        }

        AddUniquePlayer(activePlayers, bestPlayer);
        return activePlayers;
    }

    private static int GetCurrentAssignmentCount(List<RPawnVillain> group, RPawnPlayer player)
    {
        var count = 0;
        for (var i = 0; i < group.Count; i++)
        {
            if (group[i].GetTargetPlayer() == player)
            {
                count++;
            }
        }

        return count;
    }

    private static RPawnPlayer FindQuotaPlayer(
        List<RPawnPlayer> activePlayers,
        Dictionary<RPawnPlayer, int> quotas,
        Vector3 center,
        bool findLowest
    )
    {
        RPawnPlayer bestPlayer = null;

        for (var i = 0; i < activePlayers.Count; i++)
        {
            var player = activePlayers[i];
            if (bestPlayer == null)
            {
                bestPlayer = player;
                continue;
            }

            var quota = quotas[player];
            var bestQuota = quotas[bestPlayer];
            if (findLowest)
            {
                if (
                    quota < bestQuota
                    || (
                        quota == bestQuota
                        && Vector3.DistanceSquared(center, player.Location)
                            < Vector3.DistanceSquared(center, bestPlayer.Location)
                    )
                )
                {
                    bestPlayer = player;
                }

                continue;
            }

            if (
                quota > bestQuota
                || (
                    quota == bestQuota
                    && Vector3.DistanceSquared(center, player.Location)
                        > Vector3.DistanceSquared(center, bestPlayer.Location)
                )
            )
            {
                bestPlayer = player;
            }
        }

        return bestPlayer;
    }

    private static float ScoreAssignment(RPawnVillain villain, RPawnPlayer player)
    {
        if (player == null)
        {
            return float.MinValue;
        }

        var score =
            -Vector3.DistanceSquared(villain.Location, player.Location) * DistanceScoreScale;

        if (SharesPylon(villain, player))
        {
            score += SamePylonBonus;
        }

        if (villain.GetTargetPlayer() == player)
        {
            score += CurrentTargetBonus;
        }

        return score;
    }

    private static Dictionary<RPawnPlayer, int> BuildRemainingQuota(
        List<RPawnPlayer> activePlayers,
        List<RPawnVillain> group
    )
    {
        var remainingQuota = new Dictionary<RPawnPlayer, int>();
        var center = GetGroupCenter(group);
        var assignedQuota = 0;

        for (var i = 0; i < activePlayers.Count; i++)
        {
            var player = activePlayers[i];
            var currentCount = GetCurrentAssignmentCount(group, player);
            remainingQuota[player] = currentCount;
            assignedQuota += currentCount;
        }

        while (assignedQuota < group.Count)
        {
            var player = FindQuotaPlayer(activePlayers, remainingQuota, center, findLowest: true);
            if (player == null)
            {
                break;
            }

            remainingQuota[player]++;
            assignedQuota++;
        }

        while (true)
        {
            var lowestQuotaPlayer = FindQuotaPlayer(
                activePlayers,
                remainingQuota,
                center,
                findLowest: true
            );
            var highestQuotaPlayer = FindQuotaPlayer(
                activePlayers,
                remainingQuota,
                center,
                findLowest: false
            );
            if (
                lowestQuotaPlayer == null
                || highestQuotaPlayer == null
                || remainingQuota[highestQuotaPlayer] - remainingQuota[lowestQuotaPlayer] <= 1
            )
            {
                break;
            }

            remainingQuota[highestQuotaPlayer]--;
            remainingQuota[lowestQuotaPlayer]++;
        }

        return remainingQuota;
    }

    private static RPawnPlayer ChooseAssignedPlayer(
        RPawnVillain villain,
        List<RPawnPlayer> activePlayers,
        Dictionary<RPawnPlayer, int> remainingQuota,
        bool restrictToQuota
    )
    {
        RPawnPlayer bestPlayer = null;
        var bestScore = float.MinValue;

        for (var i = 0; i < activePlayers.Count; i++)
        {
            var player = activePlayers[i];
            if (restrictToQuota && remainingQuota[player] <= 0)
            {
                continue;
            }

            var score = ScoreAssignment(villain, player);
            if (bestPlayer == null || score > bestScore)
            {
                bestPlayer = player;
                bestScore = score;
            }
        }

        return bestPlayer;
    }

    private static void RebalanceGroup(List<RPawnVillain> group, List<RPawnPlayer> activePlayers)
    {
        if (group.Count == 0 || activePlayers.Count == 0)
        {
            return;
        }

        var remainingQuota = BuildRemainingQuota(activePlayers, group);
        var orderedGroup = new List<RPawnVillain>(group);
        var pending = new List<RPawnVillain>();

        orderedGroup.Sort(
            (left, right) =>
            {
                return ScoreAssignment(right, (RPawnPlayer)right.GetTargetPlayer())
                    .CompareTo(ScoreAssignment(left, (RPawnPlayer)left.GetTargetPlayer()));
            }
        );

        for (var i = 0; i < orderedGroup.Count; i++)
        {
            var villain = orderedGroup[i];
            var currentTarget = (RPawnPlayer)villain.GetTargetPlayer();
            if (ShouldKeepCurrentAssignment(currentTarget, remainingQuota))
            {
                remainingQuota[currentTarget]--;
                continue;
            }

            pending.Add(villain);
        }

        for (var i = 0; i < pending.Count; i++)
        {
            var villain = pending[i];
            var assignedPlayer =
                ChooseAssignedPlayer(villain, activePlayers, remainingQuota, restrictToQuota: true)
                ?? ChooseAssignedPlayer(
                    villain,
                    activePlayers,
                    remainingQuota,
                    restrictToQuota: false
                );
            if (assignedPlayer == null)
            {
                continue;
            }

            if (remainingQuota[assignedPlayer] > 0)
            {
                remainingQuota[assignedPlayer]--;
            }

            if (villain.GetTargetPlayer() != assignedPlayer)
            {
                villain.SetTargetPlayer(assignedPlayer);
            }
        }
    }

    private static bool ShouldKeepCurrentAssignment(
        RPawnPlayer currentTarget,
        Dictionary<RPawnPlayer, int> remainingQuota
    )
    {
        return currentTarget != null
            && remainingQuota.TryGetValue(currentTarget, out var quota)
            && quota > 0;
    }

    private static bool GroupContainsVillain(List<RPawnVillain> group, RPawnVillain villain)
    {
        for (var i = 0; i < group.Count; i++)
        {
            if (group[i] == villain)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldIncludeEntrantForGroup(
        List<RPawnVillain> group,
        RPawnVillain sourceVillain,
        RPawnPlayer entrant
    )
    {
        return entrant == null ? false
            : GroupContainsVillain(group, sourceVillain) ? true
            : IsPlayerLinkedToGroup(entrant, group);
    }

    private static void RebalanceCombatGroup(
        List<RPawnPlayer> localPlayers,
        List<RPawnVillain> group,
        RPawnVillain sourceVillain,
        RPawnPlayer entrant
    )
    {
        var groupEntrant = ShouldIncludeEntrantForGroup(group, sourceVillain, entrant)
            ? entrant
            : null;
        var activePlayers = GetActivePlayers(localPlayers, group, groupEntrant);
        RegisterSharedCombatPlayers(activePlayers);
        RebalanceGroup(group, activePlayers);
    }

    private static void RebalanceCombatAssignments(
        RBMCombatManager manager,
        RPawnVillain sourceVillain,
        RPawnPlayer entrant
    )
    {
        var localPlayers = GetLocalPlayers();
        CleanupSharedCombatPlayerGroups(localPlayers);

        var combatants = GetCombatantsForManager(manager);
        if (localPlayers.Count < 2 || combatants.Count == 0)
        {
            return;
        }

        var groups = GroupCombatants(combatants);
        var sourceGroupIndex = -1;
        for (var i = 0; i < groups.Count; i++)
        {
            if (GroupContainsVillain(groups[i], sourceVillain))
            {
                sourceGroupIndex = i;
                break;
            }
        }

        if (sourceGroupIndex >= 0)
        {
            RebalanceCombatGroup(localPlayers, groups[sourceGroupIndex], sourceVillain, entrant);
        }

        for (var i = 0; i < groups.Count; i++)
        {
            if (i == sourceGroupIndex)
            {
                continue;
            }

            RebalanceCombatGroup(localPlayers, groups[i], sourceVillain, entrant);
        }
    }

    public static void RebalanceActiveCombatAssignments()
    {
        if (!IsSplitScreenActive())
        {
            return;
        }

        RebalanceCombatAssignments(
            Game.GetGameInfo()?.CombatManager,
            sourceVillain: null,
            entrant: null
        );
    }

    public static void RebalanceCombatAssignmentsFor(RPawnVillain villain)
    {
        if (!IsSplitScreenActive() || villain?.CombatManager == null)
        {
            return;
        }

        RebalanceCombatAssignments(villain.CombatManager, villain, FindCombatEntrant(villain));
    }
}
