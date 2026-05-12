using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.BmGame;
using Process = System.Diagnostics.Process;

// Fixes a bug with the DOF/bloom effects where P1's bloom is rendered over P2's screen
[Script]
public sealed class SplitScreenRenderingDOF : Script
{
    // RockOn bloom uses a shared half-size buffer. Force blur passes to
    // sample/write from (0, 0) so one view doesn't bleed into another.
    public const IntPtr RockGaussianBlurOffset = 0x5A1DE0;
    public const IntPtr RockDOFBlurOffset = 0x5A2DE0;
    private const int RockOnBloomClearBranchOffset = 0x110BF1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RockGaussianBlurDelegate(
        IntPtr view,
        int x,
        int y,
        int w,
        int h,
        int bloomScale,
        int oneF,
        int one,
        int uvX,
        int uvY,
        int uvW,
        int uvH,
        int p12,
        int p13,
        int p14,
        int p15,
        int p16,
        int p17
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RockDOFBlurDelegate(
        IntPtr view,
        int flag,
        int dofEnabled,
        int motionBlurEnabled,
        int sizeX,
        int x,
        int y,
        int w,
        int h,
        int bloomScale,
        int oneF,
        int one,
        int uvX,
        int uvY,
        int uvW,
        int uvH
    );

    private static RockGaussianBlurDelegate? _gaussianBlurOriginal;
    private static RockDOFBlurDelegate? _dofBlurOriginal;

    public override void Main()
    {
        PatchRockOnBloomClear();

        _gaussianBlurOriginal = DetourUtil.NewDetour<RockGaussianBlurDelegate>(
            RockGaussianBlurOffset,
            GaussianBlurDetour
        );

        _dofBlurOriginal = DetourUtil.NewDetour<RockDOFBlurDelegate>(
            RockDOFBlurOffset,
            DOFBlurDetour
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

    private static void GaussianBlurDetour(
        IntPtr view,
        int x,
        int y,
        int w,
        int h,
        int bloomScale,
        int oneF,
        int one,
        int uvX,
        int uvY,
        int uvW,
        int uvH,
        int p12,
        int p13,
        int p14,
        int p15,
        int p16,
        int p17
    )
    {
        _gaussianBlurOriginal!.Invoke(
            view,
            0,
            0,
            w,
            h,
            bloomScale,
            oneF,
            one,
            uvX,
            uvY,
            uvW,
            uvH,
            p12,
            p13,
            p14,
            p15,
            p16,
            p17
        );
    }

    private static void DOFBlurDetour(
        IntPtr view,
        int flag,
        int dofEnabled,
        int motionBlurEnabled,
        int sizeX,
        int x,
        int y,
        int w,
        int h,
        int bloomScale,
        int oneF,
        int one,
        int uvX,
        int uvY,
        int uvW,
        int uvH
    )
    {
        _dofBlurOriginal!.Invoke(
            view,
            flag,
            dofEnabled,
            motionBlurEnabled,
            sizeX,
            0,
            0,
            w,
            h,
            bloomScale,
            oneF,
            one,
            uvX,
            uvY,
            uvW,
            uvH
        );
    }

    public override void OnTick()
    {
        // Bloom and DOF produce split-screen artifacts. Volume overrides kill
        // the baseline pipeline; per-camera RDOFManager overrides suppress the
        // zoom/gadget DOF paths which bypass volume settings.
        var engine = Game.GetEngine();
        if (engine.GamePlayers.Count >= 2)
        {
            var worldInfo = Game.GetWorldInfo();
            for (
                var v = worldInfo.HighestPriorityPostProcessVolume;
                v != null;
                v = v.NextLowerPriorityVolume
            )
            {
                var settings = v.Settings;

                settings.bOverride_Bloom_Scale = true;
                settings.Bloom_Scale = 0f;

                settings.bOverride_EnableDOF = true;
                settings.bEnableDOF = false;
                settings.bOverride_bEnableHighQualityDOF = true;
                settings.bEnableHighQualityDOF = false;
                settings.bOverride_DOF_MaxNearBlurAmount = true;
                settings.DOF_MaxNearBlurAmount = 0f;
                settings.bOverride_DOF_MaxFarBlurAmount = true;
                settings.DOF_MaxFarBlurAmount = 0f;
                settings.bOverride_DOF_MinBlurAmount = true;
                settings.DOF_MinBlurAmount = 0f;
                settings.bOverride_DOF_BlurKernelSize = true;
                settings.DOF_BlurKernelSize = 0f;

                v.Settings = settings;
            }

            foreach (var player in engine.GamePlayers)
            {
                var camera = (player.Actor as RPlayerController)?.PlayerCamera as R3rdPersonCamera;
                var dofManager = camera?.DOFManager;
                if (dofManager == null)
                {
                    continue;
                }

                dofManager.ResetBlurs();
                dofManager.Dof = DisableDof(dofManager.Dof);
                dofManager.OldDof = DisableDof(dofManager.OldDof);
                dofManager.ChangedDof = false;

                // Resonator patches PP directly,
                // bypassing the volume + DOFManager paths. Zero its DOF fields.
                camera!.ResonatorScreenScene_DOFNear = 0f;
                camera.ResonatorScreenScene_DOFFar = 0f;
                camera.ResonatorScreenScene_DOFFalloff = 0f;
                camera.ResonatorScreenScene_DOFRadius = 0f;
                camera.ResonatorScreenScene_DOFDistance = 0f;
            }
        }

        base.OnTick();
    }

    private static RDOFManager.FDofStruct DisableDof(RDOFManager.FDofStruct dof)
    {
        dof.bEnableDOF = false;
        dof.MaxNearBlurAmount = 0f;
        dof.MaxFarBlurAmount = 0f;
        dof.BlurKernelSize = 0f;
        return dof;
    }
}
