using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.BmGame;

[Script]
public sealed class SplitScreenRenderingDOF : Script
{
    // RockOn bloom uses a shared half-size buffer. Force blur passes to
    // sample/write from (0, 0) so one view doesn't bleed into another.
    public const IntPtr RockGaussianBlurOffset = 0x5A1DE0;
    public const IntPtr RockDOFBlurOffset = 0x5A2DE0;

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
