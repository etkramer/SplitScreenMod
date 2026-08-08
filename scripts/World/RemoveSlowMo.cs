using BmSDK.BmGame;

namespace Etkramer.SplitScreen.World;

[ScriptComponent(AutoAttach = true)]
public sealed class RemoveSlowMoComponent : ScriptComponent<RGameInfo>
{
    /// <summary>
    /// Overrides the game's internal mechanism for time dilation. This redirect makes sure game
    /// speed is unchanged during multiplayer gameplay.
    /// </summary>
    [ComponentRedirect(nameof(RGameInfo.SetGameSpeed))]
    public void SetGameSpeed(float dt)
    {
        if (Game.GetEngine().GamePlayers.Count < 2)
        {
            Owner.SetGameSpeed(dt);
        }
    }

    /// <summary>
    /// Sometimes the developers set time dilation directly without going through the detour.
    /// Normal game speed is forced here each tick to prevent inconsistencies.
    /// </summary>
    public override void OnTick()
    {
        if (Game.GetEngine().GamePlayers.Count > 1)
        {
            Owner.GameSpeed = 1.0f;
            Owner.WorldInfo.TimeDilation = 1.0f;
        }
    }
}
