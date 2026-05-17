using System.Numerics;
using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

// Loads world cells around P1 that would normally be LOD'd, and prevents
// non-P1 pawns from hijacking RLevelVolume activation.
[Script]
public sealed class SplitScreenStreaming : Script
{
    private const IntPtr AddStreamingLevelLODRequestOffset = 0x86FE70;
    private const IntPtr AddStreamingLevelRequestOffset = 0x871C70;
    private const IntPtr RemoveStreamingLevelLODRequestOffset = 0x86AF70;
    private const IntPtr RemoveStreamingLevelRequestOffset = 0x86FB80;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void AddLODDelegate(
        IntPtr self,
        int levelIdx,
        int levelNum,
        IntPtr originator,
        int blockOnLoad,
        int posX,
        int posY,
        int posZ,
        IntPtr borders
    );

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void AddFullDelegate(
        IntPtr self,
        int levelIdx,
        int levelNum,
        IntPtr originator,
        int blockOnLoad,
        int posX,
        int posY,
        int posZ,
        IntPtr borders,
        int roadHeight
    );

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void RemoveDelegate(IntPtr self, int levelIdx, int levelNum, IntPtr originator);

    private static AddLODDelegate? _addLODOriginal;
    private static AddFullDelegate? _addFullOriginal;
    private static RemoveDelegate? _removeLODOriginal;
    private static RemoveDelegate? _removeFullOriginal;

    // Re-entry guard: AddStreamingLevelRequest internally calls AddStreamingLevelLODRequest,
    // and RemoveStreamingLevelRequest internally calls RemoveStreamingLevelLODRequest.
    // Without this, our redirection would loop.
    [ThreadStatic]
    private static bool _inRedirect;

    public override void Main()
    {
        _addLODOriginal = DetourUtil.NewDetour<AddLODDelegate>(
            AddStreamingLevelLODRequestOffset,
            AddLODDetour
        );
        _addFullOriginal = DetourUtil.NewDetour<AddFullDelegate>(
            AddStreamingLevelRequestOffset,
            AddFullPassthrough
        );
        _removeLODOriginal = DetourUtil.NewDetour<RemoveDelegate>(
            RemoveStreamingLevelLODRequestOffset,
            RemoveLODDetour
        );
        _removeFullOriginal = DetourUtil.NewDetour<RemoveDelegate>(
            RemoveStreamingLevelRequestOffset,
            RemoveFullPassthrough
        );

        base.Main();
    }

    private static bool ShouldPromote()
    {
        var engine = Game.GetEngine();
        return engine != null && engine.GamePlayers.Count > 1;
    }

    private static void AddLODDetour(
        IntPtr self,
        int levelIdx,
        int levelNum,
        IntPtr originator,
        int blockOnLoad,
        int posX,
        int posY,
        int posZ,
        IntPtr borders
    )
    {
        if (_inRedirect || !ShouldPromote())
        {
            _addLODOriginal!.Invoke(
                self,
                levelIdx,
                levelNum,
                originator,
                blockOnLoad,
                posX,
                posY,
                posZ,
                borders
            );
            return;
        }

        // Reroute through the full path. Its internal AddStreamingLevelLODRequest call
        // re-enters this detour with the guard set, so the LOD entry still gets added.
        _inRedirect = true;
        try
        {
            _addFullOriginal!.Invoke(
                self,
                levelIdx,
                levelNum,
                originator,
                blockOnLoad,
                posX,
                posY,
                posZ,
                borders,
                0
            );
        }
        finally
        {
            _inRedirect = false;
        }
    }

    private static void AddFullPassthrough(
        IntPtr self,
        int levelIdx,
        int levelNum,
        IntPtr originator,
        int blockOnLoad,
        int posX,
        int posY,
        int posZ,
        IntPtr borders,
        int roadHeight
    )
    {
        _addFullOriginal!.Invoke(
            self,
            levelIdx,
            levelNum,
            originator,
            blockOnLoad,
            posX,
            posY,
            posZ,
            borders,
            roadHeight
        );
    }

    private static void RemoveLODDetour(IntPtr self, int levelIdx, int levelNum, IntPtr originator)
    {
        if (_inRedirect || !ShouldPromote())
        {
            _removeLODOriginal!.Invoke(self, levelIdx, levelNum, originator);
            return;
        }

        // Symmetric: RemoveStreamingLevelRequest's first act is to call the LOD remove,
        // so routing here keeps the LOD list cleanup intact while also clearing the
        // full entry we added during the Add path.
        _inRedirect = true;
        try
        {
            _removeFullOriginal!.Invoke(self, levelIdx, levelNum, originator);
        }
        finally
        {
            _inRedirect = false;
        }
    }

    private static void RemoveFullPassthrough(
        IntPtr self,
        int levelIdx,
        int levelNum,
        IntPtr originator
    )
    {
        _removeFullOriginal!.Invoke(self, levelIdx, levelNum, originator);
    }

    [Redirect(typeof(RLevelVolume), nameof(RLevelVolume.Touch))]
    private static void TouchRedirect(
        RLevelVolume self,
        Actor Other,
        PrimitiveComponent OtherComp,
        Vector3 HitLocation,
        Vector3 HitNormal
    )
    {
        if (IsExtraPlayerPawn(Other))
        {
            return;
        }
        
        self.Touch(Other, OtherComp, HitLocation, HitNormal);
    }

    [Redirect(typeof(RLevelVolume), nameof(RLevelVolume.UnTouch))]
    private static void UnTouchRedirect(RLevelVolume self, Actor Other)
    {
        if (IsExtraPlayerPawn(Other))
        {
            return;
        }

        self.UnTouch(Other);
    }

    private static bool IsExtraPlayerPawn(Actor? other)
    {
        if (other == null)
        {
            return false;
        }

        var engine = Game.GetEngine();
        if (engine == null)
        {
            return false;
        }

        for (var i = 1; i < engine.GamePlayers.Count; i++)
        {
            if (engine.GamePlayers[i]?.Actor?.Pawn == other)
            {
                return true;
            }
        }

        return false;
    }
}
