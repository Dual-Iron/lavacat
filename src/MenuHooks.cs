namespace LavaCat;

static class MenuHooks
{
    public static void Apply()
    {
        On.Menu.InteractiveMenuScene.Update += InteractiveMenuScene_Update;
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
