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

    private static ProcessPrimitiveCullingDelegate? _ProcessPrimitiveCullingDetourBase =
        null;

    // RockOn bloom uses a shared half-size buffer. In split-screen, later passes
    // must stay in that buffer's coordinate space or one view bleeds into another.

    private const int RockOnBloomClearBranchOffset = 0x110BF1;
    private const int RockOnGatherPass2SourceXOffset = 0x111575;
    private const int RockOnGatherPass2SourceYOffset = 0x111592;
    public const IntPtr RockGaussianBlurOffset = 0x5A1DE0;
    public const IntPtr RockDOFBlurOffset = 0x5A2DE0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RockGaussianBlurDelegate(
        IntPtr view, int x, int y, int w, int h,
        int bloomScale, int oneF, int one,
        int uvX, int uvY, int uvW, int uvH,
        int p12, int p13, int p14, int p15, int p16, int p17
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RockDOFBlurDelegate(
        IntPtr view, int flag, int dofEnabled, int motionBlurEnabled, int sizeX,
        int x, int y, int w, int h,
        int bloomScale, int oneF, int one,
        int uvX, int uvY, int uvW, int uvH
    );

    private static RockGaussianBlurDelegate? _gaussianBlurOriginal;
    private static RockDOFBlurDelegate? _dofBlurOriginal;

    public override void Main()
    {
        PatchRockOnBloomClear();
        PatchRockOnGatherPass2Offsets();

        _gaussianBlurOriginal = DetourUtil.NewDetour<RockGaussianBlurDelegate>(
            RockGaussianBlurOffset,
            GaussianBlurDetour
        );

        _dofBlurOriginal = DetourUtil.NewDetour<RockDOFBlurDelegate>(
            RockDOFBlurOffset,
            DOFBlurDetour
        );

        _ProcessPrimitiveCullingDetourBase =
            DetourUtil.NewDetour<ProcessPrimitiveCullingDelegate>(
                ProcessPrimitiveCullingOffset,
                ProcessPrimitiveCullingDetour
            );

        base.Main();
    }

    private static void GaussianBlurDetour(
        IntPtr view, int x, int y, int w, int h,
        int bloomScale, int oneF, int one,
        int uvX, int uvY, int uvW, int uvH,
        int p12, int p13, int p14, int p15, int p16, int p17
    )
    {
        _gaussianBlurOriginal!.Invoke(
            view, 0, 0, w, h,
            bloomScale, oneF, one,
            uvX, uvY, uvW, uvH,
            p12, p13, p14, p15, p16, p17
        );
    }

    private static void DOFBlurDetour(
        IntPtr view, int flag, int dofEnabled, int motionBlurEnabled, int sizeX,
        int x, int y, int w, int h,
        int bloomScale, int oneF, int one,
        int uvX, int uvY, int uvW, int uvH
    )
    {
        _dofBlurOriginal!.Invoke(
            view, flag, dofEnabled, motionBlurEnabled, sizeX,
            0, 0, w, h,
            bloomScale, oneF, one,
            uvX, uvY, uvW, uvH
        );
    }

    /// <summary>
    /// Always clears the shared half-size bloom buffer for each view.
    /// </summary>
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

    /// <summary>
    /// Forces the second bloom gather pass to sample from source (0, 0).
    /// </summary>
    private static unsafe void PatchRockOnGatherPass2Offsets()
    {
        PatchInstruction(
            RockOnGatherPass2SourceXOffset,
            expected0: 0xD1,
            expected1: 0xF9,
            replacement0: 0x33,
            replacement1: 0xC9,
            "RockOn bloom gather patch (source X)"
        );

        PatchInstruction(
            RockOnGatherPass2SourceYOffset,
            expected0: 0xD1,
            expected1: 0xF8,
            replacement0: 0x33,
            replacement1: 0xC0,
            "RockOn bloom gather patch (source Y)"
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

        if (!VirtualProtect((IntPtr)patchAddr, 2, 0x40, out var oldProtect))
        {
            Debug.LogWarning($"{label}: VirtualProtect failed");
            return;
        }

        patchAddr[0] = replacement0;
        patchAddr[1] = replacement1;

        VirtualProtect((IntPtr)patchAddr, 2, oldProtect, out _);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(
        IntPtr lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect
    );

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
