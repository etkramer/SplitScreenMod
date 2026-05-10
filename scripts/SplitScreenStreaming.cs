using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

[Script]
public sealed class SplitScreenStreaming : Script
{
    private const int CenterSwitchConfirmTicks = 8;

    private static readonly Dictionary<string, FName> s_extraFullLoads = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly Dictionary<int, PlayerCenterState> s_playerCenterStates = [];

    private static bool s_hasEnteredGame;
    private static bool s_hasCapturedLateFarRefresh;
    private static bool s_previousAllowLateFarRefresh;
    private static string s_lastCentersSummary = string.Empty;

    public override void OnEnterMenu()
    {
        s_hasEnteredGame = false;
        s_playerCenterStates.Clear();
        ReleaseAllRequests();
    }

    public override void OnEnterGame()
    {
        s_hasEnteredGame = true;
        s_extraFullLoads.Clear();
        s_playerCenterStates.Clear();
        s_hasCapturedLateFarRefresh = false;
        s_lastCentersSummary = string.Empty;
    }

    public override void OnUnload()
    {
        ReleaseAllRequests();
    }

    public override void OnTick()
    {
        if (!s_hasEnteredGame)
        {
            return;
        }

        var gri = Game.GetGameRI();
        if (gri == null)
        {
            return;
        }

        EnsureLateFarRefreshDisabled(gri);

        if (!HasStreamingContext(gri))
        {
            return;
        }

        SyncExpandedStreaming(gri);
    }

    private void SyncExpandedStreaming(RGameRI gri)
    {
        var desired = new Dictionary<string, DesiredStreamingRequest>(
            StringComparer.OrdinalIgnoreCase
        );
        var centers = new List<string>();
        var centerVolumes = GetPlayerCenterVolumes(gri, centers);

        AddDesiredRequestsForVolumes(desired, centerVolumes);

        var centersSummary = string.Join(", ", centers);
        if (!string.Equals(centersSummary, s_lastCentersSummary, StringComparison.Ordinal))
        {
            s_lastCentersSummary = centersSummary;
            Debug.Log($"Split-screen streaming centers: {centersSummary}");
        }

        var removed = 0;
        foreach (var levelKey in new List<string>(s_extraFullLoads.Keys))
        {
            if (desired.ContainsKey(levelKey))
            {
                continue;
            }

            gri.RemoveStreamingLevelRequest(s_extraFullLoads[levelKey], gri);
            s_extraFullLoads.Remove(levelKey);
            removed++;
        }

        var added = 0;
        foreach (var (levelKey, request) in desired)
        {
            if (s_extraFullLoads.ContainsKey(levelKey))
            {
                continue;
            }

            gri.AddStreamingLevelRequest(
                request.LevelName,
                gri,
                false,
                Vector3.Zero,
                request.Borders,
                request.RoadHeight
            );

            s_extraFullLoads[levelKey] = request.LevelName;
            added++;
        }

        if (added > 0 || removed > 0)
        {
            Debug.Log(
                $"Split-screen streaming sync: +{added}, -{removed}, active={s_extraFullLoads.Count}"
            );
        }
    }

    private static bool HasStreamingContext(RGameRI gri)
    {
        var engine = Game.GetEngine();
        if (engine == null || engine.GamePlayers.Count == 0 || gri.LevelVolumeList.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            var player = engine.GamePlayers[i];
            if (player?.Actor?.Pawn != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureLateFarRefreshDisabled(RGameRI gri)
    {
        if (!s_hasCapturedLateFarRefresh)
        {
            s_previousAllowLateFarRefresh = gri.bAllowLateAndFarLevelRefresh;
            s_hasCapturedLateFarRefresh = true;
        }

        if (gri.bAllowLateAndFarLevelRefresh)
        {
            gri.bAllowLateAndFarLevelRefresh = false;
        }
    }

    private static List<PlayerCenterVolume> GetPlayerCenterVolumes(
        RGameRI gri,
        List<string> centers
    )
    {
        var centerVolumes = new Dictionary<string, PlayerCenterVolume>(
            StringComparer.OrdinalIgnoreCase
        );
        var engine = Game.GetEngine();
        if (engine == null)
        {
            return [];
        }

        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            var player = engine.GamePlayers[i];
            var pawn = player?.Actor?.Pawn;
            if (pawn == null || pawn.Health <= 0)
            {
                continue;
            }

            var volume = ResolvePlayerCenterVolume(i, gri, pawn);
            if (volume == null)
            {
                s_playerCenterStates.Remove(i);
                continue;
            }

            var includeOuterRing = i > 0;
            var levelKey = volume.Level.ToString();
            centers.Add(
                includeOuterRing ? $"P{i + 1}:{levelKey} (+outer)" : $"P{i + 1}:{levelKey}"
            );

            if (centerVolumes.TryGetValue(levelKey, out var existing))
            {
                if (includeOuterRing && !existing.IncludeOuterRing)
                {
                    centerVolumes[levelKey] = new PlayerCenterVolume(volume, true);
                }

                continue;
            }

            centerVolumes[levelKey] = new PlayerCenterVolume(volume, includeOuterRing);
        }

        return [.. centerVolumes.Values];
    }

    private static RLevelVolume? FindPlayerCenterVolume(RGameRI gri, Pawn pawn)
    {
        RLevelVolume? best = null;
        var bestPriority = float.MinValue;

        foreach (var volume in gri.LevelVolumeList)
        {
            if (volume == null)
            {
                continue;
            }

            if (!IsPlayerCenterCandidate(volume))
            {
                continue;
            }

            if (!volume.Encompasses(pawn) && !volume.EncompassesPoint(pawn.Location))
            {
                continue;
            }

            var priority = volume.Priority;
            if (volume.bIsLevelActive)
            {
                priority += 1000000.0f;
            }

            if (best == null || priority > bestPriority)
            {
                best = volume;
                bestPriority = priority;
            }
        }

        return best;
    }

    private static RLevelVolume? ResolvePlayerCenterVolume(int playerIndex, RGameRI gri, Pawn pawn)
    {
        var candidate = FindPlayerCenterVolume(gri, pawn);
        if (candidate == null)
        {
            return null;
        }

        if (!s_playerCenterStates.TryGetValue(playerIndex, out var state) || state.StableVolume == null)
        {
            s_playerCenterStates[playerIndex] = new PlayerCenterState(candidate, null, 0);
            return candidate;
        }

        var stable = state.StableVolume;
        if (IsSameCenterVolume(stable, candidate))
        {
            s_playerCenterStates[playerIndex] = new PlayerCenterState(stable, null, 0);
            return stable;
        }

        var stillInsideStable = stable.Encompasses(pawn) || stable.EncompassesPoint(pawn.Location);
        if (!stillInsideStable)
        {
            s_playerCenterStates[playerIndex] = new PlayerCenterState(candidate, null, 0);
            return candidate;
        }

        var pendingTicks = IsSameCenterVolume(state.PendingVolume, candidate)
            ? state.PendingTicks + 1
            : 1;
        if (pendingTicks < CenterSwitchConfirmTicks)
        {
            s_playerCenterStates[playerIndex] = new PlayerCenterState(stable, candidate, pendingTicks);
            return stable;
        }

        s_playerCenterStates[playerIndex] = new PlayerCenterState(candidate, null, 0);
        return candidate;
    }

    private static bool IsSameCenterVolume(RLevelVolume? left, RLevelVolume? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.Level.ToString().Equals(right.Level.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayerCenterCandidate(RLevelVolume volume)
    {
        return volume != null && volume.IsOverworldVolume() && volume.bUsedByLevelVisibilityVolume;
    }

    private static void AddDesiredRequestsForVolumes(
        Dictionary<string, DesiredStreamingRequest> desired,
        List<PlayerCenterVolume> centerVolumes
    )
    {
        foreach (var center in centerVolumes)
        {
            var volume = center.Volume;

            AddDesiredRequest(desired, volume.Level, [], 0.0f);

            AddDesiredRequestsFromVisibleInfos(desired, volume.OtherLevelsVisibleInfo);

            if (center.IncludeOuterRing)
            {
                AddDesiredRequestsFromVisibleInfos(desired, volume.OtherLevelLODsVisibleInfo);
            }
        }
    }

    private static void AddDesiredRequestsFromVisibleInfos(
        Dictionary<string, DesiredStreamingRequest> desired,
        TArray<RLevelVolume.FVisibleLevelInfo> infos
    )
    {
        foreach (var info in infos)
        {
            AddDesiredRequest(desired, info.LevelName, CloneBorders(info.Borders), info.RoadHeight);
        }
    }

    private static void AddDesiredRequest(
        Dictionary<string, DesiredStreamingRequest> desired,
        FName levelName,
        TArray<RGameRI.FBorderInfo> borders,
        float roadHeight
    )
    {
        var levelKey = levelName.ToString();
        if (
            string.IsNullOrWhiteSpace(levelKey)
            || levelKey.Equals("None", StringComparison.OrdinalIgnoreCase)
            || desired.ContainsKey(levelKey)
        )
        {
            return;
        }

        desired[levelKey] = new DesiredStreamingRequest(levelName, borders, roadHeight);
    }

    private static TArray<RGameRI.FBorderInfo> CloneBorders(TArray<RGameRI.FBorderInfo> source)
    {
        var copy = new TArray<RGameRI.FBorderInfo>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy[i] = source[i];
        }

        return copy;
    }

    private static float Saturate(float value)
    {
        return value <= 0.0f ? 0.0f
            : value >= 1.0f ? 1.0f
            : value;
    }

    private void ReleaseAllRequests()
    {
        var gri = Game.GetGameRI();
        if (gri != null)
        {
            foreach (var levelName in s_extraFullLoads.Values)
            {
                gri.RemoveStreamingLevelRequest(levelName, gri);
            }

            if (s_hasCapturedLateFarRefresh)
            {
                gri.bAllowLateAndFarLevelRefresh = s_previousAllowLateFarRefresh;
            }
        }

        s_extraFullLoads.Clear();
        s_playerCenterStates.Clear();
        s_hasCapturedLateFarRefresh = false;
        s_lastCentersSummary = string.Empty;
    }

    private readonly record struct DesiredStreamingRequest(
        FName LevelName,
        TArray<RGameRI.FBorderInfo> Borders,
        float RoadHeight
    );

    private readonly record struct PlayerCenterState(
        RLevelVolume? StableVolume,
        RLevelVolume? PendingVolume,
        int PendingTicks
    );

    private readonly record struct PlayerCenterVolume(RLevelVolume Volume, bool IncludeOuterRing);
}
