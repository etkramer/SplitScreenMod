namespace Etkramer.SplitScreen;

public static class GameState
{
    public static bool IsMultiplayer() => Game.GetEngine().GamePlayers.Count > 1;
}
