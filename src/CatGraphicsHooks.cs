using RWCustom;
using System;
using UnityEngine;

namespace LavaCat;

static class CatGraphicsHooks
{
    static ref FSprite BodyPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites()];
    static ref FSprite HipsPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites() + 1];
    static ref FSprite LegPlate(this PlayerGraphics g, RoomCamera.SpriteLeaser leaser) => ref leaser.sprites[g.PlateSprites() + 2];

    public static void Apply()
    {
        On.PlayerGraphics.InitiateSprites += PlayerGraphics_InitiateSprites;
        On.PlayerGraphics.AddToContainer += PlayerGraphics_AddToContainer;
        On.PlayerGraphics.Update += PlayerGraphics_Update;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
    }

    private static void PlayerGraphics_InitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        orig(self, sLeaser, rCam);

        self.PlateSprites() = sLeaser.sprites.Length;

        Array.Resize(ref sLeaser.sprites, sLeaser.sprites.Length + 3);

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

            self.BodyPlate(sLeaser).MoveInFrontOfOtherNode(sLeaser.sprites[6]);
            self.LegPlate(sLeaser).MoveInFrontOfOtherNode(sLeaser.sprites[4]);
        }
    }

    private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
    {
        orig(self);

    }

    private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        if (self.player.IsLavaCat() && self.player.playerState.playerNumber <= 0) {
            AlignSprite(self.BodyPlate(sLeaser), sLeaser.sprites[0]);
            AlignSprite(self.HipsPlate(sLeaser), sLeaser.sprites[1]);
            AlignSprite(self.LegPlate(sLeaser), sLeaser.sprites[4]);

            self.BodyPlate(sLeaser).anchorY = 0.84f;
            self.HipsPlate(sLeaser).alpha = 0;

            RotateRibcage(self, sLeaser, timeStacker);
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

        //        Console.WriteLine(1 - fade);
        //    }
        //}
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
