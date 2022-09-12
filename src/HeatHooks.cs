using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static LavaCat.Extensions;

namespace LavaCat;

struct HeatProperties
{
    public float Conductivity { get; set; }
    public bool IsFood { get; set; }
}

static class HeatHooks
{
    public static void Apply()
    {
        // Let player heat objects up
        IL.Player.GrabUpdate += Player_GrabUpdate;

        // Special behavior for heated objects
        On.PhysicalObject.Collide += PhysicalObject_Collide;
        On.PhysicalObject.Update += PhysicalObject_Update;
        On.SeedCob.Update += SeedCob_Update;
        On.Creature.Update += Creature_Update;
        On.PreyTracker.TrackedPrey.Attractiveness += TrackedPrey_Attractiveness;

        On.Fly.Update += Fly_Update;

        On.Spear.HitSomething += Spear_HitSomething;
        On.Spear.Update += Spear_Update;
        On.Spear.DrawSprites += Spear_DrawSprites;

        On.Rock.HitSomething += Rock_HitSomething;
        On.Rock.Update += Rock_Update;
        On.Rock.DrawSprites += Rock_DrawSprites;

        On.FirecrackerPlant.Update += FirecrackerPlant_Update;
        On.ExplosiveSpear.Update += ExplosiveSpear_Update;
        On.ScavengerBomb.Update += ScavengerBomb_Update;

        // TODO explosives should explode, flarebombs turn orange, various things should smoke/ignite/cook
    }

    private static bool FireParticleChance(float temp)
    {
        return RngChance(0.50f * temp * temp);
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
                    if ((chunk.pos - drip.pos).MagnitudeLt(chunk.rad + drip.width)) {
                        o.Cool(1 / 200f, 0.22f, drip.pos, -drip.vel * 0.5f);

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

        // Add any objects that were spawned while iterating, like steam particles  
        iterate.AddRange(newObjects);

        o.room.updateList = iterate;
    }

    private static HeatProperties HeatProperties(PhysicalObject o)
    {
        static HeatProperties Inedible(float conductivity) => new() { Conductivity = conductivity };
        static HeatProperties Edible(float conductivity) => new() { Conductivity = conductivity, IsFood = true };

        return o switch {
            IPlayerEdible or WaterNut or FlyLure => Edible(0.2f),
            Spear => Inedible(0.80f),
            Rock => Inedible(0.05f),
            Player p => Inedible(p.IsLavaCat() ? 0.01f : 0.10f),
            Creature => Inedible(0.10f),
            _ => Inedible(0.01f),
        };
    }

    private static HSLColor HeatedBlackColor(float minLightness, float temp)
    {
        return new(hue: Mathf.Lerp(0f, Plugin.LavaColor.hue, temp),
                   saturation: Mathf.Lerp(0.5f, 1f, temp),
                   lightness: Mathf.Lerp(minLightness, 1f, temp * temp)
                   );
    }

    private static void Equalize(PhysicalObject self, PhysicalObject other, float speed = 0.05f, bool losePlayerHeat = false)
    {
        // Lighter objects should lose heat faster than heavier objects.
        float massRatio = other.TotalMass / (other.TotalMass + self.TotalMass);
        float conductivity = HeatProperties(self).Conductivity;
        float heatFlow = other.Temperature() - self.Temperature();

        // LavaCat can't be cooled down by other objects
        bool canSelfCool = losePlayerHeat || !(self is Player p && p.IsLavaCat());
        bool canOtherCool = losePlayerHeat || !(other is Player p2 && p2.IsLavaCat());

        if (canSelfCool || heatFlow > 0)
            self.Temperature() += heatFlow * conductivity * speed * massRatio;

        if (canOtherCool || heatFlow < 0)
            other.Temperature() -= heatFlow * conductivity * speed * (1 - massRatio);
    }


    private static void Player_GrabUpdate(ILContext il)
    {
        ILCursor cursor = new(il);

        cursor.GotoNext(MoveType.Before, i => i.MatchLdcI4(0) && i.Next.MatchStloc(0));
        cursor.Index += 2;

        // Overwrite num0, which decides if the player should eat/swallow or not
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloca, il.Body.Variables[0]);
        cursor.EmitDelegate(EatFoodHook);

        cursor.GotoNext(MoveType.Before, i => i.MatchCall<Player>("HeavyCarry"));
        cursor.GotoNext(MoveType.Before, i => i.MatchStloc(out _));

        // Prevent lavacat from dropping critters when trying to overheat them to death
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate(ReleaseHeavyObjectHook);
    }

    private static void EatFoodHook(Player player, ref bool result)
    {
        if (player.IsLavaCat()) {
            bool heat = result;

            result = false;

            if (heat && player.input[0].pckp && player.bodyChunks[0].submersion <= 0 && player.bodyChunks[1].submersion <= 0) {
                foreach (var grasp in player.grasps) {
                    if (grasp?.grabbed is PhysicalObject o && HeatUpdate(player, o, grasp)) {
                        return;
                    }
                }
            }

            player.HeatProgress() = 0;
        }

        static bool HeatUpdate(Player player, PhysicalObject o, Creature.Grasp grasp)
        {
            ref float progress = ref player.HeatProgress();

            bool isFood = HeatProperties(o).IsFood;

            if (!isFood && o.Temperature() - player.Temperature() > -0.01f) {
                return false;
            }

            if (progress > 1f && isFood) {
                progress = 0;

                o.Destroy();
                o.BurstIntoFlame();

                player.TemperatureChange() += o.TotalMass + 0.005f;
            }

            if (progress > 1 / 30f) {
                player.Blink(5);
                player.BlindTimer() = 10;

                int particleCount = (int)Rng(0, progress * 10);
                for (int i = 0; i < particleCount; i++) {
                    LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.5f, foreground: RngChance(0.50f));
                    particle.vel.x *= 0.5f;
                    particle.vel.y *= 1.5f;
                    player.room.AddObject(particle);
                }

                player.WispySmoke().Emit(player.Hand(grasp).pos, new Vector2(0, 0.5f), Plugin.LavaColor.rgb);

                // Show food bar for food items
                if (isFood) {
                    player.abstractCreature.world.game.cameras[0].hud.foodMeter.visibleCounter = 100;
                }
                // Heat up non-food items rapidly by holding PCKP
                else {
                    Equalize(player, o, progress * 0.25f);
                }
            }

            if (isFood) {
                progress += 1 / (80f + 160f * o.TotalMass);
            }
            else {
                progress += 1 / 80f;
            }

            return true;
        }
    }

    private static bool ReleaseHeavyObjectHook(bool dropHeavyObject, Player player)
    {
        return dropHeavyObject && (!player.IsLavaCat() || player.input[0].y < 0);
    }
    
    // -- Physics --

    private static void PhysicalObject_Collide(On.PhysicalObject.orig_Collide orig, PhysicalObject self, PhysicalObject otherObject, int myChunk, int otherChunk)
    {
        orig(self, otherObject, myChunk, otherChunk);

        bool connected = self.abstractPhysicalObject.stuckObjects.Any(s => s.B == otherObject.abstractPhysicalObject || s.B == otherObject.abstractPhysicalObject);
        if (!connected) {
            Equalize(self, otherObject, speed: 0.15f);
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

                // Player doesn't lose heat if just holding an object
                bool retainHeat = stick is AbstractPhysicalObject.CreatureGripStick grip && grip.A.realizedObject is Player p3 && p3.IsLavaCat();

                Equalize(self, other, losePlayerHeat: !retainHeat);
            }
        }

        // Lose heat to diffusion
        if (!touchingLavaCat) {
            // More massive things take slightly longer to cool down
            float heatConservation = self.TotalMass / (self.TotalMass + 0.15f);

            self.Temperature() *= 0.992f + 0.004f * heatConservation;
        }

        if (self.TemperatureChange() < 0) {
            throw new System.InvalidOperationException();
        }

        float flow = self.TemperatureChange() * 0.1f;
        self.Temperature() += flow;
        self.TemperatureChange() -= flow;

        if (self.SteamSound() > 0) {
            self.SteamSound() -= 1;
            self.room.PlaySound(SoundID.Gate_Water_Steam_Puff, self.firstChunk.pos, 0.4f, 1.15f);
        }

        UpdateWaterCollision(self);
    }

    private static void SeedCob_Update(On.SeedCob.orig_Update orig, SeedCob cob, bool eu)
    {
        orig(cob, eu);

        // Burn seed cobs for a large amount of heat
        if (!cob.AbstractCob.dead && cob.open > 0.95f) {
            foreach (var player in cob.room.game.Players) {
                if (player.realizedObject is Player p && p.IsLavaCat() && p.room == cob.room && p.eatExternalFoodSourceCounter <= 10 && p.Temperature() < 0.9f) {
                    int freeHand = p.FreeHand();
                    if (freeHand != -1) {
                        Burn(p, cob, freeHand);
                        break;
                    }
                }
            }
        }

        static void Burn(Player player, SeedCob cob, int hand)
        {
            player.Blink(5);
            player.Graf().objectLooker.LookAtNothing();

            int particleCount = (int)Rng(0, cob.Temperature() * cob.Temperature() * 10);
            for (int i = 0; i < particleCount; i++) {
                LavaFireSprite particle = new(cob.seedPositions.RandomElement(), foreground: RngChance(0.50f));
                particle.vel.x *= 0.5f;
                particle.vel.y *= 1.5f;
                player.room.AddObject(particle);
            }

            player.room.FireSmoke().Emit(player.Hand(hand).pos, new Vector2(0, 0.5f), Plugin.LavaColor.rgb, 10);
        }
    }

    // Burning creatures

    private static void Creature_Update(On.Creature.orig_Update orig, Creature crit, bool eu)
    {
        if (crit is not Player && crit.grasps != null) {
            foreach (var grasp in crit.grasps) {
                if (grasp?.grabbed != null && RngChance(grasp.grabbed.Temperature() * 0.1f)) {
                    crit.ReleaseGrasp(grasp.graspUsed);
                    crit.abstractCreature.AvoidsHeat() = true;
                }
            }
        }

        orig(crit, eu);

        if (crit is Player player && player.IsLavaCat() || crit.room == null) {
            return;
        }

        float temp = crit.Temperature();

        if (temp > 0.05f) {
            BurnCrit(crit, temp);
        }

        static void BurnCrit(Creature crit, float temp)
        {
            // Stun creatures
            if (RngChance(0.06f)) {
                int stunTime = (int)Mathf.Lerp(2, 25 * crit.Template.baseStunResistance, temp * temp);

                crit.Stun(stunTime);

                crit.Temperature() *= 0.95f;

                Fx(crit, temp, crit.bodyChunks.RandomElement());
            }

            // Stun bonus is negative to prevent any stunning at all.
            float damage = temp / 40f;
            float stunBonus = damage * -30;

            // We're using explosion damage as a stand-in for fire damage. This ignores explosion damage resistance.
            if (crit.Template.damageRestistances[4, 0] > 0) {
                damage *= crit.Template.damageRestistances[4, 0];
            }

            BodyChunk chunk = crit.bodyChunks.RandomElement();

            bool critWasDead = crit.dead;

            crit.Violence(null, null, chunk, null, Creature.DamageType.Explosion, damage, stunBonus);

            var burningGrasp = crit.grabbedBy.FirstOrDefault(g => g.grabber.Temperature() > temp);
            if (burningGrasp != null) {
                if (crit.dead && !critWasDead) {
                    crit.room.socialEventRecognizer.Killing(burningGrasp.grabber, crit);
                }
                else if (RngChance(1 / 80f)) {
                    crit.room.socialEventRecognizer.SocialEvent(SocialEventRecognizer.EventID.NonLethalAttack, burningGrasp.grabber, crit, null);

                    (burningGrasp.grabber as Player)?.SessionRecord?.BreakPeaceful(crit);
                }
            }
        }

        static void Fx(Creature crit, float temp, BodyChunk chunk)
        {
            crit.room.PlaySound(SoundID.Firecracker_Burn, chunk.pos, 0.2f, 1.2f);

            Vector2 pos = chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f;
            Vector2 vel = new Vector2(0, 5) + Random.insideUnitCircle * 5;
            float intensity = 0.1f + 0.9f * temp;
            crit.room.Steam().Emit(pos, vel, intensity);
        }
    }

    private static float TrackedPrey_Attractiveness(On.PreyTracker.TrackedPrey.orig_Attractiveness orig, PreyTracker.TrackedPrey tracked)
    {
        if (tracked.owner.AI.creature.AvoidsHeat()) {
            float temp = tracked.critRep.representedCreature.Temperature();

            return orig(tracked) * (1f - temp * temp);
        }
        return orig(tracked);
    }

    // -- Specific behavior --

    private static void Fly_Update(On.Fly.orig_Update orig, Fly fly, bool eu)
    {
        orig(fly, eu);

        if (fly.room != null) {
            if (fly.Temperature() > 0.1f) {
                fly.Die();
                fly.bites = Mathf.Min(fly.bites, 2);
            }
            if (fly.Temperature() > 0.3f) {
                fly.bites = Mathf.Min(fly.bites, 1);
            }
            if (fly.Temperature() > 0.5f) {
                fly.Destroy();
                fly.BurstIntoFlame();
            }

            int particleCount = (int)Rng(0, fly.Temperature() * 5);

            for (int i = 0; i < particleCount; i++) {
                fly.room.AddObject(new LavaFireSprite(fly.firstChunk.pos + Random.insideUnitCircle * 2, foreground: RngChance(0.5f)));
            }
        }
    }

    private static bool Spear_HitSomething(On.Spear.orig_HitSomething orig, Spear self, SharedPhysics.CollisionResult result, bool eu)
    {
        float bonus = self.spearDamageBonus;

        self.spearDamageBonus += 0.5f * self.Temperature();

        bool ret = orig(self, result, eu);

        self.spearDamageBonus = bonus;

        return ret;
    }

    private static void Spear_Update(On.Spear.orig_Update orig, Spear spear, bool eu)
    {
        const float halfLength = 22;

        orig(spear, eu);

        float temp = spear.Temperature();

        if (spear.room != null && FireParticleChance(temp)) {
            LavaFireSprite sprite = new(spear.firstChunk.pos + Random.insideUnitCircle * 2 + spear.rotation * Rng(-halfLength, halfLength));
            sprite.life *= 0.7f;
            spear.room.AddObject(sprite);
        }

        // Smoky tip
        if (temp > 0.1f && FireParticleChance(temp)) {
            Color fireColor = Plugin.LavaColor.rgb * temp * temp;

            spear.WispySmoke().Emit(spear.firstChunk.pos + spear.rotation * halfLength, new Vector2(0, 0.5f), fireColor);
        }
    }

    private static void Spear_DrawSprites(On.Spear.orig_DrawSprites orig, Spear self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        if (self.blink <= 0) {
            sLeaser.sprites[0].color = HeatedBlackColor(self.color.HSL().lightness, self.Temperature()).rgb;
        }
    }

    private static bool Rock_HitSomething(On.Rock.orig_HitSomething orig, Rock self, SharedPhysics.CollisionResult result, bool eu)
    {
        if (result.obj != null) {
            Equalize(self, result.obj, 0.20f, losePlayerHeat: true);
        }

        return orig(self, result, eu);
    }

    private static void Rock_Update(On.Rock.orig_Update orig, Rock self, bool eu)
    {
        orig(self, eu);

        self.buoyancy = 0.4f + 0.6f * self.Temperature();

        if (self.room != null && FireParticleChance(self.Temperature())) {
            self.room.AddObject(new LavaFireSprite(self.firstChunk.pos + Random.insideUnitCircle * 3));
        }
    }

    private static void Rock_DrawSprites(On.Rock.orig_DrawSprites orig, Rock self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        HSLColor color = HeatedBlackColor(self.color.HSL().lightness, self.Temperature());

        if (self.blink <= 0) {
            sLeaser.sprites[0].color = color.rgb;

            color.saturation *= 0.5f;
            color.lightness *= 0.5f;
            sLeaser.sprites[1].color = color.rgb;
        }
    }

    private static void FirecrackerPlant_Update(On.FirecrackerPlant.orig_Update orig, FirecrackerPlant self, bool eu)
    {
        orig(self, eu);

        if (self.Temperature() > 0.1f && self.fuseCounter == 0) {
            self.Ignite();
            self.fuseCounter += 20;
        }
    }

    private static void ExplosiveSpear_Update(On.ExplosiveSpear.orig_Update orig, ExplosiveSpear self, bool eu)
    {
        orig(self, eu);

        if (self.Temperature() > 0.1f && self.igniteCounter == 0) {
            self.Ignite();
            self.explodeAt += 20;
        }
    }

    private static void ScavengerBomb_Update(On.ScavengerBomb.orig_Update orig, ScavengerBomb self, bool eu)
    {
        orig(self, eu);

        if (self.Temperature() > 0.1f) {
            self.burn = Rng(0.8f, 1);
            self.room.PlaySound(SoundID.Fire_Spear_Ignite, self.firstChunk, false, 0.5f, 1.4f);
        }
    }
}
