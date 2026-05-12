using BmSDK.BmGame;
using BmSDK.Engine;

// Adds skeletons to players in detective mode
[Script]
public class SplitScreenSkeleton : Script
{
    [ScriptComponent(AutoAttach = true)]
    class PlayerSkeletonComponent : ScriptComponent<RPawnPlayer>
    {
        [ComponentRedirect(nameof(RPawnPlayer.PostBeginPlay))]
        public void PostBeginPlay()
        {
            Owner.PostBeginPlay();

            var depthBiasData = new SkeletalMeshComponent.FDepthBiasData
            {
                DepthBias = -10,
                AlternateDepthBias = -10,
                DepthBiasCalculationType = SkeletalMeshComponent.EDepthBiasCalculationType.DEPTHBIASCALCULATIONTYPE_Constant,
                DepthBiasApplicationType = SkeletalMeshComponent.EDepthBiasApplicationType.DEPTHBIASAPPLICATIONTYPE_Screen,
                DepthBiasMinDistanceFromCameraPlaneOverride = 0,
                MinDepthBiasMultiplier = 1
            };

            var meshComponent = new SkeletalMeshComponent(Owner);
            meshComponent.SetSkeletalMesh(Game.FindObject<SkeletalMesh>("Skeletons.Mesh.Batman_Skeleton_Skin"));
            meshComponent.SetDepthBias(depthBiasData);
            meshComponent.SetDepthPriorityGroup(Scene.ESceneDepthPriorityGroup.SDPG_HighlightXray);
            meshComponent.SetParentAnimComponent(Owner.Mesh);
            meshComponent.SetOnlyXraySee(true);
            meshComponent.SetOwnerNoSee(true);

            Owner.AttachComponent(meshComponent);
        }
    }
}
