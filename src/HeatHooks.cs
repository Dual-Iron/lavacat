using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static LavaCat.Extensions;

namespace LavaCat;

static class HeatHooks
{
    private static void UpdateWaterCollision(PhysicalObject o)
    {
        if (o.Temperature() <= 0) {
            return;
        }

        if (o is Creature c && c.rainDeath > 0) {
            o.RandomChunk.Cool(c.rainDeath / 40f, c.rainDeath * 0.5f, c.mainBodyChunk.pos, new Vector2(0, 5) + Random.insideUnitCircle * 5);
        }

        // Cool down if any part of the body is submerged
        foreach (var chunk in o.bodyChunks) {
            bool mainChunk = o.bodyChunks.Length == 0 || o is Creature c2 && c2.mainBodyChunk == chunk;
            if (chunk.submersion > 0) {
                // Reduce water level at position
                if (o.room.waterObject != null) {
                    o.room.waterObject.WaterfallHitSurface(chunk.pos.x - chunk.rad, chunk.pos.x + chunk.rad, chunk.submersion);

                    // Divide by room width so that bigger rooms lose water level slower.
                    // E.g. shoreline should evaporate MUCH slower than a flooded shelter
                    o.room.waterObject.fWaterLevel -= chunk.submersion / o.room.Width;
                }

                chunk.Cool(
                    temperatureLoss: chunk.submersion / (mainChunk ? 100 : 400),
                    smokeIntensity: chunk.submersion * 0.5f,
                    smokePos: chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f,
                    smokeVel: new Vector2(0, 5) + Random.insideUnitCircle * 5
                );
            }
        }

        // Iterate room's objects safely
        var iterate = o.room.updateList;
        var newObjects = new List<UpdatableAndDeletable>();

        o.room.updateList = newObjects;

        foreach (var updateable in iterate) {
            if (updateable is WaterDrip drip) {
                foreach (var chunk in o.bodyChunks) {
                    if ((chunk.pos - drip.pos).MagnitudeLt(chunk.rad + drip.width)) {
                        chunk.Cool(1 / 200f, 0.22f, drip.pos, -drip.vel * 0.5f);

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
                        chunk.Cool(1 / 600f * waterfall.flow, 0.3f, chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f, Random.insideUnitCircle * 2);
                    }
                }
            }
        }

        // Add any objects that were spawned while iterating, like steam particles  
        iterate.AddRange(newObjects);

        o.room.updateList = iterate;
    }

    public static void Apply()
    {
        // Let player heat objects up
        On.Player.GrabUpdate += Player_GrabUpdate;
        On.Player.ReleaseObject += Player_ReleaseObject;

        // Special behavior for heated objects
        On.Player.Collide += Player_Collide;
        On.PhysicalObject.Collide += PhysicalObject_Collide;
        On.PhysicalObject.Update += PhysicalObject_Update;

        On.Creature.Violence += Creature_Violence;
        On.Creature.Update += Creature_Update;
        On.PreyTracker.TrackedPrey.Attractiveness += TrackedPrey_Attractiveness;
        On.ScavengerAI.CollectScore_PhysicalObject_bool += ScavengerAI_CollectScore_PhysicalObject_bool;
    }

    // Fix grab update
    static bool grabUpdate;
    private static void Player_GrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
    {
        bool jmp = self.input[0].jmp;

        if (self.IsLavaCat()) {
            self.input[0].jmp = true;
            grabUpdate = true;
        }

        try {
            orig(self, eu);
        } finally {
            grabUpdate = false;
            self.input[0].jmp = jmp;
        }

        GrabUpdate(self);
    }

    private static void Player_ReleaseObject(On.Player.orig_ReleaseObject orig, Player self, int grasp, bool eu)
    {
        if (grabUpdate && self.input[0].y > -1 && self.grasps[grasp]?.grabbed is PhysicalObject o && self.HeavyCarry(o)) {
            return;
        }
        orig(self, grasp, eu);
    }

    private static void GrabUpdate(Player player)
    {
        if (player.IsLavaCat()) {
            bool heat = player.input[0].x == 0 && player.input[0].y == 0 && !player.input[0].jmp && !player.input[0].thrw;
            if (heat && player.input[0].pckp && player.Submersion <= 0) {
                foreach (var grasp in player.grasps) {
                    if (grasp?.grabbed is PhysicalObject o && CanHeat(player, o)) {
                        HeatUpdate(player, o, grasp);
                        return;
                    }
                }
            }

            bool suicide = player.input[0].x == 0 && player.input[0].y == 0 && !player.input[0].jmp && !player.input[0].thrw;
            if (suicide && player.input[0].pckp && player.Submersion >= 0.99f && player.grasps.All(g => g == null)) {
                Suicide(player);
                return;
            }

            player.HeatProgress() = 0;
        }

        static bool CanHeat(Player p, PhysicalObject o)
        {
            bool isFood = o.HeatProperties().IsEdible;
            if (!isFood && o.Temperature() - p.Temperature() > -0.01f) {
                return false;
            }
            return !o.slatedForDeletetion;
        }

        static void HeatUpdate(Player player, PhysicalObject o, Creature.Grasp grasp)
        {
            ref float progress = ref player.HeatProgress();

            bool isFood = o.HeatProperties().IsEdible;
            if (progress >= 1f && isFood) {
                progress = 0;

                player.SessionRecord?.AddEat(o);
                o.Destroy();
                o.BurstIntoFlame();

                player.TemperatureChange() += o.FoodHeat();
            }

            if (progress > 1/4f) {
                player.Blink(5);
                player.BlindTimer() = 10;

                player.WispySmoke(grasp.graspUsed).Emit(player.Hand(grasp).pos, new Vector2(0, 0.5f), LavaColor.rgb);

                // Show food bar for food items
                if (isFood) {
                    if (o is Creature c) c.Die();

                    if (player.abstractCreature.world.game.cameras[0].hud?.foodMeter != null)
                        player.abstractCreature.world.game.cameras[0].hud.foodMeter.visibleCounter = 200;

                    int particleCount = (int)Rng(0, progress * 10);
                    for (int i = 0; i < particleCount; i++) {
                        LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.5f, foreground: RngChance(0.50f));
                        particle.vel.x *= 0.5f;
                        particle.vel.y *= 1.5f;
                        particle.lifeTime += (int)(progress * 40);
                        player.room.AddObject(particle);
                    }
                }
                // Heat up non-food items rapidly by holding PCKP
                else {
                    player.EqualizeHeat(o, progress * 0.25f);
                }
            }

            if (CanHeat(player, o)) {
                float progressTime = isFood
                    ? (80 + 160 * o.TotalMass) / o.HeatProperties().EatSpeed
                    : 80;
                progress += 1 / progressTime;
                progress = Mathf.Clamp01(progress);
            }
            else {
                progress = 0;
            }
        }

        static void Suicide(Player player)
        {
            ref float progress = ref player.HeatProgress();

            if (progress > 1f) {
                progress = 0;

                player.room.PlaySound(SoundID.Water_Nut_Swell, player.firstChunk,false, 1, 0.8f);

                // TODO: fugly head explosion
                player.deaf += 500000;
                player.Die();
            }

            if (progress > 1/2f) {
                if (RngChance(0.1f * progress * progress)) {
                    player.room.Steam().Emit(player.firstChunk.pos, Random.insideUnitCircle * (2 + 6 * progress), 0.5f);
                    player.SteamSound() = 7;
                }

                player.standing = false;

                Vector2 shake = Random.insideUnitCircle * 2 * progress * progress;
                player.bodyChunks[0].pos += shake;
                player.bodyChunks[0].vel += shake;
                player.bodyChunks[1].pos -= shake;
                player.bodyChunks[1].vel -= shake;
            }
            else if (progress > 1/4f) {
                player.standing = false;
            }

            progress += 1 / 400f;
        }
    }

    // -- Physics --

    private static void Player_Collide(On.Player.orig_Collide orig, Player self, PhysicalObject otherObject, int myChunk, int otherChunk)
    {
        orig(self, otherObject, myChunk, otherChunk);

        Collide(self, otherObject);
    }

    private static void PhysicalObject_Collide(On.PhysicalObject.orig_Collide orig, PhysicalObject self, PhysicalObject otherObject, int myChunk, int otherChunk)
    {
        orig(self, otherObject, myChunk, otherChunk);

        Collide(self, otherObject);
    }

    private static void Collide(PhysicalObject self, PhysicalObject otherObject)
    {
        bool connected = self.abstractPhysicalObject.stuckObjects.Any(s => s.A == otherObject.abstractPhysicalObject || s.B == otherObject.abstractPhysicalObject);
        if (!connected) {
            // If being touched by a hot creature, blame it for our death
            if (self is Creature crit && otherObject is Creature otherCrit && otherCrit.Temperature() > crit.Temperature() && otherCrit.Temperature() > 0.1f) {
                crit.SetKillTag(otherCrit.abstractCreature);

                float diff = otherCrit.Temperature() - crit.Temperature();
                if (RngChance(diff / 30f)) {
                    crit.room.socialEventRecognizer.SocialEvent(SocialEventRecognizer.EventID.LethalAttackAttempt, otherCrit, crit, null);
                }

                float resist = crit.Template.baseStunResistance.Max(1);
                if (RngChance(0.5f * diff * diff / resist)) {
                    crit.Stun((int)(8 * diff));
                }
            }

            self.EqualizeHeat(otherObject, speed: 0.07f);
        }
    }

    private static void PhysicalObject_Update(On.PhysicalObject.orig_Update orig, PhysicalObject self, bool eu)
    {
        orig(self, eu);

        bool heldByLavaCat = self is Player p && p.IsLavaCat();

        foreach (var stick in self.abstractPhysicalObject.stuckObjects) {
            AbstractPhysicalObject notMe = stick.A == self.abstractPhysicalObject ? stick.B : stick.A;

            if (notMe.realizedObject is PhysicalObject other) {
                // Player doesn't lose heat if just holding an object
                bool retainHeat = stick is AbstractPhysicalObject.CreatureGripStick or Player.AbstractOnBackStick && stick.A.realizedObject is Player p3 && p3.IsLavaCat();
                heldByLavaCat |= retainHeat;

                self.EqualizeHeat(other, losePlayerHeat: !retainHeat);
            }
        }

        // Lose heat to diffusion
        if (!heldByLavaCat) {
            self.Temperature() -= 0.01f * self.Temperature() * self.HeatProperties().Conductivity;

            if (self.Temperature() < 0.000001f) {
                self.Temperature() = 0f;
            }
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

    // Burning creatures

    private static void Creature_Violence(On.Creature.orig_Violence orig, Creature crit, BodyChunk source, Vector2? directionAndMomentum, BodyChunk hitChunk, PhysicalObject.Appendage.Pos hitAppendage, Creature.DamageType type, float damage, float stunBonus)
    {
        // Rocks do extra damage while hot
        if (source?.owner is Rock r && r.Temperature() > crit.Temperature()) {
            damage += 0.2f * (r.Temperature() - crit.Temperature());
        }

        // Lavacat takes less damage and stun while hot
        if (crit is Player p && p.IsLavaCat()) {
            float damageOriginal = damage;
            float reduction = p.Temperature() * 0.6f;

            if (p.Temperature() > 0.5f) {
                damage *= 1 - reduction;
                stunBonus *= 1 - reduction;
            }

            p.Temperature() -= damageOriginal * 0.05f;

            Plugin.Logger.LogDebug($"LavaCat reduced {damageOriginal:0.00} damage to {damage * (1 - reduction):0.00}, then lost {damageOriginal * 5:0.00%} temperature");
        }

        orig(crit, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
    }

    private static void Creature_Update(On.Creature.orig_Update orig, Creature crit, bool eu)
    {
        bool isLavaCat = crit is Player pl && pl.IsLavaCat();

        // If grabbing a burning creature and can't withstand the heat, drop it
        if (!isLavaCat && crit.grasps != null) {
            foreach (var grasp in crit.grasps) {
                if (grasp?.grabbed == null) {
                    continue;
                }

                // Creatures with higher pain tolerance (stun resistance) should hold onto grasps longer.
                var resist = Mathf.Max(0.5f, crit.Template.baseStunResistance);
                var diff = grasp.grabbed.Temperature() - crit.Temperature();
                if (diff > 0.1f && RngChance(diff * diff / resist)) {
                    crit.abstractCreature.AvoidsHeat() = true;
                    crit.ReleaseGrasp(grasp.graspUsed);
                    crit.Stun((int)(16 * diff));
                }
            }
        }

        orig(crit, eu);

        if (isLavaCat || crit.room == null) {
            return;
        }

        float temp = crit.Temperature();

        if (temp > 0.1f) {
            foreach (var grasp in crit.grabbedBy) {
                // If grabbed by a burning creature, blame it for our death
                if (grasp.grabber.Temperature() > crit.Temperature()) {
                    var grabber = grasp.grabber;

                    crit.SetKillTag(grabber.abstractCreature);

                    float difference = grabber.Temperature() - crit.Temperature();
                    if (crit.State.alive && RngChance(difference / 20f)) {
                        crit.room.socialEventRecognizer.SocialEvent(SocialEventRecognizer.EventID.LethalAttackAttempt, grabber, crit, null);

                        (grabber as Player)?.SessionRecord?.BreakPeaceful(crit);
                    }
                }
            }

            // Blow off steam occasionally and get stunned
            if (RngChance(0.06f)) {

                crit.Stun((int)(8 * temp + 8 * Random.value));

                crit.Temperature() *= 0.9f;

                Fx(crit.RandomChunk, temp);
            }

            // Stun bonus is negative to prevent any stunning at all.
            float damage = temp / 120f;
            float stunBonus = damage * -30;

            // We're using explosion damage as a stand-in for fire damage. This ignores explosion damage resistance.
            if (crit.Template.damageRestistances[4, 0] > 0) {
                damage *= crit.Template.damageRestistances[4, 0];
            }

            BodyChunk chunk = crit.RandomChunk;

            crit.Violence(null, null, chunk, null, Creature.DamageType.Explosion, damage, stunBonus);

            if (crit.Template.smallCreature && temp > 0.2f) {
                crit.Die();
            }
        }

        static void Fx(BodyChunk chunk, float temp)
        {
            if (temp >= chunk.owner.HeatProperties().DryTemp) {
                return;
            }

            Vector2 pos = chunk.pos + Random.insideUnitCircle * chunk.rad * 0.5f;
            Vector2 vel = new Vector2(0, 5) + Random.insideUnitCircle * 5;
            float intensity = 0.1f + 0.9f * temp;

            chunk.owner.room.Steam().Emit(pos, vel, intensity);
            chunk.owner.room.PlaySound(SoundID.Firecracker_Burn, chunk.pos, 0.2f, 1.2f);
        }
    }

    private static float TrackedPrey_Attractiveness(On.PreyTracker.TrackedPrey.orig_Attractiveness orig, PreyTracker.TrackedPrey tracked)
    {
        if (tracked.owner.AI.creature.AvoidsHeat()) {
            float temp = tracked.critRep.representedCreature.Temperature();

            return Mathf.Lerp(orig(tracked), 0, temp * 0.9f);
        }
        return orig(tracked);
    }

    private static int ScavengerAI_CollectScore_PhysicalObject_bool(On.ScavengerAI.orig_CollectScore_PhysicalObject_bool orig, ScavengerAI self, PhysicalObject obj, bool weaponFiltered)
    {
        if (self.creature.AvoidsHeat() && obj.Temperature() > 0.1f || obj is DataPearl p && p.AbstractPearl.dataPearlType == BurntPearl) {
            return 0;
        }
        return orig(self, obj, weaponFiltered);
    }
}
