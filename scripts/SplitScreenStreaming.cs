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
        var worldInfo = Game.GetWorldInfo();
        if (gri == null || worldInfo == null || !HasStreamingContext(gri))
        {
            return;
        }

        // WorldInfo is our Originator sentinel -- the engine never uses it for streaming,
        // so filtering by it gives us a clean ours-only view (kismet uses gri, volumes
        // use themselves, etc.).
        SyncExpandedStreaming(gri, worldInfo);
    }

    private static void RefreshLateAndFarLevelsDetour(IntPtr self)
    {
        // Let the original run so P1's _Late/_FAR flags are managed correctly
        // when CurrentLevel changes. We re-assert flags for P2+ afterwards.
        _refreshLateAndFarLevelsOriginal!.Invoke(self);

        var engine = Game.GetEngine();
        if (engine == null || engine.GamePlayers.Count <= 1)
        {
            return;
        }

        var gri = Game.GetGameRI();
        var worldInfo = Game.GetWorldInfo();
        if (gri == null || worldInfo == null)
        {
            return;
        }

        var interests = CollectExtraPlayerInterests(gri, engine);
        if (interests.Count == 0)
        {
            return;
        }

        // Same-frame fix-up: the engine just cleared bits on variants whose
        // BaseLevel didn't match P1's CurrentLevel. Set them back for P2+'s
        // interests before UpdateLevelStreaming reads the result.
        foreach (var ls in worldInfo.StreamingLevels)
        {
            if (ls == null)
            {
                continue;
            }

            var name = ls.PackageName.ToString();
            if (string.IsNullOrEmpty(name) || !IsLateOrFarVariant(name))
            {
                continue;
            }

            foreach (var interest in interests)
            {
                if (name.Length > interest.Length
                    && name[interest.Length] == '_'
                    && name.StartsWith(interest, StringComparison.OrdinalIgnoreCase))
                {
                    ls.bShouldBeLoaded = true;
                    ls.bShouldBeVisible = true;
                    ls.bHighPriorityLoadRequest = true;
                    break;
                }
            }
        }
    }

    private static bool IsLateOrFarVariant(string name)
    {
        return name.IndexOf("_late", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_far", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static HashSet<string> CollectExtraPlayerInterests(RGameRI gri, GameEngine engine)
    {
        var interests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            AddInterest(interests, volume.Level);
            foreach (var info in volume.OtherLevelsVisibleInfo)
            {
                AddInterest(interests, info.LevelName);
            }
            foreach (var info in volume.OtherLevelLODsVisibleInfo)
            {
                AddInterest(interests, info.LevelName);
            }
        }

        return interests;
    }

    private static void AddInterest(HashSet<string> interests, FName name)
    {
        var s = name.ToString();
        if (!string.IsNullOrEmpty(s) && !s.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            interests.Add(s);
        }
    }

    private static void SyncExpandedStreaming(RGameRI gri, WorldInfo originator)
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
        var ourFull = CollectOurs(originator, gri.StreamingLevels);
        var ourLOD = CollectOurs(originator, gri.StreamingLevelsLODs);
        foreach (var key in ourFull.Keys)
        {
            ourLOD.Remove(key);
        }

        Sync(gri, originator, ourFull, desiredFull, isLOD: false);
        Sync(gri, originator, ourLOD, desiredLOD, isLOD: true);
    }

    private static void RemoveOurEntry(
        TArray<RGameRI.FStreamingLevelInfo> engineList,
        FName levelName,
        WorldInfo originator
    )
    {
        for (var i = 0; i < engineList.Count; i++)
        {
            var info = engineList[i];
            if (
                info.Level.Index == levelName.Index
                && info.Level.Number == levelName.Number
                && ReferenceEquals(info.Originator, originator)
            )
            {
                engineList.RemoveAt(i);
                return;
            }
        }
    }

    private static Dictionary<string, FName> CollectOurs(
        WorldInfo originator,
        TArray<RGameRI.FStreamingLevelInfo> engineList
    )
    {
        var map = new Dictionary<string, FName>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in engineList)
        {
            if (!ReferenceEquals(info.Originator, originator))
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
        WorldInfo originator,
        Dictionary<string, FName> ours,
        Dictionary<string, DesiredStreamingRequest> desired,
        bool isLOD
    )
    {
        var engineList = isLOD ? gri.StreamingLevelsLODs : gri.StreamingLevels;

        foreach (var (levelKey, levelName) in ours)
        {
            if (desired.ContainsKey(levelKey))
            {
                continue;
            }

            // Bypass RemoveStreamingLevelRequest -- its UnloadLevel queue path
            // clears bShouldBeLoaded and overrides kismet's vote.
            RemoveOurEntry(engineList, levelName, originator);
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
                    originator,
                    false,
                    Vector3.Zero,
                    request.Borders
                );
            }
            else
            {
                gri.AddStreamingLevelRequest(
                    request.LevelName,
                    originator,
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
