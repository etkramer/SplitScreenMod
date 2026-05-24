using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;

namespace Etkramer.SplitScreen.Characters;

[ScriptComponent(AutoAttach = true)]
sealed class RestockWristDartComponent : ScriptComponent<RNightwingWristDart>
{
    static readonly float s_rechargeTime = RProjectileGadgetBase.DefaultObject.ReplenishTime;
    bool _replenishing;
    float _cooldown;

    /// <summary>
    /// Overrides function responsible for shooting aimed darts.
    /// Used to queue dart regeneration.
    /// </summary>
    [ComponentRedirect(nameof(RNightwingWristDart.FireDart))]
    void FireDart(Rotator rotation, Vector3 position)
    {
        if (Owner.Ammo > 0)
        {
            Owner.FireDart(rotation, position);
            OnDecrementAmmo();
        }
    }

    /// <summary>
    /// Overrides function responsible for shooting darts through the shortcut.
    /// Used to queue dart regeneration.
    /// </summary>
    [ComponentRedirect(nameof(RNightwingWristDart.QuickFireDart))]
    void QuickFireDart()
    {
        if (Owner.QuickFireTarget is not null)
        {
            Owner.QuickFireDart();
            OnDecrementAmmo();
        }
    }

    void OnDecrementAmmo()
    {
        if (Game.GetGameRI().IsOverworldGameplay())
        {
            ScheduleIncrementAmmo();
        }
    }

    void ScheduleIncrementAmmo()
    {
        if (_replenishing)
        {
            return;
        }

        _cooldown = s_rechargeTime;
        _replenishing = true;
    }

    public override void OnTick()
    {
        if (_replenishing)
        {
            _cooldown -= Game.GetDeltaTime();
            if (_cooldown <= 0)
            {
                _replenishing = false;
                IncrementAmmo();
            }
        }
    }

    void IncrementAmmo()
    {
        if (!Game.GetGameRI().IsOverworldGameplay())
        {
            return;
        }

        if (Owner.Ammo < Owner.MaxAmmo)
        {
            Owner.Ammo++;
            Owner.UpdateGadgetHUDParams();
            if (Owner.Ammo < Owner.MaxAmmo)
            {
                ScheduleIncrementAmmo();
            }
        }
    }

    /// <summary>
    /// Overrides the function called when loading a new level in interiors.
    /// This is used to completely restore ammo like other gadgets do.
    /// </summary>
    [ComponentRedirect(nameof(RNightwingWristDart.OnRoomChange))]
    void OnRoomChange()
    {
        if (!Game.GetGameRI().IsOverworldGameplay())
        {
            Owner.RestockAmmo();
        }
    }

    /// <summary>
    /// Overrides the function used by the CheatManager to max out ammo.
    /// This function doesn't actually set ammo by default though.
    /// </summary>
    [ComponentRedirect(nameof(RNightwingWristDart.RestockAmmo))]
    void RestockAmmo()
    {
        Owner.Ammo = Owner.MaxAmmo;
        Owner.NumHeadShotsInRound = 0;
        Owner.UpdateGadgetHUDParams();
    }

    /// <summary>
    /// Overrides the function called when a new map is loaded
    /// (no matter if it's in the overworld or not). Here we schedule
    /// regeneration if in the overworld.
    /// </summary>
    [ComponentRedirect(nameof(RNightwingWristDart.OnLevelChange))]
    void OnLevelChange()
    {
        if (Game.GetGameRI().IsOverworldGameplay())
        {
            if (Owner.Ammo < Owner.MaxAmmo)
            {
                ScheduleIncrementAmmo();
            }
        }
    }
}
