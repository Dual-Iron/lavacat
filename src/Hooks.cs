using SlugBase;
using UnityEngine;
using WeakTables;
using static LavaCat.Extensions;

namespace LavaCat;

sealed class PlayerData
{
    public float temperature = 1; // 0 min, 1 max
    public float temperatureLast = 1; // 0 min, 1 max
}

sealed class PlayerGraphicsData
{
    public WeakRef<CatLight[]> lights = new();
}

struct CatLight
{
    public LightSource source;
    public Vector2 offset;
    public Vector2 targetOffset;
    public float targetRad;
}

static class Hooks
{
    public static readonly WeakTable<Player, PlayerData> plrData = new(_ => new PlayerData());
    public static readonly WeakTable<PlayerGraphics, PlayerGraphicsData> graphicsData = new(_ => new PlayerGraphicsData());

    public static void Apply()
    {
        // Fix underwater movement
        On.Player.MovementUpdate += Player_MovementUpdate;
        On.Room.FloatWaterLevel += Room_FloatWaterLevel;

        On.Player.Update += Player_Update;

        On.PlayerGraphics.ApplyPalette += PlayerGraphics_ApplyPalette;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
        On.PlayerGraphics.Update += PlayerGraphics_Update;
    }

    private static bool movementUpdate = false;
    private static void Player_MovementUpdate(On.Player.orig_MovementUpdate orig, Player self, bool eu)
    {
        movementUpdate = true;
        orig(self, eu);
        movementUpdate = false;
    }
    private static float Room_FloatWaterLevel(On.Room.orig_FloatWaterLevel orig, Room self, float horizontalPos)
    {
        // Ignore water level when performing movement update.
        if (movementUpdate) return 0;
        return orig(self, horizontalPos);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);

        if (self.IsLavaCat()) {
            self.TempLast() = self.Temp();

            self.waterFriction = 0.8f;
            self.buoyancy = 0.8f * self.Temp();

            WaterCollision(self, self.bodyChunks[0]);
            WaterCollision(self, self.bodyChunks[1]);

            // This cat doesn't breathe.
            self.airInLungs = 1f;
            self.aerobicLevel = 0f;
        }
    }

    private static void WaterCollision(Player player, BodyChunk chunk)
    {
        if (chunk.submersion > 0 && player.Temp() > 0) {
            float ticksToFreeze = chunk.index == 0 ? 40 : 180;

            player.Temp() -= chunk.submersion / ticksToFreeze;
            player.Temp() = Mathf.Clamp01(player.Temp());

            player.room.PlaySound(SoundID.Gate_Electric_Steam_Puff, chunk);
        }
    }

    // -- Graphics --

    private static void PlayerGraphics_ApplyPalette(On.PlayerGraphics.orig_ApplyPalette orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
    {
        orig(self, sLeaser, rCam, palette);

        // Make tail creamy white :)
        if (self.player.IsLavaCat() && sLeaser.sprites[2] is TriangleMesh mesh) {
            mesh.verticeColors = new Color[mesh.vertices.Length];
            mesh.customColor = true;

            for (int i = 0; i < mesh.verticeColors.Length; i++) {
                mesh.verticeColors[i] = Color.Lerp(self.player.SkinColor(), Color.white * (self.player.Temp() + 0.25f), i / (float)mesh.verticeColors.Length);
            }

            for (int i = 0; i < sLeaser.sprites.Length; i++) {
                if (i != 2 && i != 9 && i != 10 && i != 11) {
                    sLeaser.sprites[i].color = self.player.SkinColor();
                }
            }
        }
    }

    private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        if (self.player.IsLavaCat()) {
            if (self.player.Temp() != self.player.TempLast()) {
                self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
            }
        }
    }

    private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
    {
        orig(self);

        if (self.player.IsLavaCat()) {
            self.breath = 0f;
            self.lastBreath = 0f;

            EmitFire(self);
            UpdateLights(self);
        }

        static void EmitFire(PlayerGraphics self)
        {
            float chance = 0.50f * self.player.Temp();

            // Emit fire at top of head
            if (RngChance(chance * 0.5f)) {
                self.player.room.AddObject(new LavaFireSprite(self.head.pos + Random.insideUnitCircle * 4));
            }
            // Emit fire on tail
            if (RngChance(chance)) {
                LavaFireSprite particle = new(self.tail.RngElement().pos);
                particle.vel.x *= 0.25f;
                particle.life *= 0.70f;
                self.player.room.AddObject(particle);
            }
        }

        static void UpdateLights(PlayerGraphics self)
        {
            var data = graphicsData[self];

            self.player.glowing = true;

            // Dampen glow light source
            if (self.lightSource != null) {
                self.lightSource.color = PlayerManager.GetSlugcatColor(self.player);
                self.lightSource.rad = 150 * self.player.Temp();
            }

            // If the lights are loaded, update them
            if (data.lights.TryGetTarget(out var lights)) {
                for (int i = 0; i < lights.Length; i++) {
                    if (RngChance(0.2f)) {
                        lights[i].targetOffset = Random.insideUnitCircle * 25f;
                    }
                    if (RngChance(0.2f)) {
                        float minRad = 50f;
                        float maxRad = Mathf.Lerp(250f, 100f, i / (lights.Length - 1f));
                        lights[i].targetRad = Mathf.Lerp(minRad, maxRad, Mathf.Sqrt(Random.value)) * self.player.Temp();
                    }
                    lights[i].offset = Vector2.Lerp(lights[i].offset, lights[i].targetOffset, 0.2f);
                    lights[i].source.setRad = Mathf.Lerp(lights[i].source.rad, lights[i].targetRad, 0.2f);
                    lights[i].source.setPos = self.player.firstChunk.pos + lights[i].offset;
                }

                // Delete lights if they're done updating
                if (lights[0].source.slatedForDeletetion) {
                    data.lights = new();
                }
            }
            // If not, create new ones
            else {
                Vector2 pos = self.player.firstChunk.pos;
                CatLight[] newLights = new CatLight[3];

                for (int i = 0; i < newLights.Length; i++) {
                    float hue = Mathf.Lerp(0.01f, 0.07f, i / (newLights.Length - 1));
                    Color color = new HSLColor(hue, 1f, 0.5f).rgb;

                    newLights[i].source = new LightSource(pos, false, color, self.player) {
                        setAlpha = 1,
                    };

                    self.player.room.AddObject(newLights[i].source);
                }

                data.lights = new(newLights);
            }
        }
    }
}
