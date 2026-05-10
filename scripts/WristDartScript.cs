using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

[Script]
public class WristDartScript : Script
{
    public override void Main()
    {
        Game.LoadPackage("Playable_Batman_SF");
        Game.LoadPackage("Playable_Nightwing_SF");
        Game.LoadPackage("Court_B1_Audio");

        // Give Batman all Nightwing AnimSets
        var pawnCdo = (RPawnPlayerBm)RPawnPlayerBm.StaticClass().DefaultObject;
        pawnCdo.AnimationConfig.GadgetAnimSets.Add(Game.FindObject<AnimSet>("Anim_Nightwing_Gadget.Code.NW_Ricochet"));
        pawnCdo.AnimationConfig.GadgetAnimSets.Add(Game.FindObject<AnimSet>("Anim_Nightwing_Gadget.Code.NW_Electromagnetic_Blast"));
        pawnCdo.AnimationConfig.GadgetAnimSets.Add(Game.FindObject<AnimSet>("Anim_Nightwing_Gadget.Code.NW_Wrist_Dart"));
        pawnCdo.AnimationConfig.GadgetAnimSets.Add(Game.FindObject<AnimSet>("Anim_Nightwing_Gadget.Code.NW_Wingding"));
        pawnCdo.AnimationConfig.GadgetAnimSets.Add(Game.FindObject<AnimSet>("Anim_Nightwing_Gadget.Code.NW_LineLauncher"));
        pawnCdo.AddToRoot();

        var playerCharacter = Game.FindObject<RAddContentPlayerCharacter>("Playable_Batman.Playable_Batman")!;
        //playerCharacter.GadgetsPC_0 = "RNightwingWristDart";
        playerCharacter.GadgetsPC_4 = "RNightwingWristDart";
        playerCharacter.AddToRoot();

        base.Main();
    }

    //public override void OnKeyDown(Keys key)
    //{
    //    // Debug actions based on key press.
    //    if (key == Keys.B)
    //    {
    //        GiveWristDart();
    //    }
    //}

    [Redirect(typeof(RPawnPlayerBm), "AddDefaultInventory")]
    public static void AddDefaultInventoryRedirect(RPawnPlayerBm self)
    {
        Game.LoadPackage("Playable_Nightwing_SF");

        self.AddDefaultInventory();
        self.GiveGadget(RNightwingWristDart.StaticClass(), false, true);

        var pawn = Game.GetPlayerPawn();
        var weaponConfig = pawn.UnarmedWeaponConfig;

        // PoseConfigs is empty usually?
        weaponConfig.PoseConfigs.Clear();
        weaponConfig.PoseConfigs.Add(new RPoseConfig(pawn));

        // Build PoseConfig
        var someNightwingActor = Game.SpawnActor<RPawnPlayerNightwing>();
        someNightwingActor.AddWristDartMovesToWeaponConfig(weaponConfig);
    }

    //static void GiveWristDart()
    //{
    //    Game.LoadPackage("Playable_Nightwing_SF");

    //    var pawn = Game.GetPlayerPawn();
    //    var controller = Game.GetPlayerController();

    //    // Give gadget to player
    //    pawn.GiveGadget(RNightwingWristDart.StaticClass(), true, false);
    //    controller.GadgetsUpdated();

    //    // Update inventory and equip
    //    var inventoryManager = (RInventoryManager)pawn.InvManager;
    //    inventoryManager.SetCurrentGadgetByName("RNightwingWristDart");
    //    inventoryManager.DisplayedGadget = inventoryManager.CurrentGadget;
    //    controller.HudMovieNew.GadgetSelects[controller.HudMovieSide].AutoSelectCurrentGadget();

    //    // Set up animations
    //    SetupAnimations(pawn);
    //}

    //static void SetupAnimations(RPawnPlayer pawn)
    //{
    //    var weaponConfig = pawn.UnarmedWeaponConfig;

    //    // PoseConfigs is empty usually?
    //    weaponConfig.PoseConfigs.Clear();
    //    weaponConfig.PoseConfigs.Add(new RPoseConfig(pawn));

    //    // Build PoseConfig
    //    var someNightwingActor = Game.SpawnActor<RPawnPlayerNightwing>();
    //    someNightwingActor.AddWristDartMovesToWeaponConfig(weaponConfig);
    //}
}
