using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static LavaCat.Extensions;

namespace LavaCat;

static class HeatHooks
{
    private static void UpdateWaterCollision(PhysicalObject o)
    {
        if (o.Temperature() <= 0 || o.room == null) {
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
        // Special behavior for heated objects
        On.Player.Collide += Player_Collide;
        On.PhysicalObject.Collide += PhysicalObject_Collide;
        On.PhysicalObject.Update += PhysicalObject_Update;

        // Fix saving
        On.AbstractCreature.Realize += AbstractCreature_Realize;
        On.SaveState.AbstractCreatureToString_AbstractCreature_WorldCoordinate += CritToString;
        On.SaveState.AbstractCreatureFromString += SaveState_AbstractCreatureFromString;

        // Fix misc creature behavior
        On.Creature.Violence += Creature_Violence;
        On.Creature.Update += Creature_Update;
        On.PreyTracker.TrackedPrey.Attractiveness += TrackedPrey_Attractiveness;
        On.ScavengerAI.CollectScore_PhysicalObject_bool += ScavengerAI_CollectScore_PhysicalObject_bool;
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
            if (self is Creature crit && !crit.ImmuneToHeat() && otherObject is Creature otherCrit && otherCrit.Temperature() > crit.Temperature() && otherCrit.Temperature() > 0.1f) {
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

        foreach (var stick in self.abstractPhysicalObject.stuckObjects) {
            AbstractPhysicalObject notMe = stick.A == self.abstractPhysicalObject ? stick.B : stick.A;

            if (notMe.realizedObject is PhysicalObject other) {
                // Player doesn't lose heat if just holding an object
                bool retainHeat = stick.RetainsHeat();

                self.EqualizeHeat(other, drainHeat: !retainHeat);
            }
        }

        // Lose heat to diffusion
        if (!self.RetainsHeat()) {
            self.Temperature() -= 0.01f * self.Temperature() * self.HeatProperties().Conductivity;

            if (self.Temperature() < 0.00001f) {
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
            self.room?.PlaySound(SoundID.Gate_Water_Steam_Puff, self.firstChunk.pos, 0.4f, 1.15f);
        }

        UpdateWaterCollision(self);
    }

    // Burning save data

    private static void AbstractCreature_Realize(On.AbstractCreature.orig_Realize orig, AbstractCreature self)
    {
        orig(self);

        if (self.realizedCreature != null && self.Burn() > 0) {
            self.realizedCreature.Burn() = self.Burn();
        }
    }

    private static string CritToString(On.SaveState.orig_AbstractCreatureToString_AbstractCreature_WorldCoordinate orig, AbstractCreature critter, WorldCoordinate pos)
    {
        string ret = orig(critter, pos);
        if (critter.Burn() > 0) {
            ret += $"<cA>BURN[{critter.Burn()}]";
        }
        return ret;
    }

    private static AbstractCreature SaveState_AbstractCreatureFromString(On.SaveState.orig_AbstractCreatureFromString orig, World world, string creatureString, bool onlyInCurrentRegion)
    {
        AbstractCreature ret = orig(world, creatureString, onlyInCurrentRegion);
        try {
            int index = creatureString.IndexOf("<cA>BURN[");
            if (index != -1) {
                int start = index + "<cA>BURN[".Length;
                int end = creatureString.IndexOf("]", start);
                string number = creatureString.Substring(start, end - start);
                ret.Burn() = float.Parse(number);
            }
        } catch (System.Exception e) {
            Plugin.Logger.LogError(e);
        }
        return ret;
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

            Plugin.Logger.LogDebug($"LavaCat reduced {damageOriginal:0.00} damage to {damage * (1 - reduction):0.00}, then lost {damageOriginal * 0.05f:0.00} temperature");
        }

        orig(crit, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
    }

    private static void Creature_Update(On.Creature.orig_Update orig, Creature crit, bool eu)
    {
        if (crit is Player pl && pl.IsLavaCat() || crit.room == null) {
            orig(crit, eu);
            return;
        }

        // If grabbing a burning creature and can't withstand the heat, drop it
        if (crit.grasps != null) {
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

        if (crit.abstractCreature != null && crit.Burn() > 0) {
            crit.abstractCreature.Burn() = crit.Burn();
        }

        orig(crit, eu);

        if (crit.Burn() > 0) {
            crit.State.meatLeft = 0;
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
                if (!crit.RetainsHeat())
                    crit.Temperature() *= 0.9f;

                crit.Stun((int)(10 * temp + 5 * Random.value + 5));

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

            if (temp > 0.2f && crit.Template.smallCreature) {
                crit.Die();
            }
            if (temp > 0.9f * crit.Template.instantDeathDamageLimit) {
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
        if (tracked.critRep.representedCreature.Burn() > 0) {
            return 0;
        }
        if (tracked.owner.AI.creature.AvoidsHeat()) {
            float temp = tracked.critRep.representedCreature.Temperature();

            return Mathf.Lerp(orig(tracked), 0, temp * 0.9f);
        }
        return orig(tracked);
    }

    private static int ScavengerAI_CollectScore_PhysicalObject_bool(On.ScavengerAI.orig_CollectScore_PhysicalObject_bool orig, ScavengerAI self, PhysicalObject obj, bool weaponFiltered)
    {
        if (obj != null && obj.Temperature() > 0.1f) {
            return self.creature.AvoidsHeat() ? 0 : orig(self, obj, weaponFiltered);
        }
        return orig(self, obj, weaponFiltered);
    }
}
