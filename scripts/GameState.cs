using BmSDK.BmGame;

namespace Etkramer.SplitScreen;

public static class GameState
{
    public static bool IsMultiplayer() => Game.GetEngine().GamePlayers.Count > 1;

    // Game.GetPlayerPawn goes by controller, so it finds the wrong player after rotating them
    public static RPawn GetPlayerPawn(int index) =>
        (Game.GetEngine().GamePlayers[index]?.Actor as RPlayerController)?.CombatPawn!;
}
