using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

// Adds support for swapping characters based on mods.toml
[Script]
public class SplitScreenCharacter : Script
{
    public override void Main()
    {
        RGameInfo.DefaultObject.MaxPlayers = 4;
        RGameInfo.DefaultObject.MaxPlayersAllowed = 4;

        var options = SplitScreen.Instance.Options;
        var playerConfigs = (TomlArray)options["players"];

        // Assign player slots from config
        for (var i = 0; i < playerConfigs.Count; i++)
        {
            var playerConfig = (TomlArray)playerConfigs[i]!;
            var characterName = (string)playerConfig[0]!;
            var meshName = $"{characterName}_{(string)playerConfig[1]!}";

            // Load packages
            Game.LoadPackage($"{characterName}_SF");
            Game.LoadPackage($"{meshName}_SF");

            // Set PlayerCharacters in CDO
            RGameRI.DefaultObject.PlayerCharacters[i] = new RGameRI.FLoadedPlayerCharacter()
            {
                CharacterName = characterName,
                MeshName = meshName,
                CharacterData = Game.FindObject<RAddContentPlayerCharacter>(
                    $"{characterName}.{characterName}"
                ),
                MeshData = Game.FindObject<RAddContentPlayerCharacterMesh>(
                    $"{meshName}.{meshName}"
                ),
            };
        }
    }

    public override void OnTick()
    {
        Game.GetGameInfo().MaxPlayers = 4;
        Game.GetGameInfo().MaxPlayersAllowed = 4;
        base.OnTick();
    }

    [ScriptComponent(AutoAttach = true)]
    class GameInfoComponent : ScriptComponent<RGameInfo>
    {
        [ComponentRedirect(nameof(RGameInfo.GetPlayerCharacterIndex))]
        public int GetPlayerCharacterIndex(Controller C)
        {
            return C.PlayerNum;
        }
    }
}
