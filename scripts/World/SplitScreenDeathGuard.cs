using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.World;

// Story scenes only expect one Batman. When one starts (e.g. Talia's death in the
// Monarch Theatre), an extra player can be squashed by P1 or the scene, or left in
// a part of the map that's no longer loaded and fall out of the world, and their
// death is a game over for everyone. In co-op, players no longer kill each other by
// overlapping, and an extra player who would die during a cutscene or by falling out
// of the world is healed and brought to P1 instead. Any other death
// of an extra player is a game over through P1. Every player death and game over is
// logged with its cause. Solo is untouched.
[Script]
public class SplitScreenDeathGuard : Script
{
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.Died), AllowSubtypes = true)]
    private static bool DiedRedirect(RPawnPlayer self, Controller killer, Class damageType, Vector3 hitLocation)
    {
        var index = PlayerIndex(self);
        if (!GameState.IsMultiplayer() || index < 0)
        {
            return self.Died(killer, damageType, hitLocation);
        }

        var cause = damageType?.Name.ToString() ?? "none";
        var reason = index > 0 ? RescueReason(self, cause) : null;
        Debug.Log(
            $"SplitScreenDeathGuard: P{index + 1} died: cause={cause}, killer={killer?.Name.ToString() ?? "none"}, "
                + $"{Describe(self)}{(reason != null ? $"; saving them ({reason})" : "")}"
        );

        if (reason != null)
        {
            Rescue(self);
            return false;
        }

        // The game only runs the game over for P1, so an extra player's death left the
        // screen black for good. The extra player dies for real (so it's clear who went
        // down), and P1's controller goes into its dead state without P1's pawn dying,
        // which runs the normal fade, game over and checkpoint reload.
        if (
            index > 0
            && Game.GetEngine().GamePlayers[0]?.Actor is RPlayerController p1
            && p1.Pawn is RPawnPlayer p1Pawn
            && p1Pawn.Health > 0
        )
        {
            if (!self.Died(killer, damageType, hitLocation))
            {
                return false;
            }

            Debug.Log($"SplitScreenDeathGuard: P{index + 1} is down, game over through P1");
            s_gameOverPawn = p1Pawn;
            s_gameOverController = p1;
            s_gameOverKiller = killer;
            s_gameOverDamageType = damageType;
            s_gameOverDeadline = Environment.TickCount64 + GameOverTimeoutMs;
            p1.PawnDied(p1Pawn);
            return true;
        }

        return self.Died(killer, damageType, hitLocation);
    }

    // If P1's dead state doesn't start a game over in time, P1's pawn dies instead (the
    // old way), so an extra player's death can never leave everyone stuck
    private const long GameOverTimeoutMs = 10_000;
    private static RPawnPlayer? s_gameOverPawn;
    private static RPlayerController? s_gameOverController;
    private static Controller? s_gameOverKiller;
    private static Class? s_gameOverDamageType;
    private static long s_gameOverDeadline;

    public override void OnTick()
    {
        base.OnTick();

        if (s_gameOverPawn == null || Environment.TickCount64 < s_gameOverDeadline)
        {
            return;
        }

        var pawn = s_gameOverPawn;
        s_gameOverPawn = null;
        var p1 = s_gameOverController;
        if (!pawn.bDeleteMe && pawn.Health > 0 && p1 != null)
        {
            Debug.Log("SplitScreenDeathGuard: no game over through P1's controller, P1 goes down instead");
            if (pawn.Controller == null)
            {
                p1.Possess(pawn, false);
            }
            pawn.Health = 0;
            pawn.Died(s_gameOverKiller!, s_gameOverDamageType!, pawn.Location);
        }
    }

    // A player pawn moved into another (scene teleports, matinees) kills the one in the way
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.EncroachedBy), AllowSubtypes = true)]
    private static void EncroachedByRedirect(RPawnPlayer self, Actor other)
    {
        if (GameState.IsMultiplayer() && other is Pawn otherPawn && PlayerIndex(otherPawn) >= 0 && PlayerIndex(self) >= 0)
        {
            return;
        }

        self.EncroachedBy(other);
    }

    [Redirect(typeof(RPawn), nameof(RPawn.CrushedBy), AllowSubtypes = true)]
    private static void CrushedByRedirect(RPawn self, Pawn otherPawn)
    {
        if (GameState.IsMultiplayer() && PlayerIndex(otherPawn) >= 0 && PlayerIndex(self) >= 0)
        {
            return;
        }

        self.CrushedBy(otherPawn);
    }

    [Redirect(typeof(Pawn), nameof(Pawn.FellOutOfWorld), AllowSubtypes = true)]
    private static void FellOutOfWorldRedirect(Pawn self, Class dmgType)
    {
        var index = PlayerIndex(self);
        if (GameState.IsMultiplayer() && index > 0)
        {
            Debug.Log($"SplitScreenDeathGuard: P{index + 1} fell out of the world, {Describe(self)}; bringing to P1");
            Rescue(self);
            return;
        }

        self.FellOutOfWorld(dmgType);
    }

    [Redirect(typeof(Pawn), nameof(Pawn.OutsideWorldBounds), AllowSubtypes = true)]
    private static void OutsideWorldBoundsRedirect(Pawn self)
    {
        var index = PlayerIndex(self);
        if (GameState.IsMultiplayer() && index > 0)
        {
            Debug.Log($"SplitScreenDeathGuard: P{index + 1} outside world bounds, {Describe(self)}; bringing to P1");
            Rescue(self);
            return;
        }

        self.OutsideWorldBounds();
    }

    // Logging only, so a game over that isn't a death (e.g. a scripted fail) shows up
    [Redirect(typeof(RPlayerController), nameof(RPlayerController.TriggerGameOver), AllowSubtypes = true)]
    private static void TriggerGameOverRedirect(RPlayerController self)
    {
        if (GameState.IsMultiplayer())
        {
            s_gameOverPawn = null;
            Debug.Log(
                $"SplitScreenDeathGuard: game over triggered on P{ControllerIndex(self) + 1} (state {self.GetStateName()}, "
                    + $"pawn={self.Pawn?.Name.ToString() ?? "none"}); {DescribeAll()}"
            );
        }

        self.TriggerGameOver();
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.GameOver), AllowSubtypes = true)]
    private static void GameOverRedirect(RPlayerController self, FString movieName)
    {
        if (GameState.IsMultiplayer())
        {
            Debug.Log($"SplitScreenDeathGuard: game over movie '{movieName}' on P{ControllerIndex(self) + 1}; {DescribeAll()}");
        }

        self.GameOver(movieName);
    }

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.PawnDied), AllowSubtypes = true)]
    private static void PawnDiedRedirect(RPlayerController self, Pawn inPawn)
    {
        if (GameState.IsMultiplayer())
        {
            Debug.Log(
                $"SplitScreenDeathGuard: P{ControllerIndex(self) + 1} PawnDied({inPawn?.Name.ToString() ?? "none"}), "
                    + $"{(inPawn != null ? Describe(inPawn) : "")}"
            );
        }

        self.PawnDied(inPawn);
    }

    // Each player's pawn before they last let go of it
    static readonly Pawn?[] s_previousPawn = new Pawn?[4];

    [Redirect(typeof(RPlayerController), nameof(RPlayerController.UnPossess), AllowSubtypes = true)]
    private static void UnPossessRedirect(RPlayerController self)
    {
        var index = ControllerIndex(self);
        if (GameState.IsMultiplayer() && index >= 0)
        {
            Debug.Log($"SplitScreenDeathGuard: P{index + 1} UnPossess({self.Pawn?.Name.ToString() ?? "none"})");
            if (self.Pawn != null && index < s_previousPawn.Length)
            {
                s_previousPawn[index] = self.Pawn;
            }
        }

        self.UnPossess();
    }

    // Story scenes swap "the player" into a special pawn (e.g. Batman with the sword at
    // the Monarch Theatre) for every player in turn, so P2 took it away from P1, which
    // left P1 with nothing and gave a game over. An extra player keeps their own pawn
    // instead of taking one another player is using.
    [Redirect(typeof(RPlayerController), nameof(RPlayerController.Possess), AllowSubtypes = true)]
    private static void PossessRedirect(RPlayerController self, Pawn inPawn, bool bVehicleTransition)
    {
        var index = ControllerIndex(self);
        if (!GameState.IsMultiplayer() || index < 0)
        {
            self.Possess(inPawn, bVehicleTransition);
            return;
        }

        var owner = inPawn?.Controller != null ? ControllerIndex(inPawn.Controller) : -1;
        Debug.Log(
            $"SplitScreenDeathGuard: P{index + 1} Possess({inPawn?.Name.ToString() ?? "none"}"
                + $"{(owner >= 0 ? $", held by P{owner + 1}" : "")})"
        );

        if (index == 0 || owner < 0 || owner == index)
        {
            self.Possess(inPawn, bVehicleTransition);
            return;
        }

        // Back into their own pawn if it's still around, otherwise a fresh one next to the owner
        var previous = index < s_previousPawn.Length ? s_previousPawn[index] : null;
        if (previous != null && !previous.bDeleteMe && previous.Controller == null && previous.Health > 0)
        {
            Debug.Log($"SplitScreenDeathGuard: P{index + 1} keeps {previous.Name} instead of taking P{owner + 1}'s pawn");
            self.Possess(previous, bVehicleTransition);
            return;
        }

        Debug.Log($"SplitScreenDeathGuard: P{index + 1} gets a new pawn instead of taking P{owner + 1}'s pawn");
        Respawn(self, index);
    }

    // Spawn a new pawn for a player that has none and put it next to another player
    private static bool Respawn(RPlayerController pc, int index)
    {
        Pawn? buddy = null;
        var players = Game.GetEngine().GamePlayers;
        for (var i = 0; i < players.Count && buddy == null; i++)
        {
            if (i != index && players[i]?.Actor?.Pawn is { } other)
            {
                buddy = other;
            }
        }

        if (buddy == null || Game.GetGameInfo() is not RGameInfo gameInfo)
        {
            Debug.Log($"SplitScreenDeathGuard: no other player to respawn P{index + 1} next to, letting the game over happen");
            return false;
        }

        gameInfo.RestartPlayer(pc);
        if (pc.Pawn is not { } pawn)
        {
            Debug.Log($"SplitScreenDeathGuard: couldn't respawn P{index + 1}, letting the game over happen");
            return false;
        }

        var target = buddy.Location + new Vector3(0f, 0f, 50f);
        if (pawn is RPawn rpawn)
        {
            rpawn.SetLocationIgnoringCollision(target);
        }
        else
        {
            pawn.SetLocation(target);
        }
        pawn.SetRotation(buddy.Rotation);
        pawn.Velocity = default;

        Debug.Log($"SplitScreenDeathGuard: respawned P{index + 1} next to another player instead of a game over");
        return true;
    }

    private static int ControllerIndex(Controller controller)
    {
        var players = Game.GetEngine().GamePlayers;
        for (var i = 0; i < players.Count; i++)
        {
            if (ReferenceEquals(players[i]?.Actor, controller))
            {
                return i;
            }
        }
        return -1;
    }

    [Redirect(typeof(RGameInfo), nameof(RGameInfo.GameOver), AllowSubtypes = true)]
    private static void GameInfoGameOverRedirect(RGameInfo self, float timeTillFade, bool playerDied)
    {
        if (GameState.IsMultiplayer())
        {
            Debug.Log($"SplitScreenDeathGuard: GameInfo.GameOver(playerDied={playerDied}); {DescribeAll()}");
        }

        self.GameOver(timeTillFade, playerDied);
    }

    // Why an extra player's death should be undone, or null to let it happen
    private static string? RescueReason(Pawn pawn, string cause)
    {
        var players = Game.GetEngine().GamePlayers;
        if (players[0]?.Actor is RPlayerController p1)
        {
            if (p1.bCinematicMode || p1.bForceCinematicMode || p1.IsMatineePlaing())
            {
                return "P1 is in a cutscene";
            }
        }

        if (
            cause.Contains("Fell", StringComparison.OrdinalIgnoreCase)
            || cause.Contains("KillZ", StringComparison.OrdinalIgnoreCase)
            || cause.Contains("Crush", StringComparison.OrdinalIgnoreCase)
            || cause.Contains("Encroach", StringComparison.OrdinalIgnoreCase)
            || cause.Contains("Telefrag", StringComparison.OrdinalIgnoreCase)
        )
        {
            return $"cause {cause}";
        }

        return null;
    }

    // Heal and put next to P1
    private static void Rescue(Pawn pawn)
    {
        pawn.Health = Math.Max(pawn.Health, pawn.HealthMax);

        var p1Pawn = Game.GetEngine().GamePlayers[0]?.Actor?.Pawn;
        if (p1Pawn == null || ReferenceEquals(p1Pawn, pawn))
        {
            return;
        }

        if (Vector3.Distance(pawn.Location, p1Pawn.Location) > 200f || pawn.Location.Z < p1Pawn.Location.Z - 500f)
        {
            var target = p1Pawn.Location + new Vector3(0f, 0f, 50f);
            if (pawn is RPawn rpawn)
            {
                rpawn.SetLocationIgnoringCollision(target);
            }
            else
            {
                pawn.SetLocation(target);
            }
            pawn.SetRotation(p1Pawn.Rotation);
        }

        pawn.Velocity = default;
    }

    private static int PlayerIndex(Pawn? pawn)
    {
        if (pawn == null)
        {
            return -1;
        }

        var players = Game.GetEngine().GamePlayers;
        for (var i = 0; i < players.Count; i++)
        {
            if (ReferenceEquals(players[i]?.Actor?.Pawn, pawn))
            {
                return i;
            }
        }
        return -1;
    }

    private static string Describe(Pawn pawn)
    {
        var p1Pawn = Game.GetEngine().GamePlayers[0]?.Actor?.Pawn;
        var distance = p1Pawn != null ? Vector3.Distance(pawn.Location, p1Pawn.Location) : -1f;
        var cinematic = pawn.Controller is RPlayerController pc && (pc.bCinematicMode || pc.bForceCinematicMode);
        return $"health={pawn.Health}, at {pawn.Location}, {distance:0} from P1, physics={pawn.Physics}, cinematic={cinematic}";
    }

    private static string DescribeAll()
    {
        var players = Game.GetEngine().GamePlayers;
        var text = "";
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i]?.Actor?.Pawn is { } pawn)
            {
                text += $"P{i + 1}: {Describe(pawn)}. ";
            }
        }
        return text;
    }
}
