using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.World;

// Door interactions assume there is only one player. Without these redirects,
// when P2 uses a door the engine plays the open-door animation on P1 (because
// the door's internals call WorldInfo.Game.GetPC() which always returns P1). The door
// also picks which way to send the player from P1's side of it, so with P1 already
// through, P2 was sent back into the room they were in.
[Script]
public class SplitScreenDoors : Script
{
    private static readonly Dictionary<
        RLevelTransitionDoorBase,
        RPlayerController
    > InteractingByDoor = [];

    [Redirect(
        typeof(RLevelTransitionDoorBase),
        nameof(RLevelTransitionDoorBase.Interact),
        AllowSubtypes = true
    )]
    private static void InteractRedirect(RLevelTransitionDoorBase self, RPlayerController PC)
    {
        InteractingByDoor[self] = PC;

        var engine = Game.GetEngine();
        var pcIndex = FindPlayerIndex(engine, PC);
        var saved = engine.GamePlayers[0];
        if (pcIndex > 0)
        {
            engine.GamePlayers[0] = engine.GamePlayers[pcIndex];
            engine.GamePlayers[pcIndex] = saved;
        }

        try
        {
            self.Interact(PC);
        }
        finally
        {
            if (pcIndex > 0)
            {
                engine.GamePlayers[pcIndex] = engine.GamePlayers[0];
                engine.GamePlayers[0] = saved;
            }

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
