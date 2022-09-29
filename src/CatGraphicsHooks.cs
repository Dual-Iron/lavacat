using UnityEngine;

namespace LavaCat;

static class CatGraphicsHooks
{
    public static void Apply()
    {
        On.PlayerGraphics.Update += PlayerGraphics_Update;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
    }

    private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
    {
        orig(self);

    }

    private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        if (self.player.IsLavaCat() && self.player.playerState.playerNumber <= 0) {
            foreach (var sprite in sLeaser.sprites) {
                if (Plugin.Atlas._elementsByName.TryGetValue("Magma" + sprite.element.name, out FAtlasElement replacement)) {
                    sprite.element = replacement;
                }
            }
        }
    }
}
