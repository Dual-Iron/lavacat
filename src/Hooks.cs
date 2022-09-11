using RWCustom;
using SlugBase;
using Smoke;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeakTables;
using static LavaCat.Extensions;
using static UnityEngine.Mathf;

namespace LavaCat;

sealed class PlayerData
{
    public float temperature = 1; // 0 min, 1 max
    public float temperatureLast = 1; // 0 min, 1 max
    public int steamSound = 0;

    public WeakRef<LavaSteam> steam = new();
}

sealed class PlayerGraphicsData
{
    public CatLight[] lights = new CatLight[3];
}

struct CatLight
{
    public WeakRef<LightSource> source;
    public Vector2 offset;
    public Vector2 targetOffset;
    public float targetRad;
}

sealed class LavaSteam : SmokeSystem
{
    public LavaSteam(Room room) : base(SmokeType.Steam, room, 2, 0f)
    {
    }

    public override SmokeSystemParticle CreateParticle()
    {
        return new LavaSteamParticle();
    }

    public void EmitSmoke(Vector2 pos, Vector2 vel, float intensity)
    {
        if (AddParticle(pos, vel, Lerp(60f, 180f, Random.value * intensity)) is LavaSteamParticle particle) {
            particle.intensity = intensity;
            particle.rad = Lerp(108f, 286f, Random.value) * Lerp(0.5f, 1f, intensity);
        }
    }

    sealed class LavaSteamParticle : SpriteSmoke
    {
        private float upForce;
        public float moveDir;
        public float intensity;

        public override float ToMidSpeed => 0.4f;

        public override void Reset(SmokeSystem newOwner, Vector2 pos, Vector2 vel, float lifeTime)
        {
            base.Reset(newOwner, pos, vel, lifeTime);
            upForce = Random.value * 100f / lifeTime;
            moveDir = Random.value * 360f;
        }

        public override void Update(bool eu)
        {
            base.Update(eu);

            if (!resting) {
                moveDir += Lerp(-1f, 1f, Random.value) * 50f;
                vel *= 0.8f;
                vel += Custom.DegToVec(moveDir) * 1.8f * intensity * life;
                vel.y += 2.8f * intensity * upForce;
                if (room.PointSubmerged(pos)) {
                    vel.y += 1.4f * intensity * upForce;
                }
            }
        }

        public override float Rad(int type, float useLife, float useStretched, float timeStacker)
        {
            float val = Pow(Lerp(Sin(useLife * 3.1415927f), 1f - useLife, 0.7f), 0.8f);
            if (type == 0) {
                return Lerp(4f, rad, val + useStretched);
            }
            if (type != 1) {
                return Lerp(4f, rad, val);
            }
            return 1.5f * Lerp(2f, rad, val);
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            base.InitiateSprites(sLeaser, rCam);

            for (int i = 0; i < 2; i++) {
                sLeaser.sprites[i].shader = room.game.rainWorld.Shaders["Steam"];
            }
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            base.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            if (!resting) {
                for (int i = 0; i < 2; i++) {
                    sLeaser.sprites[i].alpha = life;
                }
            }
        }

        public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
        {
            base.ApplyPalette(sLeaser, rCam, palette);

            for (int i = 0; i < 2; i++) {
                sLeaser.sprites[i].color = Color.Lerp(palette.fogColor, new Color(1f, 1f, 1f), Lerp(0.03f, 0.35f, palette.texture.GetPixel(30, 7).r));
            }
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            newContainer = rCam.ReturnFContainer("Water");

            base.AddToContainer(sLeaser, rCam, newContainer);
        }
    }
}

static class Hooks
{
    public static readonly WeakTable<Player, PlayerData> plrData = new(_ => new PlayerData());
    public static readonly WeakTable<PlayerGraphics, PlayerGraphicsData> graphicsData = new(_ => new PlayerGraphicsData());

    private static void Cool(Player player, float temperature, float smokeIntensity, Vector2 smokePos, Vector2 smokeVel)
    {
        if (player.Temp() > 0 && temperature > 0) {
            player.Temp() = Clamp01(player.Temp() - temperature);

            // Create steam object if there's none currently
            if (!plrData[player].steam.TryGetTarget(out var steam)) {
                plrData[player].steam = new(steam = new LavaSteam(player.room));

                player.room.AddObject(steam);
            }

            steam.EmitSmoke(smokePos, smokeVel, smokeIntensity);

            plrData[player].steamSound = 7;
        }
    }

    public static void Apply()
    {
        // Fix underwater movement
        On.Creature.Grab += Creature_Grab;
        On.Player.MovementUpdate += Player_MovementUpdate;
        On.Room.FloatWaterLevel += Room_FloatWaterLevel;

        On.Player.Update += Player_Update;

        On.Player.ShortCutColor += Player_ShortCutColor;
        On.PlayerGraphics.ApplyPalette += PlayerGraphics_ApplyPalette;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
        On.PlayerGraphics.Update += PlayerGraphics_Update;
    }

    private static bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
    {
        // Prevent leeches from softlocking the game
        if (self is Leech leech && obj is Player player && player.IsLavaCat()) {
            leech.HeardSnailClick(player.firstChunk.pos);
            leech.firstChunk.vel *= -1.5f;
            return false;
        }
        return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
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
        return movementUpdate ? 0 : orig(self, horizontalPos);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);

        if (self.IsLavaCat()) {
            float temp = self.Temp();

            self.TempLast() = temp;

            self.waterFriction = Lerp(0.96f, 0.8f, self.Temp());
            self.buoyancy = Lerp(0.3f, 1.1f, self.Temp());

            // This cat doesn't breathe.
            self.airInLungs = 1f;
            self.aerobicLevel = 0f;

            UpdateWaterCollision(self);
            UpdateStats(self, temp);

            if (plrData[self].steamSound > 0) {
                plrData[self].steamSound -= 1;

                self.room.PlaySound(SoundID.Gate_Water_Steam_Puff, self.firstChunk.pos, 0.35f, 1.2f);
            }
        }
    }

    private static void UpdateWaterCollision(Player self)
    {
        // Cool down if any part of the body is submerged
        Cool(self,
             temperature: self.bodyChunks[0].submersion / 100,
             smokeIntensity: self.bodyChunks[0].submersion * 0.5f,
             smokePos: self.bodyChunks[0].pos + Random.insideUnitCircle * 5,
             smokeVel: new Vector2(0, 5) + Random.insideUnitCircle * 5
             );

        Cool(self,
             temperature: self.bodyChunks[1].submersion / 400,
             smokeIntensity: self.bodyChunks[1].submersion * 0.5f,
             smokePos: self.bodyChunks[1].pos + Random.insideUnitCircle * 5,
             smokeVel: new Vector2(0, 5) + Random.insideUnitCircle * 5
             );

        // Iterate room's objects safely
        var iterate = self.room.updateList;
        var newObjects = new List<UpdatableAndDeletable>();

        self.room.updateList = newObjects;

        foreach (var updateable in iterate) {
            if (updateable is WaterDrip drip) {
                Droplet(self, drip);
            }
            else if (updateable is WaterFall waterfall) {
                Waterfall(self, waterfall);
            }
        }

        iterate.AddRange(newObjects);

        self.room.updateList = iterate;

        // Collision logic for water drops and waterfalls
        static void Droplet(Player self, WaterDrip drip)
        {
            if ((self.bodyChunks[0].pos - drip.pos).MagnitudeLessThan(self.bodyChunks[0].rad + drip.width) ||
                (self.bodyChunks[1].pos - drip.pos).MagnitudeLessThan(self.bodyChunks[1].rad + drip.width)) {

                Cool(self, 1 / 100f, 0.25f, drip.pos, -drip.vel * 0.5f);

                drip.life = 0;
            }
        }

        static void Waterfall(Player self, WaterFall waterfall)
        {
            FloatRect bounds = new(waterfall.FloatLeft, waterfall.strikeLevel, waterfall.FloatRight, waterfall.startLevel);

            foreach (var chunk in self.bodyChunks) {
                FloatRect bounds2 = bounds;

                bounds2.Grow(chunk.rad);

                if (bounds2.Vector2Inside(chunk.pos)) {
                    Cool(self, 1 / 600f, 0.3f, chunk.pos, Random.insideUnitCircle * 2);
                }
            }
        }
    }

    private static void UpdateStats(Player player, float temperature)
    {
        var stats = player.slugcatStats;

        stats.loudnessFac = 1.5f;
        stats.lungsFac = 0.01f;

        stats.bodyWeightFac = Lerp(1.35f, 1.4f, temperature);
        stats.generalVisibilityBonus = Lerp(0f, 0.2f, temperature);
        stats.corridorClimbSpeedFac = Lerp(0.9f, 1.2f, temperature);
        stats.poleClimbSpeedFac = Lerp(0.9f, 1.2f, temperature);
        stats.runspeedFac = Lerp(0.95f, 1.25f, temperature);
    }

    // -- Graphics --

    private static Color Player_ShortCutColor(On.Player.orig_ShortCutColor orig, Player self)
    {
        return self.IsLavaCat() ? self.SkinColor() : orig(self);
    }

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
            float chance = 0.50f * self.player.Temp() * self.player.Temp();

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
            CatLight[] lights = graphicsData[self].lights;

            if (self.player.Temp() <= 0) {
                if (self.player.glowing) {

                    self.player.glowing = false;
                    self.lightSource?.Destroy();

                    for (int i = 0; i < lights.Length; i++) {
                        if (lights[i].source.TryGetTarget(out var light)) {
                            lights[i].source = new();
                            light.Destroy();
                        }
                    }
                }
                return;
            }

            self.player.glowing = true;

            // Dampen glow light source
            if (self.lightSource != null) {
                self.lightSource.color = PlayerManager.GetSlugcatColor(self.player) with { a = self.player.Temp() };
                self.lightSource.rad = 200 * self.player.Temp();
            }

            for (int i = 0; i < lights.Length; i++) {
                lights[i].source ??= new();
                lights[i].source.TryGetTarget(out var source);

                if (source != null && (source.slatedForDeletetion || source.room != self.player.room)) {
                    source.setAlpha = 0;
                    source.Destroy();
                    source = null;
                }

                if (source == null) {
                    float hue = Lerp(0.01f, 0.07f, i / 2f);
                    Color color = new HSLColor(hue, 1f, 0.5f).rgb;
                    source = new LightSource(self.player.firstChunk.pos, false, color, self.player) {
                        setAlpha = 1,
                    };

                    lights[i].source = new(source);
                    self.player.room.AddObject(source);
                }

                if (RngChance(0.2f)) {
                    lights[i].targetOffset = Random.insideUnitCircle * 25f;
                }
                if (RngChance(0.2f)) {
                    float minRad = 50f;
                    float maxRad = Lerp(350f, 150f, i / (lights.Length - 1f));
                    lights[i].targetRad = Lerp(minRad, maxRad, Sqrt(Random.value)) * self.player.Temp();
                }
                lights[i].offset = Vector2.Lerp(lights[i].offset, lights[i].targetOffset, 0.2f);
                source.setRad = Lerp(source.rad, lights[i].targetRad, 0.2f);
                source.setPos = self.player.firstChunk.pos + lights[i].offset;
                source.alpha = self.player.Temp();
            }
        }
    }
}
