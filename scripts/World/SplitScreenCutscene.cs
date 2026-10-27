using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.World;

// Cutscenes lock every player's controls, but the game only unlocks the player it
// thinks of as "the" player (P1) afterwards, so P2 can be left frozen. Once P1 is
// free again, release any other player still locked, and bring them over if the
// cutscene moved P1 far away. Only players the cutscene also locked are brought over:
// the slow-motion finish of a fight only locks P1, and P2 can be across the city.
// Solo is untouched.
[Script]
public class SplitScreenCutscene : Script
{
    // Locked this long after a cutscene ends counts as stuck
    const long AfterCutsceneMs = 1000;

    // Locked this long with P1 free, without a cutscene, counts as stuck
    const long OtherwiseMs = 5000;

    // After a cutscene, players further apart than this get brought to P1
    const float MaxDistance = 5000f;

    static bool s_afterCutscene;
    static long s_p1FreeSince;
    static bool s_checkedDistance;
    static readonly long[] s_lockedSince = new long[4];
    static readonly bool[] s_lockedInCutscene = new bool[4];

    public override void OnTick()
    {
        base.OnTick();

        var players = Game.GetEngine().GamePlayers;
        if (players.Count < 2 || players[0]?.Actor is not RPlayerController p1)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (IsLocked(p1) || p1.IsMatineePlaing())
        {
            s_afterCutscene = p1.bCinematicMode || p1.bForceCinematicMode || p1.IsMatineePlaing();
            s_p1FreeSince = now;
            s_checkedDistance = false;
            Array.Clear(s_lockedSince);
            for (var i = 1; i < players.Count && i < s_lockedInCutscene.Length; i++)
            {
                if (players[i]?.Actor is RPlayerController other && (IsLocked(other) || other.IsMatineePlaing()))
                {
                    s_lockedInCutscene[i] = true;
                }
            }
            return;
        }

        for (var i = 1; i < players.Count && i < s_lockedSince.Length; i++)
        {
            if (players[i]?.Actor is not RPlayerController pc || !IsLocked(pc))
            {
                s_lockedSince[i] = 0;
                continue;
            }

            if (s_lockedSince[i] == 0)
            {
                s_lockedSince[i] = now;
            }

            if (now - s_lockedSince[i] >= (s_afterCutscene ? AfterCutsceneMs : OtherwiseMs))
            {
                Release(i, pc, p1);
                s_lockedSince[i] = 0;
            }
        }

        if (s_afterCutscene && !s_checkedDistance && now - s_p1FreeSince >= AfterCutsceneMs)
        {
            s_checkedDistance = true;
            BringToP1(p1);
            Array.Clear(s_lockedInCutscene);
        }

        if (now - s_p1FreeSince > 3000)
        {
            s_afterCutscene = false;
            Array.Clear(s_lockedInCutscene);
        }
    }

    private static bool IsLocked(RPlayerController pc) =>
        pc.bCinematicMode || pc.bForceCinematicMode || pc.bIgnoreMoveInput > 0;

    private static void Release(int index, RPlayerController pc, RPlayerController p1)
    {
        var pawn = pc.Pawn;
        Debug.Log(
            $"SplitScreenCutscene: P{index + 1} stuck (cinematic={pc.bCinematicMode}, forced={pc.bForceCinematicMode}, "
                + $"ignoreMove={pc.bIgnoreMoveInput}, ignoreLook={pc.bIgnoreLookInput}, "
                + $"hidden={pawn?.bHidden}, physics={pawn?.Physics}), releasing"
        );

        if (pc.bForceCinematicMode)
        {
            pc.UnforceCinematicMode();
        }

        if (pc.bCinematicMode)
        {
            pc.SetCinematicMode(false, false, true, true, true, true, false);
        }

        pc.bIgnoreMoveInput = 0;
        pc.bIgnoreLookInput = 0;

        if (pawn != null && pawn.bHidden && p1.Pawn != null && !p1.Pawn.bHidden)
        {
            pawn.SetHidden(false);
        }
    }

    private static void BringToP1(RPlayerController p1)
    {
        var players = Game.GetEngine().GamePlayers;
        var p1Pawn = p1.Pawn;
        if (p1Pawn == null)
        {
            return;
        }

        for (var i = 1; i < players.Count; i++)
        {
            var pawn = players[i]?.Actor?.Pawn != null ? Game.GetPlayerPawn(i) : null;
            if (pawn == null || Vector3.Distance(pawn.Location, p1Pawn.Location) <= MaxDistance)
            {
                continue;
            }

            if (i < s_lockedInCutscene.Length && !s_lockedInCutscene[i])
            {
                continue;
            }

            Debug.Log($"SplitScreenCutscene: P{i + 1} left behind by cutscene, bringing to P1");
            pawn.SetLocationIgnoringCollision(p1Pawn.Location);
            pawn.SetRotation(p1Pawn.Rotation);
            pawn.Velocity = default;
        }
    }
}
