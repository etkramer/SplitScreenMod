using System.Runtime.InteropServices;
using Process = System.Diagnostics.Process;

// Split-screen culling only processes Views[0]. Re-run it for each extra view by
// temporarily exposing that view as Views[0].

[Script]
public sealed class SplitScreenRendering : Script
{
    public const IntPtr ProcessPrimitiveCullingOffset = 0x5A9330;

    private const int ViewsDataOffset = 80;
    private const int ViewsArrayNumOffset = 84;
    private const int ViewInfoStride = 6064;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void ProcessPrimitiveCullingDelegate(
        IntPtr self,
        IntPtr Primitives,
        int NumPrimitives,
        byte ValidViews,
        int Depth
    );

    private static ProcessPrimitiveCullingDelegate? _ProcessPrimitiveCullingDetourBase = null;

    // The shared half-size bloom buffer is conditionally cleared; force it
    // to always clear so per-view content doesn't leak between players.
    private const int RockOnBloomClearBranchOffset = 0x110BF1;

    public override void Main()
    {
        PatchRockOnBloomClear();

        _ProcessPrimitiveCullingDetourBase = DetourUtil.NewDetour<ProcessPrimitiveCullingDelegate>(
            ProcessPrimitiveCullingOffset,
            ProcessPrimitiveCullingDetour
        );

        base.Main();
    }

    private static unsafe void PatchRockOnBloomClear()
    {
        PatchInstruction(
            RockOnBloomClearBranchOffset,
            expected0: 0x74,
            expected1: 0x20,
            replacement0: 0x90,
            replacement1: 0x90,
            "RockOn bloom clear patch"
        );
    }

    private static unsafe void PatchInstruction(
        int offset,
        byte expected0,
        byte expected1,
        byte replacement0,
        byte replacement1,
        string label
    )
    {
        var baseAddr = Process.GetCurrentProcess().MainModule!.BaseAddress;
        var patchAddr = (byte*)(baseAddr + offset);

        if (patchAddr[0] != expected0 || patchAddr[1] != expected1)
        {
            Debug.LogWarning(
                $"{label}: unexpected bytes at patch site "
                    + $"({patchAddr[0]:X2} {patchAddr[1]:X2}), skipping"
            );
            return;
        }

        if (!PInvoke.VirtualProtect((IntPtr)patchAddr, 2, 0x40, out var oldProtect))
        {
            Debug.LogWarning($"{label}: VirtualProtect failed");
            return;
        }

        patchAddr[0] = replacement0;
        patchAddr[1] = replacement1;

        PInvoke.VirtualProtect((IntPtr)patchAddr, 2, oldProtect, out _);
    }

    private static unsafe void ProcessPrimitiveCullingDetour(
        IntPtr self,
        IntPtr Primitives,
        int NumPrimitives,
        byte ValidViews,
        int Depth
    )
    {
        // Let the original path process the first view normally.
        _ProcessPrimitiveCullingDetourBase!.Invoke(
            self,
            Primitives,
            NumPrimitives,
            ValidViews,
            Depth
        );

        // Only remap at the top level; recursive calls inherit the active view.
        if (Depth > 0)
        {
            return;
        }

        var viewDataField = (byte**)((byte*)self + ViewsDataOffset);
        var viewCountField = (int*)((byte*)self + ViewsArrayNumOffset);

        // Keep the view family count in sync with the temporary single-view remap.
        var familyViewCountField = (int*)((byte*)self + 8);

        var viewCount = *viewCountField;
        if (viewCount <= 1)
        {
            return;
        }

        var originalViewData = *viewDataField;
        var originalFamilyViewCount = *familyViewCountField;

        for (var viewIndex = 1; viewIndex < viewCount; viewIndex++)
        {
            // Remap the current view into slot 0 for one pass through the original.
            *viewDataField = originalViewData + (ViewInfoStride * viewIndex);
            *viewCountField = 1;
            *familyViewCountField = 1;

            _ProcessPrimitiveCullingDetourBase!.Invoke(
                self,
                Primitives,
                NumPrimitives,
                ValidViews,
                Depth
            );

            // Restore before the next view.
            *viewDataField = originalViewData;
            *viewCountField = viewCount;
            *familyViewCountField = originalFamilyViewCount;
        }
    }
}
