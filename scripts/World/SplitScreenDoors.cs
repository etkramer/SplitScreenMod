using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.World;

// Door interactions assume there is only one player. Without these redirects,
// when P2 uses a door the engine plays the open-door animation on P1 (because
// the door's internals call WorldInfo.Game.GetPC() which always returns P1).
[Script]
public class SplitScreenDoors : Script
{
    private static readonly Dictionary<
        RLevelTransitionDoorBase,
        RPlayerController
    > InteractingByDoor = new();

    [Redirect(
        typeof(RLevelTransitionDoorBase),
        nameof(RLevelTransitionDoorBase.Interact),
        AllowSubtypes = true
    )]
    private static void InteractRedirect(RLevelTransitionDoorBase self, RPlayerController PC)
    {
        InteractingByDoor[self] = PC;
        try
        {
            self.Interact(PC);
        }
        finally
        {
            InteractingByDoor.Remove(self);
        }
    }

    [Redirect(
        typeof(RLevelTransitionDoorBase),
        nameof(RLevelTransitionDoorBase.TriggerPlayerAnim),
        AllowSubtypes = true
    )]
    private static void TriggerPlayerAnimRedirect(
        RLevelTransitionDoorBase self,
        RSpecialMoveConfig SpecialMove,
        RPawnPlayer.EnvironmentAnimationDirection AnimDir,
        SkeletalMeshComponent SyncSkelMeshComp,
        FName SyncSkelMeshAnim
    )
    {
        if (!InteractingByDoor.TryGetValue(self, out var pc) || pc == null)
        {
            self.TriggerPlayerAnim(SpecialMove, AnimDir, SyncSkelMeshComp, SyncSkelMeshAnim);
            return;
        }

        var engine = Game.GetEngine();
        var pcIndex = FindPlayerIndex(engine, pc);
        if (pcIndex <= 0)
        {
            self.TriggerPlayerAnim(SpecialMove, AnimDir, SyncSkelMeshComp, SyncSkelMeshAnim);
            return;
        }

        var saved = engine.GamePlayers[0];
        engine.GamePlayers[0] = engine.GamePlayers[pcIndex];
        engine.GamePlayers[pcIndex] = saved;
        try
        {
            self.TriggerPlayerAnim(SpecialMove, AnimDir, SyncSkelMeshComp, SyncSkelMeshAnim);
        }
        finally
        {
            engine.GamePlayers[pcIndex] = engine.GamePlayers[0];
            engine.GamePlayers[0] = saved;
        }
    }

    private static int FindPlayerIndex(GameEngine engine, RPlayerController pc)
    {
        for (var i = 0; i < engine.GamePlayers.Count; i++)
        {
            if (ReferenceEquals(engine.GamePlayers[i]?.Actor, pc))
            {
                return i;
            }
        }
        return -1;
    }
}
