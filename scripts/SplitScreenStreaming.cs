using System.Numerics;
using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

// Loads world cells around P2 in addition to P1
[Script]
public sealed class SplitScreenStreaming : Script
{
    public const IntPtr RefreshLateAndFarLevelsOffset = 0x85A510;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void RefreshLateAndFarLevelsDelegate(IntPtr self);

    private static RefreshLateAndFarLevelsDelegate? _refreshLateAndFarLevelsOriginal = null;

    public override void Main()
    {
        _refreshLateAndFarLevelsOriginal = DetourUtil.NewDetour<RefreshLateAndFarLevelsDelegate>(
            RefreshLateAndFarLevelsOffset,
            RefreshLateAndFarLevelsDetour
        );

        base.Main();
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

    private static void RefreshLateAndFarLevelsDetour(IntPtr self)
    {
        var engine = Game.GetEngine();
        if (engine != null && engine.GamePlayers.Count > 1)
        {
            return;
        }

        _refreshLateAndFarLevelsOriginal!.Invoke(self);
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

        // A Full request subsumes a LOD request for the same level.
        foreach (var levelKey in new List<string>(desiredLOD.Keys))
        {
            if (desiredFull.ContainsKey(levelKey))
            {
                desiredLOD.Remove(levelKey);
            }
        }

        // AddStreamingLevelRequest internally adds a LOD entry too, so a level
        // in ourFull also appears in StreamingLevelsLODs with our originator.
        // Exclude those from ourLOD so we don't drop them as stale -- they
        // belong to the Full request and get cleaned up when it's removed.
        var ourFull = CollectOurs(gri, gri.StreamingLevels);
        var ourLOD = CollectOurs(gri, gri.StreamingLevelsLODs);
        foreach (var key in ourFull.Keys)
        {
            ourLOD.Remove(key);
        }

        Sync(gri, ourFull, desiredFull, isLOD: false);
        Sync(gri, ourLOD, desiredLOD, isLOD: true);
    }

    private static Dictionary<string, FName> CollectOurs(
        RGameRI gri,
        TArray<RGameRI.FStreamingLevelInfo> engineList
    )
    {
        var map = new Dictionary<string, FName>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in engineList)
        {
            if (!ReferenceEquals(info.Originator, gri))
            {
                continue;
            }

            var key = info.Level.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            map[key] = info.Level;
        }

        return map;
    }

    private static void Sync(
        RGameRI gri,
        Dictionary<string, FName> ours,
        Dictionary<string, DesiredStreamingRequest> desired,
        bool isLOD
    )
    {
        foreach (var (levelKey, levelName) in ours)
        {
            if (desired.ContainsKey(levelKey))
            {
                continue;
            }

            if (isLOD)
            {
                gri.RemoveStreamingLevelLODRequest(levelName, gri);
            }
            else
            {
                gri.RemoveStreamingLevelRequest(levelName, gri);
            }
        }

        foreach (var (levelKey, request) in desired)
        {
            if (ours.ContainsKey(levelKey))
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
        }
    }

    private static bool HasStreamingContext(RGameRI gri)
    {
        if (gri.LevelStreamingLocked || gri.LevelStreamingInitialising)
        {
            return false;
        }

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
        var p1Volume =
            p1Pawn != null && p1Pawn.Health > 0 ? FindPlayerCenterVolume(gri, p1Pawn) : null;

        for (var i = 1; i < engine.GamePlayers.Count; i++)
        {
            var pawn = engine.GamePlayers[i]?.Actor?.Pawn;
            if (pawn == null || pawn.Health <= 0)
            {
                continue;
            }

            var volume = FindPlayerCenterVolume(gri, pawn);
            if (volume == null)
            {
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
            // we must hand it a managed copy -- the volume-owned source can be
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

    private readonly record struct DesiredStreamingRequest(
        FName LevelName,
        TArray<RGameRI.FBorderInfo> Borders,
        float RoadHeight
    );
}
