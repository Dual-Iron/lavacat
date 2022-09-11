using Mono.Cecil.Cil;
using MonoMod.Cil;
using Smoke;
using System.Collections.Generic;
using UnityEngine;
using static LavaCat.Extensions;

namespace LavaCat;

static class HeatHooks
{
    public static void Apply()
    {
        // Let player heat objects up
        IL.Player.GrabUpdate += Player_GrabUpdate;

        // Special behavior for heated objects
        On.PhysicalObject.Update += PhysicalObject_Update;
        On.RoomCamera.SpriteLeaser.Update += SpriteLeaser_Update;
    }

    private static void UpdateWaterCollision(PhysicalObject o)
    {
        if (o.Temperature() <= 0) {
            return;
        }

        if (o is Creature c && c.rainDeath > 0) {
            o.Cool(c.rainDeath / 100f, c.rainDeath * 0.5f, c.mainBodyChunk.pos, new Vector2(0, 5) + Random.insideUnitCircle * 5);
        }

        // Cool down if any part of the body is submerged
        foreach (var chunk in o.bodyChunks) {
            bool mainChunk = o.bodyChunks.Length == 0 || o is Creature c2 && c2.mainBodyChunk == chunk;

            o.Cool(
                temperatureLoss: chunk.submersion / (mainChunk ? 100 : 400),
                smokeIntensity: chunk.submersion * 0.5f,
                smokePos: chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f,
                smokeVel: new Vector2(0, 5) + Random.insideUnitCircle * 5
            );
        }

        // Iterate room's objects safely
        var iterate = o.room.updateList;
        var newObjects = new List<UpdatableAndDeletable>();

        o.room.updateList = newObjects;

        foreach (var updateable in iterate) {
            if (updateable is WaterDrip drip) {
                foreach (var chunk in o.bodyChunks) {
                    if ((chunk.pos - drip.pos).MagnitudeLessThan(chunk.rad + drip.width)) {
                        o.Cool(1 / 100f, 0.25f, drip.pos, -drip.vel * 0.5f);

                        drip.life = 0;
                    }
                }
            }
            else if (updateable is WaterFall waterfall && waterfall.flow > 0) {
                FloatRect bounds = new(waterfall.FloatLeft, waterfall.strikeLevel, waterfall.FloatRight, waterfall.startLevel);

                foreach (var chunk in o.bodyChunks) {
                    FloatRect bounds2 = bounds;

                    bounds2.Grow(chunk.rad);

                    if (bounds2.Vector2Inside(chunk.pos)) {
                        o.Cool(1 / 600f * waterfall.flow, 0.3f, chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f, Random.insideUnitCircle * 2);
                    }
                }
            }
        }

        // Add any objects that were spawned while iterating, like smoke particles  
        iterate.AddRange(newObjects);

        o.room.updateList = iterate;
    }

    private static IHeatable GetHeatedBehavior(PhysicalObject o)
    {
        return o switch {
            // TODO add more special interactions, like with spears (damage boost), explosives (explode), and creatures (stun)
            IPlayerEdible or WaterNut or FlyLure or FirecrackerPlant => new Simple { Conductivity = 0.2f, IsFood = true },
            Player => new Simple { Conductivity = 0.1f },
            Spear => new HeatSpear(),
            Rock => new HeatRock(),
            _ => new Simple() { Conductivity = 0.01f },
        };
    }

    private static void Equalize(PhysicalObject self, PhysicalObject other)
    {
        // Lighter objects should lose heat faster than heavier objects.
        float massRatio = other.TotalMass / (other.TotalMass + self.TotalMass);
        float conductivity = GetHeatedBehavior(self).Conductivity;
        float diff = other.Temperature() - self.Temperature();

        // LavaCat can't be cooled down by holding items--only heated up
        if (other is Player p && p.IsLavaCat() && other.Temperature() > self.Temperature()) {
            massRatio = 1;
        }
        else if (self is Player p2 && p2.IsLavaCat() && self.Temperature() > other.Temperature()) {
            massRatio = 0;
        }

        self.Temperature() += diff * conductivity * 0.05f * massRatio;
        other.Temperature() -= diff * conductivity * 0.05f * (1 - massRatio);
    }


    private static void Player_GrabUpdate(ILContext il)
    {
        ILCursor cursor = new(il);

        cursor.GotoNext(MoveType.Before, i => i.MatchLdcI4(0) && i.Next.MatchStloc(0));
        cursor.Index += 2;

        // Overwrite num0, which decides if the player should eat/swallow or not
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloca, il.Body.Variables[0]);
        cursor.EmitDelegate(CanEatFood);

        static void CanEatFood(Player player, ref bool result)
        {
            if (player.IsLavaCat()) {
                bool heat = result;

                result = false;

                if (heat && player.input[0].pckp && player.Submersion == 0) {
                    foreach (var grasp in player.grasps) {
                        if (grasp?.grabbed is PhysicalObject o) {
                            HeatUpdate(player, o, grasp);
                            return;
                        }
                    }
                }

                player.HeatProgress() = 0;

                if (player.Smoke().TryGetTarget(out _))
                    player.Smoke() = new();
            }

            static void HeatUpdate(Player player, PhysicalObject o, Creature.Grasp grasp)
            {
                ref float progress = ref player.HeatProgress();

                bool isFood = GetHeatedBehavior(o).IsFood;

                if (!isFood && o.Temperature() >= player.Temperature()) {
                    progress = 0;
                    return;
                }

                if (progress > 1f && isFood) {
                    progress = 0;

                    o.Destroy();
                    player.TemperatureChange() += o.TotalMass;

                    for (int i = 0; i < 10 + o.firstChunk.rad; i++) {
                        LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.8f, foreground: false);
                        particle.vel.x *= 1.5f;
                        particle.vel.y *= 2f;
                        particle.lifeTime += 80;
                        player.room.AddObject(particle);
                    }
                }

                if (progress > 1 / 30f) {
                    player.Blink(5);
                    player.Graf().objectLooker.LookAtNothing();

                    int particleCount = (int)Rng(0, progress * 10);
                    for (int i = 0; i < particleCount; i++) {
                        LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.5f, foreground: RngChance(0.50f));
                        particle.vel.x *= 0.5f;
                        particle.vel.y *= 1.5f;
                        player.room.AddObject(particle);
                    }

                    if (!player.Smoke().TryGetTarget(out var smoke) || smoke.room != player.room) {
                        player.Smoke() = new(smoke = new BombSmoke(player.room, player.Hand(grasp).pos, null, Plugin.LavaColor.rgb) { autoSpawn = false });
                        player.room.AddObject(smoke);
                    }
                    smoke.pos = player.Hand(grasp).pos;
                    smoke.EmitWithMyLifeTime(smoke.pos, new Vector2(0, 0.5f));

                    // Heat up non-food items rapidly by holding PCKP
                    if (!isFood) {
                        for (int i = 0; i < progress * 5; i++) {
                            Equalize(player, o);
                        }
                    }
                }

                progress += 1 / 80f;
            }
        }
    }

    private static void PhysicalObject_Update(On.PhysicalObject.orig_Update orig, PhysicalObject self, bool eu)
    {
        orig(self, eu);

        bool touchingLavaCat = self is Player p && p.IsLavaCat();

        foreach (var stick in self.abstractPhysicalObject.stuckObjects) {
            AbstractPhysicalObject notMe = stick.A == self.abstractPhysicalObject ? stick.B : stick.A;

            if (notMe.realizedObject is PhysicalObject other) {
                touchingLavaCat |= other is Player p2 && p2.IsLavaCat();

                Equalize(self, other);
            }
        }

        // Lose heat to diffusion
        if (!touchingLavaCat) {
            float heatConservation = self.TotalMass / (self.TotalMass + 0.15f);

            self.Temperature() *= 0.992f + 0.008f * heatConservation;
        }

        float warming = self.TemperatureChange() * 0.15f;
        self.Temperature() += warming;
        self.TemperatureChange() -= warming;

        if (self.SteamSound() > 0) {
            self.SteamSound() -= 1;
            self.room.PlaySound(SoundID.Gate_Water_Steam_Puff, self.firstChunk.pos, 0.4f, 1.15f);
        }

        UpdateWaterCollision(self);

        GetHeatedBehavior(self).Update(self);
    }

    private static void SpriteLeaser_Update(On.RoomCamera.SpriteLeaser.orig_Update orig, RoomCamera.SpriteLeaser sLeaser, float timeStacker, RoomCamera rCam, Vector2 camPos)
    {
        orig(sLeaser, timeStacker, rCam, camPos);

        if (sLeaser.drawableObject is PhysicalObject o) {
            GetHeatedBehavior(o).DrawSprites(o, sLeaser, rCam, camPos);
        }
    }



    // - Water physics -

}

interface IHeatable
{
    bool IsFood { get; }
    float Conductivity { get; }

    void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos);
    void Update(PhysicalObject o);
}

struct Simple : IHeatable
{
    public bool IsFood { get; set; }
    public float Conductivity { get; set; }

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos) { }
    public void Update(PhysicalObject o) { }
}

struct HeatSpear : IHeatable
{
    public bool IsFood => false;
    public float Conductivity => 0.10f;

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos)
    {
        Spear spear = (Spear)o;
        HSLColor spearHsl = spear.color.HSL();

        float temp = o.Temperature();
        float hue = Mathf.Lerp(0f, Plugin.LavaColor.hue, temp);
        float sat = Mathf.Lerp(0.5f, 1f, temp);
        float light = Mathf.Lerp(spearHsl.lightness, 1f, temp * temp);

        sLeaser.sprites[0].color = new HSLColor(hue, sat, light).rgb;
    }
    public void Update(PhysicalObject o)
    {
        Spear spear = (Spear)o;

        if (o.room != null && Extensions.RngChance(0.50f * o.Temperature() * o.Temperature())) {
            const float halfLength = 22;

            LavaFireSprite sprite = new(o.firstChunk.pos + Random.insideUnitCircle * 2 + spear.rotation * Extensions.Rng(-halfLength, halfLength));
            sprite.life *= 0.7f;
            o.room.AddObject(sprite);
        }
    }
}

struct HeatRock : IHeatable
{
    public bool IsFood => false;
    public float Conductivity => 0.05f;

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos)
    {
        Rock rock = (Rock)o;
        HSLColor rockHsl = rock.color.HSL();

        float temp = o.Temperature();
        float hue = Mathf.Lerp(0f, Plugin.LavaColor.hue, temp);
        float sat = Mathf.Lerp(0.5f, 1f, temp);
        float light = Mathf.Lerp(rockHsl.lightness, 1f, temp * temp);

        sLeaser.sprites[0].color = new HSLColor(hue, sat, light).rgb;
        sLeaser.sprites[1].color = new HSLColor(hue, sat / 2, light / 2).rgb;
    }
    public void Update(PhysicalObject o)
    {
        if (o.room != null && Extensions.RngChance(0.50f * o.Temperature() * o.Temperature())) {
            o.room.AddObject(new LavaFireSprite(o.firstChunk.pos + Random.insideUnitCircle * 3));
        }
    }
}
