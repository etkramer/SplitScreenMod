using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

// Loads world cells around P2 as well as P1
[Script]
public sealed class SplitScreenStreaming : Script
{
    private const int CenterSwitchConfirmTicks = 8;

    private static readonly Dictionary<string, FName> s_extraFullLoads = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly Dictionary<string, FName> s_extraLODLoads = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly Dictionary<int, PlayerCenterState> s_playerCenterStates = [];

    public override void OnEnterMenu()
    {
        s_playerCenterStates.Clear();
        ReleaseAllRequests();
    }

    public override void OnEnterGame()
    {
        s_extraFullLoads.Clear();
        s_extraLODLoads.Clear();
        s_playerCenterStates.Clear();
    }

    public override void OnUnload()
    {
        ReleaseAllRequests();
    }

    public override void OnTick()
    {
        var gri = Game.GetGameRI();
        if (gri == null || !HasStreamingContext(gri))
        {
            return;
        }

        SyncExpandedStreaming(gri);
    }

    // The engine's late/far refresh evicts dynamic actors (roads, NPCs, ...)
    // from levels deemed too far from the (single) player. With split-screen,
    // "far from P1" can still be "right next to P2", so we suppress the refresh
    // entirely while a second player is active. This replaces the old approach
    // of mutating bAllowLateAndFarLevelRefresh, which got captured in saves.
    [Redirect(typeof(RGameRI), nameof(RGameRI.RefreshLateAndFarLevels))]
    private static void RefreshLateAndFarLevelsRedirect(RGameRI self)
    {
        var engine = Game.GetEngine();
        if (engine != null && engine.GamePlayers.Count > 1)
        {
            return;
        }

        self.RefreshLateAndFarLevels();
    }

    private static void SyncExpandedStreaming(RGameRI gri)
    {
        var desiredFull = new Dictionary<string, DesiredStreamingRequest>(
            StringComparer.OrdinalIgnoreCase
        );
        var desiredLOD = new Dictionary<string, DesiredStreamingRequest>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var volume in GetExtraCenterVolumes(gri))
        {
            AddDesiredRequest(desiredFull, volume.Level, [], 0.0f);
            AddDesiredVisibleInfos(desiredFull, volume.OtherLevelsVisibleInfo);
            AddDesiredVisibleInfos(desiredLOD, volume.OtherLevelLODsVisibleInfo);
        }

        // A level requested at full detail subsumes a LOD request for the same
        // level; never ask for both or the engine ends up with two originators
        // fighting over the same streaming entry.
        foreach (var levelKey in new List<string>(desiredLOD.Keys))
        {
            if (desiredFull.ContainsKey(levelKey))
            {
                desiredLOD.Remove(levelKey);
            }
        }

        var fullDelta = SyncRequests(gri, s_extraFullLoads, desiredFull, isLOD: false);
        var lodDelta = SyncRequests(gri, s_extraLODLoads, desiredLOD, isLOD: true);

        if (fullDelta.Added + fullDelta.Removed + lodDelta.Added + lodDelta.Removed > 0)
        {
            Debug.Log(
                $"Split-screen streaming sync: full +{fullDelta.Added}/-{fullDelta.Removed} "
                    + $"({s_extraFullLoads.Count}), LOD +{lodDelta.Added}/-{lodDelta.Removed} "
                    + $"({s_extraLODLoads.Count})"
            );
        }
    }

    private static (int Added, int Removed) SyncRequests(
        RGameRI gri,
        Dictionary<string, FName> active,
        Dictionary<string, DesiredStreamingRequest> desired,
        bool isLOD
    )
    {
        var removed = 0;
        foreach (var levelKey in new List<string>(active.Keys))
        {
            if (desired.ContainsKey(levelKey))
            {
                continue;
            }

            var levelName = active[levelKey];
            if (isLOD)
            {
                gri.RemoveStreamingLevelLODRequest(levelName, gri);
            }
            else
            {
                gri.RemoveStreamingLevelRequest(levelName, gri);
            }

            active.Remove(levelKey);
            removed++;
        }

        var added = 0;
        foreach (var (levelKey, request) in desired)
        {
            if (active.ContainsKey(levelKey))
            {
                continue;
            }

            if (isLOD)
            {
                gri.AddStreamingLevelLODRequest(
                    request.LevelName,
                    gri,
                    false,
                    Vector3.Zero,
                    request.Borders
                );
            }
            else
            {
                gri.AddStreamingLevelRequest(
                    request.LevelName,
                    gri,
                    false,
                    Vector3.Zero,
                    request.Borders,
                    request.RoadHeight
                );
            }

            active[levelKey] = request.LevelName;
            added++;
        }

        return (added, removed);
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

    private static List<RLevelVolume> GetExtraCenterVolumes(RGameRI gri)
    {
        var byLevel = new Dictionary<string, RLevelVolume>(StringComparer.OrdinalIgnoreCase);
        var engine = Game.GetEngine();
        if (engine == null)
        {
            return [];
        }

        // P1's center is handled by the engine's normal streaming machinery, so
        // we don't add it ourselves. We still resolve P1's volume so any other
        // player sharing it gets filtered out below (no need to duplicate).
        var p1Pawn = engine.GamePlayers.Count > 0 ? engine.GamePlayers[0]?.Actor?.Pawn : null;
        var p1Volume = p1Pawn != null && p1Pawn.Health > 0
            ? FindPlayerCenterVolume(gri, p1Pawn)
            : null;

        for (var i = 1; i < engine.GamePlayers.Count; i++)
        {
            var pawn = engine.GamePlayers[i]?.Actor?.Pawn;
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

            if (p1Volume != null && volume.Level == p1Volume.Level)
            {
                continue;
            }

            var levelKey = volume.Level.ToString();
            if (!byLevel.ContainsKey(levelKey))
            {
                byLevel[levelKey] = volume;
            }
        }

        return [.. byLevel.Values];
    }

    private static RLevelVolume? FindPlayerCenterVolume(RGameRI gri, Pawn pawn)
    {
        RLevelVolume? best = null;
        var bestPriority = float.MinValue;

        foreach (var volume in gri.LevelVolumeList)
        {
            if (volume == null || !IsPlayerCenterCandidate(volume))
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

        // If the player has actually left the stable volume, switch immediately.
        var stillInsideStable = stable.Encompasses(pawn) || stable.EncompassesPoint(pawn.Location);
        if (!stillInsideStable)
        {
            s_playerCenterStates[playerIndex] = new PlayerCenterState(candidate, null, 0);
            return candidate;
        }

        // Otherwise, require the new candidate to be stable for N ticks before switching.
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

        return left.Level == right.Level;
    }

    private static bool IsPlayerCenterCandidate(RLevelVolume volume)
    {
        return volume.IsOverworldVolume() && volume.bUsedByLevelVisibilityVolume;
    }

    private static void AddDesiredVisibleInfos(
        Dictionary<string, DesiredStreamingRequest> desired,
        TArray<RLevelVolume.FVisibleLevelInfo> infos
    )
    {
        foreach (var info in infos)
        {
            // The engine retains the Borders TArray we pass to Add*Request, so
            // we must hand it a managed copy — the volume-owned source can be
            // reallocated out from under it and dereferenced as garbage later.
            AddDesiredRequest(desired, info.LevelName, CloneBorders(info.Borders), info.RoadHeight);
        }
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

    private static void ReleaseAllRequests()
    {
        var gri = Game.GetGameRI();
        if (gri != null)
        {
            foreach (var levelName in s_extraFullLoads.Values)
            {
                gri.RemoveStreamingLevelRequest(levelName, gri);
            }

            foreach (var levelName in s_extraLODLoads.Values)
            {
                gri.RemoveStreamingLevelLODRequest(levelName, gri);
            }
        }

        s_extraFullLoads.Clear();
        s_extraLODLoads.Clear();
        s_playerCenterStates.Clear();
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
}
