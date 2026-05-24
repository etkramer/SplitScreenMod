using BmSDK.BmGame;
using BmSDK.Engine;

namespace Etkramer.SplitScreen.Characters;

// Adds support for swapping characters based on mods.toml
[Script]
public class SplitScreenCharacter : Script
{
    [ScriptComponent(AutoAttach = true)]
    class GameRIComponent : ScriptComponent<RGameRI>
    {
        [ComponentRedirect(nameof(RGameRI.PreBeginPlay))]
        public void PreBeginPlay()
        {
            Owner.PreBeginPlay();

            var options = SplitScreen.Instance.Options;
            var playerConfigs = (TomlArray)options["players"];

            // Assign player slots from config
            for (var i = 0; i < playerConfigs.Count; i++)
            {
                var playerConfig = (TomlArray)playerConfigs[i]!;
                var characterName = (string)playerConfig[0]!;
                var meshName = $"{characterName}_{(string)playerConfig[1]!}";

                Owner.PlayerCharacters[i + 1] = new RGameRI.FLoadedPlayerCharacter()
                {
                    CharacterName = characterName,
                    MeshName = meshName,
                };
            }
        }
    }

    [ScriptComponent(AutoAttach = true)]
    class GameInfoComponent : ScriptComponent<RGameInfo>
    {
        [ComponentRedirect(nameof(RGameInfo.GetPlayerCharacterIndex))]
        public int GetPlayerCharacterIndex(Controller C)
        {
            if (C is RPlayerController rpc && rpc.IsSplitscreenPlayer(out _))
            {
                return rpc.GetMultiplayerIndex();
            }

            return Owner.GetPlayerCharacterIndex(C);
        }
    }
}
