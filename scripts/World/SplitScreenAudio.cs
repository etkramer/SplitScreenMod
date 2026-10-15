using System.Numerics;
using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.World;

// The game gives Wwise a single listener (Wwise listener 0), at a player's body facing
// where their camera looks. Each player's view updates it in turn, so it ends up on the
// last player (P2 with 2 players), and sounds far from them, like P1's punches and
// gunshots, go silent. Wwise supports up to 8 listeners, so this puts listeners 1-3 on
// the other players the same way, and lets every sound the game's listener can hear be
// heard by theirs too. Wwise uses the loudest listener for each sound. The game also
// skips creating sounds that are out of range of its listener, so that range check
// counts the other players' listeners too.
[Script]
public sealed class SplitScreenAudio : Script
{
    // Wwise's own listener functions, which the game exports by name
    private const string SetActiveListenersExport =
        "?SetActiveListeners@SoundEngine@AK@@YA?AW4AKRESULT@@IK@Z";
    private const string SetListenerPositionExport =
        "?SetListenerPosition@SoundEngine@AK@@YA?AW4AKRESULT@@ABUAkListenerPosition@@K@Z";

    // The game's Unreal-to-Wwise conversion: (-X, Z, Y) scaled by 0.01
    private const float WwiseScale = 0.01f;

    private const int MaxPlayers = 4;

    // Where unused listeners are parked, far out of earshot of everything
    private static readonly Vector3 ParkedLocation = new(0f, 0f, 10_000_000f);

    [StructLayout(LayoutKind.Sequential)]
    private struct AkListenerPosition
    {
        public Vector3 Front;
        public Vector3 Top;
        public Vector3 Position;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetActiveListenersDelegate(uint gameObject, uint listenerMask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetListenerPositionDelegate(ref AkListenerPosition position, uint index);

    // The game's own range check, run before it creates a sound source: is this position
    // within this radius of one of its listeners? It only knows its own listener, so
    // sounds near the other players were never created at all. Found by its code (the
    // start of the function, through its loop over the game's 3 listener records),
    // since its address differs between builds.
    private static readonly byte[] IsInListenerRangeCode =
    [
        0xF3, 0x0F, 0x10, 0x5C, 0x24, 0x08, 0x53, 0x8B, 0x5C, 0x24, 0x18, 0x55, 0x8B, 0x6C, 0x24, 0x14,
        0x56, 0x8B, 0x74, 0x24, 0x10, 0x57, 0x8B, 0x7C, 0x24, 0x20, 0x33, 0xD2, 0xB8, 0x01, 0x00, 0x00,
        0x00, 0xF3, 0x0F, 0x59, 0xDB, 0x81, 0xC1, 0x5C, 0x02, 0x00, 0x00, 0xEB, 0x03, 0x8D, 0x49, 0x00,
    ];

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int IsInListenerRangeDelegate(
        IntPtr self,
        IntPtr position,
        float radius,
        int listenerFlags,
        int needWwiseListener,
        int stopAtFirst
    );

    private static IsInListenerRangeDelegate? s_isInListenerRangeBase;
    private static IsInListenerRangeDelegate? s_isInListenerRangeDetour;

    // Where the extra listeners are, in game units, and how many are in use
    private static readonly Vector3[] s_extraLocations = new Vector3[MaxPlayers];
    private static int s_extraCount;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    private static SetActiveListenersDelegate? s_setActiveListenersBase;
    private static SetActiveListenersDelegate? s_setActiveListenersDetour;
    private static SetListenerPositionDelegate? s_setListenerPosition;

    // Listeners added to every sound that the game's listener can hear
    private static uint s_extraListeners;

    // Extra listeners that have been placed on a player since they were last parked
    private static uint s_placedListeners;

    public override void Main()
    {
        var module = GetModuleHandleW(null);
        var setActive = GetProcAddress(module, SetActiveListenersExport);
        var setPosition = GetProcAddress(module, SetListenerPositionExport);
        if (module == IntPtr.Zero || setActive == IntPtr.Zero || setPosition == IntPtr.Zero)
        {
            Debug.LogWarning("SplitScreenAudio: Wwise listener functions not found, skipping");
            return;
        }

        s_setListenerPosition = Marshal.GetDelegateForFunctionPointer<SetListenerPositionDelegate>(
            setPosition
        );
        s_setActiveListenersDetour = SetActiveListenersDetour;
        s_setActiveListenersBase = DetourUtil.NewDetour<SetActiveListenersDelegate>(
            (IntPtr)((nint)setActive - (nint)module),
            s_setActiveListenersDetour
        );

        HookRangeCheck(module);

        Debug.Log("SplitScreenAudio: listener hooks active");
        base.Main();
    }

    private static unsafe void HookRangeCheck(IntPtr module)
    {
        var offset = FindCode(module, IsInListenerRangeCode);
        if (offset < 0)
        {
            Debug.LogWarning("SplitScreenAudio: range check not found, sounds far from the last player may stay silent");
            return;
        }

        s_isInListenerRangeDetour = IsInListenerRangeDetour;
        s_isInListenerRangeBase = DetourUtil.NewDetour<IsInListenerRangeDelegate>(
            (IntPtr)offset,
            s_isInListenerRangeDetour
        );
        Debug.Log($"SplitScreenAudio: range check found at +0x{offset:X}");
    }

    // Offset of the only place this code appears in the game's executable sections, or -1
    private static unsafe int FindCode(IntPtr module, byte[] code)
    {
        var image = (byte*)module;
        var peHeader = image + *(int*)(image + 0x3C);
        var sectionCount = *(ushort*)(peHeader + 6);
        var optionalHeaderSize = *(ushort*)(peHeader + 20);
        var section = peHeader + 24 + optionalHeaderSize;

        var found = -1;
        for (var s = 0; s < sectionCount; s++, section += 40)
        {
            const uint Executable = 0x20000000;
            if ((*(uint*)(section + 36) & Executable) == 0)
            {
                continue;
            }

            var start = *(int*)(section + 12);
            var size = *(int*)(section + 8);
            var span = new ReadOnlySpan<byte>(image + start, size);
            var at = 0;
            while (true)
            {
                var index = span[at..].IndexOf(new ReadOnlySpan<byte>(code));
                if (index < 0)
                {
                    break;
                }

                if (found >= 0)
                {
                    return -1;
                }

                found = start + at + index;
                at += index + 1;
            }
        }

        return found;
    }

    private static unsafe int IsInListenerRangeDetour(
        IntPtr self,
        IntPtr position,
        float radius,
        int listenerFlags,
        int needWwiseListener,
        int stopAtFirst
    )
    {
        var result = s_isInListenerRangeBase!(
            self,
            position,
            radius,
            listenerFlags,
            needWwiseListener,
            stopAtFirst
        );

        // Only for "near any listener" checks (stopAtFirst); the other mode asks whether
        // it's near all of them
        if (result != 0 || stopAtFirst == 0 || s_extraCount == 0 || position == IntPtr.Zero)
        {
            return result;
        }

        // Also in range if it's close enough to one of the other players' listeners
        var location = *(Vector3*)position;
        var radiusSquared = radius * radius;
        for (var i = 0; i < s_extraCount; i++)
        {
            if (Vector3.DistanceSquared(location, s_extraLocations[i]) <= radiusSquared)
            {
                return 1;
            }
        }

        return result;
    }

    private static int SetActiveListenersDetour(uint gameObject, uint listenerMask)
    {
        if ((listenerMask & 1) != 0)
        {
            listenerMask |= s_extraListeners;
        }

        return s_setActiveListenersBase!(gameObject, listenerMask);
    }

    public override void OnTick()
    {
        base.OnTick();

        if (s_setListenerPosition == null)
        {
            return;
        }

        var players = Game.GetEngine().GamePlayers;
        var count = Math.Min(players.Count, MaxPlayers);

        // Listener 1 for P1, 2 for P2, 3 for P3 (the game's own listener has the last
        // player). Only sounds registered from now on get them, which is fine since the
        // players are added at the main menu.
        s_extraListeners = count > 1 ? ((1u << count) - 1) & ~1u : 0;
        s_extraCount = 0;

        for (var i = 1; i < MaxPlayers; i++)
        {
            var bit = 1u << i;
            if (
                i < count
                && players[i - 1]?.Actor is PlayerController pc
                && pc.PlayerCamera != null
            )
            {
                var pov = pc.PlayerCamera.CameraCache.POV;
                var location = pc.Pawn != null ? pc.Pawn.Location : pov.Location;
                SetListener(i, location, pov.Rotation);
                s_extraLocations[i - 1] = location;
                s_extraCount = i;
                s_placedListeners |= bit;
            }
            else if ((s_placedListeners & bit) != 0)
            {
                // Player removed: move their listener out of earshot so sounds that
                // still list it don't play from where they were
                SetListener(i, ParkedLocation, default);
                s_placedListeners &= ~bit;
            }
        }
    }

    private static void SetListener(int index, Vector3 location, Rotator rotation)
    {
        // Unreal's rotation matrix: X axis is forward, Z axis is up
        var pitch = rotation.Pitch * MathF.PI / 180f;
        var yaw = rotation.Yaw * MathF.PI / 180f;
        var roll = rotation.Roll * MathF.PI / 180f;
        var (sp, cp) = MathF.SinCos(pitch);
        var (sy, cy) = MathF.SinCos(yaw);
        var (sr, cr) = MathF.SinCos(roll);

        var front = new Vector3(cp * cy, cp * sy, sp);
        var top = new Vector3(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);

        var position = new AkListenerPosition
        {
            Front = ToWwise(front),
            Top = ToWwise(top),
            Position = ToWwise(location),
        };
        s_setListenerPosition!(ref position, (uint)index);
    }

    private static Vector3 ToWwise(Vector3 v) => new(-v.X * WwiseScale, v.Z * WwiseScale, v.Y * WwiseScale);
}
