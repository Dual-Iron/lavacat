using RWCustom;
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
            Spear => Inedible(0.50f),
            Rock => Inedible(0.05f),
            Player p => Inedible(p.IsLavaCat() ? 0.01f : 0.10f),
            DataPearl => Inedible(0.07f),
            Creature => Inedible(0.07f),
            _ => Inedible(0.025f),
        };
    }

    private static HSLColor HeatedBlackColor(float minLightness, float temp)
    {
        return new(hue: Mathf.Lerp(0f, LavaColor.hue, temp),
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

    public static void Apply()
    {
        // Let player heat objects up
        On.Player.GrabUpdate += Player_GrabUpdate;
        On.Player.ReleaseObject += Player_ReleaseObject;

        // Special behavior for heated objects
        On.PhysicalObject.Collide += PhysicalObject_Collide;
        On.PhysicalObject.Update += PhysicalObject_Update;

        On.Creature.Update += Creature_Update;
        On.PreyTracker.TrackedPrey.Attractiveness += TrackedPrey_Attractiveness;
        On.ScavengerAI.CollectScore_PhysicalObject_bool += ScavengerAI_CollectScore_PhysicalObject_bool;

        On.Fly.Update += Fly_Update;

        On.Spear.HitSomething += Spear_HitSomething;
        On.Spear.Update += Spear_Update;
        On.Spear.DrawSprites += Spear_DrawSprites;

        On.Rock.HitSomething += Rock_HitSomething;
        On.Rock.Update += Rock_Update;
        On.Rock.DrawSprites += Rock_DrawSprites;

        On.Lantern.Update += Lantern_Update;
        On.Lantern.DrawSprites += Lantern_DrawSprites;

        On.FlareBomb.Update += FlareBomb_Update;
        On.FlareBomb.DrawSprites += FlareBomb_DrawSprites;
        On.Creature.Blind += Creature_Blind;

        On.PuffBall.Update += PuffBall_Update;
        On.PuffBall.DrawSprites += PuffBall_DrawSprites;

        On.SporePlant.Update += SporePlant_Update;

        On.DataPearl.ApplyPalette += DataPearl_ApplyPalette;
        On.DataPearl.UniquePearlMainColor += DataPearl_UniquePearlMainColor;
        On.DataPearl.UniquePearlHighLightColor += DataPearl_UniquePearlHighLightColor;
        On.DataPearl.DrawSprites += DataPearl_DrawSprites;

        On.FirecrackerPlant.Update += FirecrackerPlant_Update;
        On.ExplosiveSpear.Update += ExplosiveSpear_Update;
        On.ScavengerBomb.Update += ScavengerBomb_Update;

        On.SeedCob.Update += SeedCob_Update;
        On.SeedCob.DrawSprites += SeedCob_DrawSprites;

        // TODO make misc objects start smoking when hot
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
            bool isFood = HeatProperties(o).IsFood;
            if (!isFood && o.Temperature() - p.Temperature() > -0.01f) {
                return false;
            }
            return !o.slatedForDeletetion;
        }

        static void HeatUpdate(Player player, PhysicalObject o, Creature.Grasp grasp)
        {
            ref float progress = ref player.HeatProgress();

            bool isFood = HeatProperties(o).IsFood;
            if (progress >= 1f && isFood) {
                progress = 0;

                o.Destroy();
                o.BurstIntoFlame();

                player.TemperatureChange() += o.TotalMass + 0.005f;
            }

            if (progress > 1/4f) {
                player.Blink(5);
                player.BlindTimer() = 10;

                player.WispySmoke(grasp.graspUsed).Emit(player.Hand(grasp).pos, new Vector2(0, 0.5f), LavaColor.rgb);

                // Show food bar for food items
                if (isFood) {
                    if (player.abstractCreature.world.game.cameras[0].hud?.foodMeter != null)
                        player.abstractCreature.world.game.cameras[0].hud.foodMeter.visibleCounter = 100;

                    int particleCount = (int)Rng(0, progress * 10);
                    for (int i = 0; i < particleCount; i++) {
                        LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.5f, foreground: RngChance(0.50f));
                        particle.vel.x *= 0.5f;
                        particle.vel.y *= 1.5f;
                        particle.lifeTime += (int)(progress * 80);
                        player.room.AddObject(particle);
                    }
                }
                // Heat up non-food items rapidly by holding PCKP
                else {
                    Equalize(player, o, progress * 0.25f);
                }
            }

            if (CanHeat(player, o)) {
                float progressTime = isFood ? 80 + 160 * o.TotalMass : 80;
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

    // Burning creatures

    private static void Creature_Update(On.Creature.orig_Update orig, Creature crit, bool eu)
    {
        bool isLavaCat = crit is Player pl && pl.IsLavaCat();

        if (!isLavaCat && crit.grasps != null) {
            foreach (var grasp in crit.grasps) {
                if (grasp?.grabbed != null) {
                    float temp2 = grasp.grabbed.Temperature();
                    if (temp2 > 0.1f && RngChance(temp2 * temp2 * 0.2f)) {
                        crit.ReleaseGrasp(grasp.graspUsed);
                        crit.abstractCreature.AvoidsHeat() = true;
                    }
                }
            }
        }

        orig(crit, eu);

        if (isLavaCat || crit.room == null) {
            return;
        }

        float temp = crit.Temperature();

        if (temp > 0.1f) {
            BurnCrit(crit, temp);
        }

        static void BurnCrit(Creature crit, float temp)
        {
            // Stun creatures
            if (RngChance(0.06f)) {
                int stunTime = (int)Mathf.Lerp(2, 10 * crit.Template.baseStunResistance, temp * temp);

                crit.Stun(stunTime);

                crit.Temperature() *= 0.95f;

                Fx(crit, temp, crit.bodyChunks.RandomElement());
            }

            // Stun bonus is negative to prevent any stunning at all.
            float damage = temp / 80f;
            float stunBonus = damage * -30;

            // We're using explosion damage as a stand-in for fire damage. This ignores explosion damage resistance.
            if (crit.Template.damageRestistances[4, 0] > 0) {
                damage *= crit.Template.damageRestistances[4, 0];
            }

            BodyChunk chunk = crit.bodyChunks.RandomElement();

            crit.Violence(null, null, chunk, null, Creature.DamageType.Explosion, damage, stunBonus);

            var burningGrasp = crit.grabbedBy.FirstOrDefault(g => g.grabber.Temperature() > temp);
            if (burningGrasp != null) {
                crit.SetKillTag(burningGrasp.grabber.abstractCreature);

                if (crit.State.alive && RngChance(1 / 80f)) {
                    crit.room.socialEventRecognizer.SocialEvent(SocialEventRecognizer.EventID.LethalAttackAttempt, burningGrasp.grabber, crit, null);

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
            Color fireColor = LavaColor.rgb * temp * temp;

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

    // Lanterns

    static readonly Color lanternHot = (LavaColor with { lightness = 0.9f }).rgb;

    private static void Lantern_Update(On.Lantern.orig_Update orig, Lantern self, bool eu)
    {
        orig(self, eu);

        float temp = self.Temperature();

        if (self.lightSource != null) {
            self.lightSource.color = Color.Lerp(new(1f, 0.2f, 0f), lanternHot, temp);
            self.lightSource.setRad += 250f * temp;
        }
    }

    private static void Lantern_DrawSprites(On.Lantern.orig_DrawSprites orig, Lantern self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        float temp = self.Temperature();

        sLeaser.sprites[0].color = Color.Lerp(new(1f, 0.2f, 0f), lanternHot, temp);
        sLeaser.sprites[2].color = Color.Lerp(Color.Lerp(new(1f, 0.2f, 0f), new(1f, 1f, 1f), 0.3f), lanternHot, temp);
        sLeaser.sprites[3].color = Color.Lerp(new(1f, 0.4f, 0.3f), lanternHot, temp);
    }

    // Flarebombs

    static FlareBomb blinding;
    private static void FlareBomb_Update(On.FlareBomb.orig_Update orig, FlareBomb self, bool eu)
    {
        self.color = Color.Lerp(new Color(0.2f, 0, 1), LavaColor.rgb, Mathf.Sqrt(self.Temperature()));

        blinding = self;
        try { orig(self, eu); }
        finally { blinding = null; }
    }

    private static void FlareBomb_DrawSprites(On.FlareBomb.orig_DrawSprites orig, FlareBomb self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
    }

    private static void Creature_Blind(On.Creature.orig_Blind orig, Creature self, int blnd)
    {
        if (blinding != null) {
            if (blinding.Temperature() > 0.5f) {
                int stun = blnd / 2;
                if (blinding.thrownBy == self) {
                    stun /= 2;
                }
                self.Stun(stun);
            }

            blnd += (int)(blnd * blinding.Temperature());
        }

        orig(self, blnd);
    }

    // Spore plant/beehive

    private static void PuffBall_Update(On.PuffBall.orig_Update orig, PuffBall self, bool eu)
    {
        orig(self, eu);

        if (self.Temperature() > 0.49f) {
            self.Explode();
        }
    }

    private static void PuffBall_DrawSprites(On.PuffBall.orig_DrawSprites orig, PuffBall self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        int l = self.dots.Length;
        for (int i = 0; i < l; i++) {
            sLeaser.sprites[3 + i].color = Color.Lerp(sLeaser.sprites[3 + i].color, LavaColor.rgb, self.Temperature() / 0.4f);
            sLeaser.sprites[3 + i + l].color = Color.Lerp(sLeaser.sprites[3 + i + l].color, LavaColor.rgb, self.Temperature() / 0.4f);
        }
    }

    private static void SporePlant_Update(On.SporePlant.orig_Update orig, SporePlant nest, bool eu)
    {
        orig(nest, eu);

        float temp = nest.Temperature();

        if (temp > 0) {
            nest.angry = Mathf.Clamp01(nest.angry - temp);
        }
        if (temp > 0.2f) {
            nest.Pacified = true;

            if (RngChance(temp * temp)) {
                var pos = nest.firstChunk.pos + nest.firstChunk.rad * Random.insideUnitCircle * 0.5f;
                var left = Custom.RotateAroundOrigo(nest.rotation, -1 * Rng(80, 100));
                var right = Custom.RotateAroundOrigo(nest.rotation, 1 * Rng(80, 100));

                nest.WispySmoke(0).Emit(pos, left * Rng(1, 3 * temp), LavaColor.rgb);
                nest.WispySmoke(1).Emit(pos, right * Rng(1, 3 * temp), LavaColor.rgb);
            }
        }
        if (temp > 0.6f && RngChance(0.05f)) {
            nest.room.PlaySound(SoundID.Firecracker_Burn, nest.firstChunk.pos, 0.15f, 1.4f);
        }
        if (temp > 0.7f) {
            nest.ReleaseBees();
        }
    }

    // Pearls

    private static void DataPearl_ApplyPalette(On.DataPearl.orig_ApplyPalette orig, DataPearl self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
    {
        orig(self, sLeaser, rCam, palette);

        if (self.AbstractPearl.dataPearlType == BurntPearl) {
            self.color = DataPearl.UniquePearlMainColor(BurntPearl);
            self.highlightColor = DataPearl.UniquePearlHighLightColor(BurntPearl);
        }
    }

    private static Color DataPearl_UniquePearlMainColor(On.DataPearl.orig_UniquePearlMainColor orig, DataPearl.AbstractDataPearl.DataPearlType pearlType)
    {
        return pearlType == BurntPearl ? new(0.2f, 0.2f, 0.2f) : orig(pearlType);
    }

    private static Color? DataPearl_UniquePearlHighLightColor(On.DataPearl.orig_UniquePearlHighLightColor orig, DataPearl.AbstractDataPearl.DataPearlType pearlType)
    {
        return pearlType == BurntPearl ? new(0.2f, 0.2f, 0.2f) : orig(pearlType);
    }

    private static void DataPearl_DrawSprites(On.DataPearl.orig_DrawSprites orig, DataPearl self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        if (self.AbstractPearl.dataPearlType == BurntPearl) {
            self.glimmer = self.lastGlimmer = 0;
        }

        orig(self, sLeaser, rCam, timeStacker, camPos);

        float percent = self.Temperature() / 0.6f;

        Color color = (LavaColor with { lightness = 0.9f }).rgb;

        sLeaser.sprites[0].color = Color.Lerp(sLeaser.sprites[0].color, color, percent);
        sLeaser.sprites[1].color = Color.Lerp(sLeaser.sprites[1].color, color, percent);
        sLeaser.sprites[2].color = Color.Lerp(sLeaser.sprites[2].color, Color.white, percent);

        if (self.AbstractPearl.dataPearlType != BurntPearl && self.Temperature() > 0.7f) {
            // Erase pearl data if too hot
            self.AbstractPearl.dataPearlType = BurntPearl;
            self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
        }
    }

    // Explosives

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

    // Burnables

    private static void SeedCob_Update(On.SeedCob.orig_Update orig, SeedCob cob, bool eu)
    {
        orig(cob, eu);

        // Burn seed cobs for a large amount of heat
        if (!cob.AbstractCob.dead && cob.open > 0.95f) {
            foreach (var player in cob.room.game.Players) {
                if (player.realizedObject is Player p && p.IsLavaCat() && p.room == cob.room) {
                    Vector2 closest = Custom.ClosestPointOnLineSegment(cob.bodyChunks[0].pos, cob.bodyChunks[1].pos, p.firstChunk.pos);
                    int freeHand = p.FreeHand();
                    if (freeHand != -1 && (closest - p.firstChunk.pos).MagnitudeLt(25)) {
                        p.handOnExternalFoodSource = closest;

                        p.Blink(5);
                        p.BlindTimer() = 10;

                        p.WispySmoke(freeHand).Emit(p.Hand(freeHand).pos, new Vector2(0, 0.5f), LavaColor.rgb);

                        // Show food bar for food items
                        Equalize(p, cob, 0.1f);

                        cob.Temperature() += 1 / 5 / 40f;
                        break;
                    }
                }
            }
        }

        if (cob.Temperature() > 0.1f) {
            if (RngChance(cob.Temperature())) {
                cob.room.FireSmoke().Emit(cob.firstChunk.pos, new Vector2(0, 0.5f) + Custom.RNV(), LavaColor.rgb, 10);
            }
        }

        if (cob.Temperature() > 0.2f) {
            cob.Temperature() += 0.01f * Mathf.Max(0, 0.8f - cob.Burn());

            // TODO fire and smoke vfx, heat up nearby entities

            // Heat up nearby objects
            foreach (var obj in cob.room.physicalObjects[cob.collisionLayer]) {
                foreach (var chunk in obj.bodyChunks) {
                    float dist = Vector2.Distance(chunk.pos, cob.firstChunk.pos);
                    if (dist < 50) {
                        Equalize(cob, obj, 0.2f * Mathf.InverseLerp(50, 0, dist));
                    }
                }
            }

            // Burning
            if (cob.Burn() < 1f) {
                cob.Burn() += 1 / (40 * 10);

                if (cob.Burn() > 0.5f) {
                    var consumed = cob.room.world.regionState.consumedItems.FirstOrDefault(c => c.placedObjectIndex == cob.AbstractCob.placedObjectIndex);
                    if (consumed != null) {
                        consumed.waitCycles += 15;
                    }
                }
            }
        }
    }

    private static void SeedCob_DrawSprites(On.SeedCob.orig_DrawSprites orig, SeedCob self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        // TODO blacken cob gradually
    }
}
