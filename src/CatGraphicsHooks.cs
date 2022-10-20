using RWCustom;
using UnityEngine;
using static UnityEngine.Mathf;
using static LavaCat.Extensions;

namespace LavaCat;

static class CatGraphicsHooks
{
    static ref FSprite BodyPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites()];
    static ref FSprite HipsPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites() + 1];
    static ref FSprite LegPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites() + 2];

    public static void Apply()
    {
        On.PlayerGraphics.PlayerObjectLooker.HowInterestingIsThisObject += PlayerObjectLooker_HowInterestingIsThisObject;
        On.Player.ShortCutColor += Player_ShortCutColor;

        On.PlayerGraphics.InitiateSprites += PlayerGraphics_InitiateSprites;
        On.PlayerGraphics.AddToContainer += PlayerGraphics_AddToContainer;
        On.PlayerGraphics.Update += PlayerGraphics_Update;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
        On.PlayerGraphics.ApplyPalette += PlayerGraphics_ApplyPalette;
    }

    private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics graf)
    {
        orig(graf);

        if (graf.player.BlindTimer() > 0) {
            graf.player.BlindTimer() -= 1;
            graf.objectLooker.LookAtNothing();
        }

        if (graf.player.IsLavaCat()) {
            graf.breath = 0f;
            graf.lastBreath = 0f;

            EmitFire(graf);
            UpdateLights(graf);
        }

        static void EmitFire(PlayerGraphics graf)
        {
            float chance = 0.50f * graf.player.Temperature() * graf.player.Temperature();

            // Emit fire at top of head
            if (RngChance(chance * 0.5f)) {
                graf.player.room.AddObject(new LavaFireSprite(graf.head.pos + Random.insideUnitCircle * 4));
            }
            // Emit fire on tail
            if (RngChance(chance)) {
                LavaFireSprite particle = new(graf.tail.RandomElement().pos);
                particle.vel.x *= 0.25f;
                particle.life *= 0.70f;
                graf.player.room.AddObject(particle);
            }
        }

        static void UpdateLights(PlayerGraphics graf)
        {
            CatLight[] lights = graf.Lights();

            if (graf.player.Temperature() <= 0) {
                if (graf.player.glowing) {

                    graf.player.glowing = false;
                    graf.lightSource?.Destroy();

                    for (int i = 0; i < lights.Length; i++) {
                        if (lights[i].source.TryGetTarget(out var light)) {
                            lights[i].source = new();
                            light.Destroy();
                        }
                    }
                }
                return;
            }

            graf.player.glowing = true;

            // Dampen glow light source
            if (graf.lightSource != null) {
                graf.lightSource.color = LavaColor.rgb with { a = graf.player.Temperature() };
                graf.lightSource.rad = 200 * graf.player.Temperature();
            }

            for (int i = 0; i < lights.Length; i++) {
                lights[i].source ??= new();
                lights[i].source.TryGetTarget(out var source);

                if (source != null && (source.slatedForDeletetion || source.room != graf.player.room)) {
                    source.setAlpha = 0;
                    source.Destroy();
                    source = null;
                }

                if (source == null) {
                    float hue = Lerp(0.01f, 0.07f, i / 2f);
                    Color color = new HSLColor(hue, 1f, 0.5f).rgb;
                    source = new LightSource(graf.player.firstChunk.pos, false, color, graf.player) {
                        setAlpha = 1,
                    };

                    lights[i].source = new(source);
                    graf.player.room.AddObject(source);
                }

                if (RngChance(0.2f)) {
                    lights[i].targetOffset = Random.insideUnitCircle * 25f;
                }
                if (RngChance(0.2f)) {
                    float minRad = 50f;
                    float maxRad = Lerp(350f, 150f, i / (lights.Length - 1f));
                    lights[i].targetRad = Lerp(minRad, maxRad, Sqrt(Random.value)) * graf.player.Temperature();
                }
                lights[i].offset = Vector2.Lerp(lights[i].offset, lights[i].targetOffset, 0.2f);
                source.setRad = Lerp(source.rad, lights[i].targetRad, 0.2f);
                source.setPos = graf.player.firstChunk.pos + lights[i].offset;
                source.alpha = graf.player.Temperature();
            }
        }
    }

    private static float PlayerObjectLooker_HowInterestingIsThisObject(On.PlayerGraphics.PlayerObjectLooker.orig_HowInterestingIsThisObject orig, object self, PhysicalObject obj)
    {
        if (self is PlayerGraphics.PlayerObjectLooker looker && looker.owner.player.BlindTimer() > 0) {
            return float.NegativeInfinity;
        }
        return orig(self, obj);
    }

    private static Color Player_ShortCutColor(On.Player.orig_ShortCutColor orig, Player player)
    {
        return player.IsLavaCat() ? player.SkinColor() : orig(player);
    }

    private static void PlayerGraphics_InitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        orig(self, sLeaser, rCam);

        self.PlateSprites() = sLeaser.sprites.Length;

        System.Array.Resize(ref sLeaser.sprites, sLeaser.sprites.Length + 3);

        self.BodyPlate(sLeaser) = new FSprite("PlatesBodyA");
        self.HipsPlate(sLeaser) = new FSprite("PlatesHipsA");
        self.LegPlate(sLeaser) = new FSprite("PlatesLegsA0");

        self.AddToContainer(sLeaser, rCam, null);
    }

    private static void PlayerGraphics_AddToContainer(On.PlayerGraphics.orig_AddToContainer orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
    {
        orig(self, sLeaser, rCam, newContatiner);

        if (sLeaser.sprites.Length > 12) {
            sLeaser.sprites[6].container.AddChild(self.BodyPlate(sLeaser));
            sLeaser.sprites[4].container.AddChild(self.LegPlate(sLeaser));
            sLeaser.sprites[1].container.AddChild(self.HipsPlate(sLeaser));

            self.BodyPlate(sLeaser).MoveInFrontOfOtherNode(sLeaser.sprites[6]);
            self.LegPlate(sLeaser).MoveInFrontOfOtherNode(sLeaser.sprites[4]);
            self.HipsPlate(sLeaser).MoveInFrontOfOtherNode(sLeaser.sprites[1]);
        }
    }

    private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        if (self.player.IsLavaCat() && self.player.playerState.playerNumber <= 0) {
            AlignSprite(self.BodyPlate(sLeaser), sLeaser.sprites[0]);
            AlignSprite(self.HipsPlate(sLeaser), sLeaser.sprites[1]);
            AlignSprite(self.LegPlate(sLeaser), sLeaser.sprites[4]);

            RotateRibcage(self, sLeaser, timeStacker);

            self.BodyPlate(sLeaser).anchorY = 0.84f;
        }

        if (self.player.IsLavaCat()) {
            self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
        }
    }

    private static void RotateRibcage(PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, float timeStacker)
    {
        int dir = 0;

        if (self.player.Consious && self.player.bodyMode == Player.BodyModeIndex.Stand) {
            dir = self.player.input[0].x;
        }
        else if (self.player.Consious && self.player.bodyMode == Player.BodyModeIndex.Crawl) {
            Vector2 drawPosHead = Vector2.Lerp(self.drawPositions[0, 1], self.drawPositions[0, 0], timeStacker);
            Vector2 drawPosBody = Vector2.Lerp(self.drawPositions[1, 1], self.drawPositions[1, 0], timeStacker);

            if (drawPosHead.x < drawPosBody.x) {
                dir = -1;
            } else {
                dir = 1;
            }
        }

        self.BodyPlate(sLeaser).scaleX = 1f;
        self.BodyPlate(sLeaser).element = Plugin.Atlas._elementsByName[BodyDir(dir)];

        //if (self.player.Consious && self.player.bodyMode != Player.BodyModeIndex.ZeroG && self.player.room != null && self.player.room.gravity > 0f) {
        //    Vector2 head = Vector2.Lerp(self.drawPositions[0, 0], self.head.pos, 0.2f);
        //    Vector2 body = self.drawPositions[1, 0];
        //    Vector2 pointing = Custom.DirVec(body, head);

        //    bool isBackVisible = pointing.y < -0.1f && self.player.bodyMode != Player.BodyModeIndex.ClimbingOnBeam;

        //    if (isBackVisible) {
        //        float fade = pointing.y / -0.5f;
        //        if (fade > 1)
        //            fade = 1;

        //        self.BodyPlate(sLeaser).scaleX = 1 - fade;
        //    }
        //}
    }


    private static void PlayerGraphics_ApplyPalette(On.PlayerGraphics.orig_ApplyPalette orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
    {
        orig(self, sLeaser, rCam, palette);

        // Make tail creamy white :)
        if (self.player.IsLavaCat() && sLeaser.sprites[2] is TriangleMesh mesh) {
            mesh.verticeColors = new Color[mesh.vertices.Length];
            mesh.customColor = true;

            for (int i = 0; i < mesh.verticeColors.Length; i++) {
                mesh.verticeColors[i] = Color.Lerp(self.player.SkinColor(), Color.white * (self.player.Temperature() + 0.25f), i / (float)mesh.verticeColors.Length);
            }

            for (int i = 0; i < 9; i++) {
                if (i != 2) {
                    sLeaser.sprites[i].color = self.player.SkinColor();
                }
            }

            // Goes from a dark rocky color to a slightly redder dark orange
            HSLColor hsl = LavaColor;
            hsl.hue *= 0.5f;
            hsl.saturation *= 0.8f;
            hsl.lightness *= 0.5f;
            Color end = hsl.rgb;
            Color start = new(0.15f, 0.15f, 0.15f);

            Color plateColor = Color.Lerp(start, end, self.player.Temperature().Pow(2));
            self.BodyPlate(sLeaser).color = plateColor;
            self.HipsPlate(sLeaser).color = plateColor;
        }
    }

    private static string BodyDir(int name)
    {
        return name switch {
            0 => "PlatesBodyA",
            1 => "PlatesBodyARight",
            -1 => "PlatesBodyALeft",
            _ => throw new()
        };
    }

    private static void AlignSprite(FSprite plate, FSprite part)
    {
        plate.anchorX = part.anchorX;
        plate.anchorY = part.anchorY;
        plate.x = part.x;
        plate.y = part.y;
        plate.rotation = part.rotation;
        plate.scaleX = part.scaleX;
        plate.scaleY = part.scaleY;
        plate.alpha = part.alpha;

        if (Plugin.Atlas._elementsByName.TryGetValue("Plates" + part.element.name, out FAtlasElement replacement)) {
            plate.element = replacement;
        }
        else {
            plate.alpha = 0;
        }
    }
}
