namespace LavaCat;

static class MenuHooks
{
    public static void Apply()
    {
        On.Menu.ControlMap.ctor += ControlMap_ctor;
        On.Menu.InteractiveMenuScene.Update += InteractiveMenuScene_Update;
    }

    private static void ControlMap_ctor(On.Menu.ControlMap.orig_ctor orig, Menu.ControlMap self, Menu.Menu menu, Menu.MenuObject owner, UnityEngine.Vector2 pos, Options.ControlSetup.Preset preset, bool showPickupInstructions)
    {
        orig(self, menu, owner, pos, preset, showPickupInstructions);

        if (self.pickupButtonInstructions != null && menu.manager.currentMainLoop is RainWorldGame game && Plugin.Character.IsMe(game)) {
            const string replace = "Hold to eat / swallow objects";
            const string with = "Hold to burn creatures and objects";

            self.pickupButtonInstructions.text = self.pickupButtonInstructions.text.Replace(replace, with);
        }
    }

    private static void InteractiveMenuScene_Update(On.Menu.InteractiveMenuScene.orig_Update orig, Menu.InteractiveMenuScene self)
    {
        int slugcat = self.menu.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat;

        // if is newdeath and this is magmacat
        var scene = self.sceneID;
        var magmaDeath = self.sceneID == Menu.MenuScene.SceneID.NewDeath && slugcat == Plugin.Character.SlugcatIndex;
        if (magmaDeath) {
            self.sceneID = (Menu.MenuScene.SceneID)(-10);
        }

        orig(self);

        if (magmaDeath) {
            self.sceneID = scene;
        }
    }
}
