using System.Runtime.InteropServices;

namespace Etkramer.SplitScreen.Rendering;

// Fixes a bug with frustum culling where no primitives are rendered for P2
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

    public override void Main()
    {
        _ProcessPrimitiveCullingDetourBase = DetourUtil.NewDetour<ProcessPrimitiveCullingDelegate>(
            ProcessPrimitiveCullingOffset,
            ProcessPrimitiveCullingDetour
        );

        base.Main();
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
