using System.Numerics;
using BmSDK;
using BmSDK.BmGame;

namespace Etkramer.SplitScreen.World;

/// <summary>
/// Solo, the game slows the whole world while you fly the remote control batarang, so the
/// batarang is tuned to look right at that slowed speed. RemoveSlowMo keeps co-op at normal
/// speed, which made the batarang fly ~3x too fast. In co-op, slow down just the batarang by
/// the speed the game asked for instead. Solo is untouched.
/// The batarang places itself along its flight path rather than using Velocity, so we scale
/// how far it actually moved each tick and put it back at that point. Its 10 second control
/// timer gets the same slowdown, since it would otherwise end the flight before the range
/// limit does. Once the game asks for normal speed again the flight is over, so it's left
/// to carry on as it does solo.
/// </summary>
[ScriptComponent(AutoAttach = true)]
public sealed class SplitScreenBatarangComponent : ScriptComponent<RBatarangProjectile_Controllable>
{
    // Anything bigger in one tick is a reset or teleport, not flight
    const float MaxStep = 2000f;

    // The timer that ends the player's control of the batarang
    const string ControlTimerName = "HomingFinished";

    float _scale = 1f;
    bool _stretchedTimer;
    Vector3 _prev;
    bool _hasPrev;

    // Measures real movement so the log shows whether the slowdown took
    readonly System.Diagnostics.Stopwatch _clock = new();
    Vector3 _measureStart;
    bool _measured;

    // Restart the control timer with the time it would have had at the slowed speed
    void StretchControlTimer()
    {
        if (_stretchedTimer)
        {
            return;
        }

        foreach (var timer in Owner.Timers)
        {
            if (timer.FuncName.ToString() == ControlTimerName)
            {
                _stretchedTimer = true;
                Owner.SetTimer((timer.Rate - timer.Count) / _scale, false, ControlTimerName, Owner);
                return;
            }
        }
    }

    public override void OnTick()
    {
        if (!GameState.IsMultiplayer() || Owner.bDeleteMe)
        {
            return;
        }

        var loc = Owner.Location;
        if (_scale >= 1f)
        {
            var requested = RemoveSlowMoComponent.RequestedSpeed;
            if (requested <= 0.01f || requested >= 0.99f)
            {
                return;
            }

            _scale = requested;
            _prev = loc;
            _hasPrev = true;
            _measureStart = loc;
            _clock.Restart();
            Debug.Log($"SplitScreenBatarang: slowing remote batarang to x{_scale:0.###}");
            return;
        }

        if (RemoveSlowMoComponent.RequestedSpeed >= 0.99f)
        {
            Debug.Log(
                $"SplitScreenBatarang: flight over after {_clock.Elapsed.TotalSeconds:0.0}s, "
                    + $"{Vector3.Distance(loc, _measureStart):0} from the start"
            );
            _scale = 1f;
            _hasPrev = false;
            return;
        }

        StretchControlTimer();

        var step = loc - _prev;
        if (_hasPrev && step.Length() < MaxStep)
        {
            var corrected = _prev + step * _scale;
            if (!Owner.SetLocation(corrected))
            {
                Owner.Location = corrected;
            }
            loc = corrected;
        }

        _prev = loc;
        _hasPrev = true;

        if (!_measured && _clock.Elapsed.TotalSeconds >= 1.0)
        {
            _measured = true;
            var moved = Vector3.Distance(loc, _measureStart) / (float)_clock.Elapsed.TotalSeconds;
            Debug.Log($"SplitScreenBatarang: actually moved {moved:0}/s over the first second");
        }
    }
}
