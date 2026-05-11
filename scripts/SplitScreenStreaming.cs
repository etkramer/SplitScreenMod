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

    public override void OnEnterMenu()
    {
        s_playerCenterStates.Clear();
        ReleaseAllRequests();
    }

    public override void OnEnterGame()
    {
        s_extraFullLoads.Clear();
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

    private static void SyncExpandedStreaming(RGameRI gri)
    {
        var desired = new Dictionary<string, DesiredStreamingRequest>(
            StringComparer.OrdinalIgnoreCase
        );
        AddDesiredRequestsForVolumes(desired, GetPlayerCenterVolumes(gri));

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

    private static List<PlayerCenterVolume> GetPlayerCenterVolumes(RGameRI gri)
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

            // P1 already gets the inner ring naturally; only widen for P2+.
            var includeOuterRing = i > 0;
            var levelKey = volume.Level.ToString();

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
            AddDesiredRequest(desired, info.LevelName, info.Borders, info.RoadHeight);
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

    private static void ReleaseAllRequests()
    {
        var gri = Game.GetGameRI();
        if (gri != null)
        {
            foreach (var levelName in s_extraFullLoads.Values)
            {
                gri.RemoveStreamingLevelRequest(levelName, gri);
            }
        }

        s_extraFullLoads.Clear();
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

    private readonly record struct PlayerCenterVolume(RLevelVolume Volume, bool IncludeOuterRing);
}
