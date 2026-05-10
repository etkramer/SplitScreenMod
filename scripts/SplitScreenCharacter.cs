using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

[Script]
public class SplitScreenCharacter : Script
{
    public override void Main()
    {
        // Load Robin
        Game.LoadPackage("Playable_RobinStoryDLC_SF");
        Game.FindObject<RAddContentPlayerCharacter>(
                    "Playable_RobinStoryDLC.Playable_RobinStoryDLC"
                )!
            .AddToRoot();

        // Load Catwoman
        Game.LoadPackage("Playable_Catwoman_SF");
        Game.FindObject<RAddContentPlayerCharacter>("Playable_Catwoman.Playable_Catwoman")!
            .AddToRoot();

        // Load Nightwing
        Game.LoadPackage("Playable_Nightwing_SF");
        Game.FindObject<RAddContentPlayerCharacter>("Playable_Nightwing.Playable_Nightwing")!
            .AddToRoot();

        Game.GetGameInfo().MaxPlayers = 4;
        Game.GetGameInfo().MaxPlayersAllowed = 4;
        RGameInfo.DefaultObject.MaxPlayers = 4;
        RGameInfo.DefaultObject.MaxPlayersAllowed = 4;

        base.Main();
    }

    public override void OnTick()
    {
        Game.GetGameInfo().MaxPlayers = 4;
        Game.GetGameInfo().MaxPlayersAllowed = 4;
        base.OnTick();
    }

    [ScriptComponent(AutoAttach = true)]
    class GameRIComponent : ScriptComponent<RGameRI>
    {
        [ComponentRedirect("PreBeginPlay")]
        public void PreBeginPlay()
        {
            Owner.PreBeginPlay();

            // Set P2 to Robin
            Owner.PlayerCharacters_1 = new RGameRI.FLoadedPlayerCharacter()
            {
                CharacterName = "Playable_RobinStoryDLC",
                DamageLevel = 0,
            };

            // Set P3 to Catwoman
            Owner.PlayerCharacters_2 = new RGameRI.FLoadedPlayerCharacter()
            {
                CharacterName = "Playable_Catwoman",
                DamageLevel = 0,
            };

            // Set P4 to Nightwing
            Owner.PlayerCharacters_3 = new RGameRI.FLoadedPlayerCharacter()
            {
                CharacterName = "Playable_Nightwing",
                DamageLevel = 0,
            };
        }
    }

    [ScriptComponent(AutoAttach = true)]
    class GameInfoComponent : ScriptComponent<RGameInfo>
    {
        [ComponentRedirect(nameof(RGameInfo.GetDefaultPlayerClass))]
        public Class GetDefaultPlayerClass(Controller C)
        {
            return C.PlayerNum switch
            {
                1 => RPawnPlayerRobinStoryDLC.StaticClass(),
                2 => RPawnPlayerCatwoman.StaticClass(),
                3 => RPawnPlayerNightwing.StaticClass(),
                _ => Owner.GetDefaultPlayerClass(C),
            };
        }
    }
}
