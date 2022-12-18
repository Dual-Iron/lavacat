using MonoMod.RuntimeDetour;
using RWCustom;
using System.Linq;
using UnityEngine;
using static LavaCat.Extensions;

namespace LavaCat;

static class ObjectHooks
{
    private static bool FireParticleChance(float temp)
    {
        return RngChance(0.50f * temp * temp);
    }

    private static HSLColor HeatedBlackColor(float minLightness, float temp)
    {
        return new(hue: Mathf.Lerp(0f, LavaColor.hue, temp),
                   saturation: Mathf.Lerp(0.5f, 1f, temp),
                   lightness: Mathf.Lerp(minLightness, 1f, temp * temp)
                   );
    }

    private static void Radiate(PhysicalObject emitter, System.Func<Vector2, Vector2> emitterPoint)
    {
        if (emitter?.room == null) return;

        foreach (var list in emitter.room.physicalObjects)
            foreach (var obj in list) {
                if (obj == emitter) continue;

                foreach (var chunk in obj.bodyChunks) {
                    Vector2 closest = emitterPoint(chunk.pos);
                    float sqDist = (chunk.pos - closest).sqrMagnitude;
                    float speed = 0.25f * Mathf.InverseLerp(50 * 50, 5 * 5, sqDist);

                    emitter.EqualizeHeat(obj, speed);

                    if (speed > 0 && obj is Player p && p.IsLavaCat()) {
                        p.SessionRecord?.AddEat(emitter);
                    }
                }
            }
    }

    public static void Apply()
    {
        // Make DLLs explode
        // Make pole plants and kelp monsters burn
        On.Fly.Update += Fly_Update;

        On.Spear.HitSomething += Spear_HitSomething;
        On.Spear.Update += Spear_Update;
        On.Spear.DrawSprites += Spear_DrawSprites;

        On.Rock.Update += Rock_Update;
        On.Rock.DrawSprites += Rock_DrawSprites;

        On.Lantern.Update += Lantern_Update;
        On.Lantern.DrawSprites += Lantern_DrawSprites;

        new Hook(typeof(FlareBomb).GetProperty("LightIntensity").GetGetMethod(), GetLightIntensity);
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

        On.WormGrass.Worm.Attached += Worm_Attached;

        On.Spider.Update += Spider_Update;

        On.VultureGrub.Update += VultureGrub_Update;

        On.SeedCob.Update += SeedCob_Update;
        On.SeedCob.DrawSprites += SeedCob_DrawSprites;

        On.BigSpider.Update += BigSpider_Update;
        On.BigSpiderGraphics.DrawSprites += BigSpiderGraphics_DrawSprites;

        On.DropBug.Update += DropBug_Update;
        On.DropBugGraphics.DrawSprites += DropBugGraphics_DrawSprites;

        On.Scavenger.Update += Scavenger_Update;
        On.ScavengerGraphics.DrawSprites += ScavengerGraphics_DrawSprites;

        On.Cicada.Update += Cicada_Update;
        On.CicadaGraphics.DrawSprites += CicadaGraphics_DrawSprites;
    }

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

        ref float temp = ref spear.Temperature();

        if (spear.stuckInWall != null) {
            temp *= 0.9f;
        }

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

    static float GetLightIntensity(System.Func<FlareBomb, float> orig, FlareBomb self)
    {
        if (self.Temperature() > 0.5f) {
            return orig(self) * (self.Temperature() / 0.5f);
        }
        return orig(self);
    }

    static FlareBomb flare;
    private static void FlareBomb_Update(On.FlareBomb.orig_Update orig, FlareBomb self, bool eu)
    {
        float temp = self.Temperature();

        self.color = temp > 0.5f
            ? Color.Lerp(LavaColor.rgb, Color.white, ((temp - 0.5f) / 0.5f).Pow(2))
            : Color.Lerp(new Color(0.2f, 0, 1), LavaColor.rgb, temp / 0.5f);

        flare = self;
        try { orig(self, eu); }
        finally { flare = null; }
    }

    private static void FlareBomb_DrawSprites(On.FlareBomb.orig_DrawSprites orig, FlareBomb self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
    }

    private static void Creature_Blind(On.Creature.orig_Blind orig, Creature self, int blnd)
    {
        if (flare != null) {
            if (flare.Temperature() > 0.5f) {
                int stun = blnd / 2;
                if (flare.thrownBy == self) {
                    stun /= 4;
                }
                self.Stun(stun);
            }
            blnd += (int)(blnd * flare.Temperature());
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
        if (temp > 0.2f && RngChance(temp * temp)) {
            var pos = nest.firstChunk.pos + nest.firstChunk.rad * Random.insideUnitCircle * 0.5f;
            var left = Custom.RotateAroundOrigo(nest.rotation, -1 * Rng(80, 100));
            var right = Custom.RotateAroundOrigo(nest.rotation, 1 * Rng(80, 100));

            nest.WispySmoke(0).Emit(pos, left * Rng(1, 3 * temp), LavaColor.rgb);
            nest.WispySmoke(1).Emit(pos, right * Rng(1, 3 * temp), LavaColor.rgb);
        }
        if (temp > 0.6f && RngChance(0.05f)) {
            nest.Pacified = true;
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
            Plugin.Logger.LogInfo("Burnt pearl! " + self.abstractPhysicalObject);
        }
    }

    // Explosives

    private static void FirecrackerPlant_Update(On.FirecrackerPlant.orig_Update orig, FirecrackerPlant self, bool eu)
    {
        orig(self, eu);

        if (self.fuseCounter == 0 && self.Temperature() > 0.25f) {
            self.Ignite();
            self.fuseCounter += 20;
        }
    }

    private static void ExplosiveSpear_Update(On.ExplosiveSpear.orig_Update orig, ExplosiveSpear self, bool eu)
    {
        orig(self, eu);

        if (self.igniteCounter == 0 && self.Temperature() > 0.25f) {
            self.Ignite();
            self.explodeAt += 20;
        }
    }

    private static void ScavengerBomb_Update(On.ScavengerBomb.orig_Update orig, ScavengerBomb self, bool eu)
    {
        orig(self, eu);

        if (self.burn == 0 && self.Temperature() > 0.25f) {
            self.burn = Rng(0.5f, 1f);
            self.room.PlaySound(SoundID.Fire_Spear_Ignite, self.firstChunk, false, 0.5f, 1.4f);
        }
    }

    // Wormgrass

    private static void Worm_Attached(On.WormGrass.Worm.orig_Attached orig, UpdatableAndDeletable self)
    {
        orig(self);

        if (self is not WormGrass.Worm worm || worm.attachedChunk?.owner == null) {
            return;
        }

        if (RngChance(worm.attachedChunk.owner.Temperature() * 0.2f)) {
            worm.attachedChunk.owner.Temperature() *= 0.995f;

            worm.vel *= -0.5f;
            worm.excitement = 0f;
            worm.focusCreature = null;
            worm.dragForce = 0f;
            worm.attachedChunk = null;
        }
    }

    // Smol spiders

    private static void Spider_Update(On.Spider.orig_Update orig, Spider self, bool eu)
    {
        orig(self, eu);

        if (RngChance(self.Temperature() * 2)) {
            self.room.AddObject(new LavaFireSprite(self.firstChunk.pos + Random.insideUnitCircle * self.firstChunk.rad * 1.15f, true));
        }

        if (self.Temperature() > 0.1f) {
            self.Die();
        }
        if (self.Temperature() > 0.15f) {
            self.deathSpasms = 0f;
        }
        if (self.Temperature() > 0.2f) {
            self.Temperature() = 1f;

            Radiate(self, v => self.firstChunk.pos);

            self.BurstIntoFlame(2);
            self.abstractPhysicalObject.LoseAllStuckObjects();
            self.Destroy();
        }
    }

    // Grub

    private static void VultureGrub_Update(On.VultureGrub.orig_Update orig, VultureGrub self, bool eu)
    {
        orig(self, eu);

        if (self.Temperature() > 0.05f) {
            self.InitiateSignalCountDown();
        }

        if (self.Temperature() > 0.25f) {
            self.Die();
        }
    }

    // Burnables

    private static void SeedCob_Update(On.SeedCob.orig_Update orig, SeedCob cob, bool eu)
    {
        ref float temp = ref cob.Temperature();

        bool burnt = cob.SeedBurns().Any(seedBurn => seedBurn > 0);
        bool dead = cob.AbstractCob.dead;

        // Set `open` to prevent it from decreasing after burning;
        // set `dead` to prevent other players from eating it once burnt
        float open = cob.open;
        cob.AbstractCob.dead |= burnt;

        orig(cob, eu);

        cob.AbstractCob.dead = dead;
        if (cob.open < open)
            cob.open = open;

        // Curl shell extra when burnt
        if (cob.AbstractCob.opened) {
            float destOpen = temp * 1.35f;

            if (cob.open < destOpen)
                cob.open = Mathf.Lerp(cob.open, destOpen, Mathf.Lerp(0.01f, 0.0001f, cob.open / destOpen));
        }

        // Open when too hot
        if (temp > 0.20f) {
            cob.Open();
        }

        // Smoky!
        if (temp > 0.1f) {
            cob.WispySmoke(0).Emit(cob.firstChunk.pos, new Vector2(0, 1), Color.black);
        }

        // Let lavacat players burn cobs
        if (!burnt && cob.Temperature() < 0.45f) {
            foreach (var player in cob.room.game.Players) {
                if (player.realizedObject is not Player p || p.room != cob.room || p.stun > 0 || p.FreeHand() == -1 || !p.IsLavaCat()) {
                    continue;
                }

                Vector2 closest = Custom.ClosestPointOnLineSegment(cob.bodyChunks[0].pos, cob.bodyChunks[1].pos, p.firstChunk.pos);
                if ((closest - p.firstChunk.pos).MagnitudeGt(22)) {
                    continue;
                }

                p.handOnExternalFoodSource = closest;
                p.eatExternalFoodSourceCounter = 30;

                p.Blink(5);
                p.BlindTimer() = 10;

                if (p.room.game.cameras[0].hud.foodMeter != null) {
                    p.room.game.cameras[0].hud.foodMeter.visibleCounter = 200;
                }

                // Heat up cob, but don't equalize, or that'd generate heat from nothing
                int ticks = 800;
                if (cob.AbstractCob.opened) ticks -= 200;
                if (cob.AbstractCob.dead) ticks -= 100;

                temp += 1f / ticks;
            }
        }

        BurnCob(cob, burnt, ref temp);
    }

    private static void BurnCob(SeedCob cob, bool burnt, ref float temp)
    {
        // TODO: satisfying fire crackle noises
        var burns = cob.SeedBurns();

        // Heat up nearby objects while hot
        if (temp > 0.1f && burnt) {
            Radiate(cob, chunkPos => Custom.ClosestPointOnLineSegment(cob.bodyChunks[0].pos, cob.bodyChunks[1].pos, chunkPos));
        }

        // Randomly ignite seeds while hot
        if (RngChance((temp - 0.2f) * 0.06f)) {
            cob.SeedBurns()[RngInt(0, cob.seedPositions.Length)] += 0.01f;
        }

        // Burn seeds
        for (int i = 0; i < cob.seedPositions.Length; i++) {
            ref float burn = ref cob.SeedBurns()[i];

            if (burn == 0 || burn >= 1f) {
                continue;
            }

            // If dead, this cob should burn extremely briefly
            burn += cob.AbstractCob.dead ? 1 / 80f : 1 / 400f;

            if (burn > 1f) {
                burn = 1f;

                Vector2 rot = 0.35f * (Custom.PerpendicularVector(cob.bodyChunks[0].pos, cob.bodyChunks[1].pos) * cob.seedPositions[i].x + Random.insideUnitCircle).normalized;
                cob.bodyChunks[0].vel += rot * cob.seedPositions[i].y;
                cob.bodyChunks[0].pos += rot * cob.seedPositions[i].y;
                cob.bodyChunks[1].vel += rot * (1f - cob.seedPositions[i].y);
                cob.bodyChunks[1].pos += rot * (1f - cob.seedPositions[i].y);
            }

            // Heat up self and nearby seeds using quadratic from 0..1 that peaks at 0.5
            float heat = 1 - (2 * burn - 1).Pow(2);

            temp += heat / 20f / cob.seedPositions.Length;

            if (RngChance(heat * heat * 0.2f)) {
                var closest = cob.seedPositions.Enumerate()
                                               .Where(seed => burns[seed.Index] == 0)
                                               .OrderByDescending(seed => (cob.SeedWorldPos(i) - cob.SeedWorldPos(seed.Index)).sqrMagnitude)
                                               .FirstOrDefault();
                if (closest != default) {
                    burns[closest.Index] += 0.01f;
                }
            }

            // Vfx
            if (RngChance(heat * 0.2f)) {
                cob.room.AddObject(new LavaFireSprite(cob.SeedWorldCenter(i) + Random.insideUnitCircle * 2, true));
            }
        }
    }

    private static void SeedCob_DrawSprites(On.SeedCob.orig_DrawSprites orig, SeedCob self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        for (int i = 0; i < self.seedPositions.Length; i++) {
            float burn = self.SeedBurns()[i];

            for (int s = 0; s < 3; s++) {
                float add = s == 2 ? 0 : 0.08f * Mathf.Sin(0.2941f * Mathf.PI * (i * self.seedPositions.Length + s));

                Color color = Color.Lerp(a: sLeaser.sprites[self.SeedSprite(i, s)].color,
                                         b: rCam.currentPalette.blackColor,
                                         t: burn * burn + add);

                sLeaser.sprites[self.SeedSprite(i, s)].color = color;
                sLeaser.sprites[self.SeedSprite(i, s)].scale *= Mathf.Lerp(1, 0.85f, burn);
            }
        }

        for (int i = 0; i < 2; i++) {
            var mesh = (TriangleMesh)sLeaser.sprites[self.ShellSprite(i)];

            for (int v = 0; v < mesh.verticeColors.Length; v++) {
                Color color = Color.Lerp(a: mesh.verticeColors[v],
                                     b: rCam.currentPalette.blackColor,
                                     t: self.open - 1f);

                mesh.verticeColors[v] = color;
            }
        }
    }

    private static void BigSpider_Update(On.BigSpider.orig_Update orig, BigSpider bug, bool eu)
    {
        // TODO: un-lazy and emit fire particles only on top of the spider's sprites
        orig(bug, eu);

        if (bug.room == null) return;

        ref float temp = ref bug.Temperature();
        ref float burn = ref bug.Burn();

        if (temp > 0.1f) {
            bug.AI.behavior = BigSpiderAI.Behavior.Flee;
            bug.runSpeed = 1.5f;

            bug.WispySmoke().Emit(bug.firstChunk.pos, new(0, 1), Color.black);

            if (bug.State.health > 0.8f && RngChance(temp * temp)) {
                bug.room.PlaySound(SoundID.Big_Spider_Jump_Warning_Rustle, bug.mainBodyChunk, false, 0.8f, 1.25f);
            }
        }
        
        // Burn baby burn
        if (temp > 0.15f) {
            Radiate(bug, _ => bug.mainBodyChunk.pos);

            burn += 1f / 40f / 10f;

            float heat = 1 - (2 * burn - 1).Pow(2);

            temp += heat / 80f;

            if (bug.State.alive) {
                bug.State.health = Mathf.Min(bug.State.health, (1 - burn) * (1 - burn));
                bug.deathConvulsions = 1;
            }

            if (burn > 0.7f) {
                bug.deathConvulsions = 0f;
            }

            int num = (int)Rng(0, heat * 16);
            for (int i = 0; i < num; i++) {
                BodyChunk chunk = bug.RandomChunk;

                bug.room.AddObject(new LavaFireSprite(chunk.pos + Random.insideUnitCircle * chunk.rad, true));
            }
        }
    }

    private static void BigSpiderGraphics_DrawSprites(On.BigSpiderGraphics.orig_DrawSprites orig, BigSpiderGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        float shrivelUp = self.bug.Burn() * 0.8f;

        for (int i = 0; i < self.legs.GetLength(0); i++) {
            for (int j = 0; j < self.legs.GetLength(1); j++) {
                Vector2 connectPos = Vector2.Lerp(self.bug.mainBodyChunk.pos, self.bug.bodyChunks[1].pos, 0.3f);

                self.legs[i, j].pos = Vector2.Lerp(self.legs[i, j].pos, connectPos, shrivelUp);
            }
        }

        orig(self, sLeaser, rCam, timeStacker, camPos);

        foreach (var sprite in sLeaser.sprites) {
            sprite.color = Color.Lerp(sprite.color, rCam.currentPalette.blackColor, self.bug.Burn());
        }
    }

    private static void DropBug_Update(On.DropBug.orig_Update orig, DropBug self, bool eu)
    {
        orig(self, eu);

        if (self.room == null) return;

        ref float temp = ref self.Temperature();
        ref float burn = ref self.Burn();

        if (temp > 0.08f) {
            self.AI.behavior = DropBugAI.Behavior.Flee;

            self.WispySmoke().Emit(self.mainBodyChunk.pos, new(0, 1), Color.black);
        }

        // Burn baby burn
        if (temp > 0.17f && self.State is HealthState state) {
            Radiate(self, _ => self.mainBodyChunk.pos);

            burn += 1f / 40f / 6f;

            float heat = 1 - (2 * burn - 1).Pow(2);

            temp += heat / 80f;

            if (self.State.alive) {
                state.health = Mathf.Min(state.health, (1 - burn).Pow(3));
            }

            int num = (int)Rng(0, heat * 18);
            for (int i = 0; i < num; i++) {
                BodyChunk chunk = self.RandomChunk;

                self.room.AddObject(new LavaFireSprite(chunk.pos + Random.insideUnitCircle * chunk.rad, true));
            }

            if (self.graphicsModule is DropBugGraphics g) {
                g.bodyThickness = Mathf.Lerp(g.bodyThickness, 0.45f, heat * 0.05f);
            }
        }
    }

    private static void DropBugGraphics_DrawSprites(On.DropBugGraphics.orig_DrawSprites orig, DropBugGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        orig(self, sLeaser, rCam, timeStacker, camPos);

        foreach (var sprite in sLeaser.sprites) {
            if (sprite is TriangleMesh mesh && mesh.verticeColors != null) {
                for (int i = 0; i < mesh.verticeColors.Length; i++) {
                    mesh.verticeColors[i] = Color.Lerp(mesh.verticeColors[i], rCam.currentPalette.blackColor, self.bug.Burn());
                }
            }
            else {
                sprite.color = Color.Lerp(sprite.color, rCam.currentPalette.blackColor, self.bug.Burn());
            }
        }
    }

    private static void Scavenger_Update(On.Scavenger.orig_Update orig, Scavenger self, bool eu)
    {
        orig(self, eu);

        if (self.room == null) return;

        ref float temp = ref self.Temperature();
        ref float burn = ref self.Burn();

        if (temp > 0.1f) {
            self.AI.behavior = ScavengerAI.Behavior.Flee;

            self.WispySmoke().Emit(self.mainBodyChunk.pos, new(0, 1), Color.black);
        }

        // Burn baby burn
        if (temp > 0.22f && self.State is HealthState state) {
            Radiate(self, _ => self.mainBodyChunk.pos);

            burn += 1f / 40f / 6f;

            float heat = 1 - (2 * burn - 1).Pow(2);

            temp += heat / 80f;

            if (self.State.alive) {
                state.health = Mathf.Min(state.health, (1 - burn).Pow(3));
                self.AI.scared = 1f;
            }

            int num = (int)Rng(0, heat * 12);
            for (int i = 0; i < num; i++) {
                BodyChunk chunk = self.RandomChunk;

                self.room.AddObject(new LavaFireSprite(chunk.pos + Random.insideUnitCircle * chunk.rad, true));
            }
        }
    }

    private static void ScavengerGraphics_DrawSprites(On.ScavengerGraphics.orig_DrawSprites orig, ScavengerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        orig(self, sLeaser, rCam, timeStacker, camPos);

        foreach (var sprite in sLeaser.sprites) {
            if (sprite is TriangleMesh mesh && mesh.verticeColors != null) {
                for (int i = 0; i < mesh.verticeColors.Length; i++) {
                    mesh.verticeColors[i] = Color.Lerp(mesh.verticeColors[i], rCam.currentPalette.blackColor, self.scavenger.Burn());
                }
            } else {
                sprite.color = Color.Lerp(sprite.color, rCam.currentPalette.blackColor, self.scavenger.Burn());
            }
        }
    }

    private static void Cicada_Update(On.Cicada.orig_Update orig, Cicada self, bool eu)
    {
        orig(self, eu);

        ref float temp = ref self.Temperature();
        ref float burn = ref self.Burn();

        if (temp > 0.25f) {
            self.AI.behavior = CicadaAI.Behavior.Flee;

            self.WispySmoke().Emit(self.mainBodyChunk.pos, new(0, 1), Color.black);
        }

        // Burn baby burn
        if (temp > 0.35f && self.State is HealthState state) {
            Radiate(self, _ => self.mainBodyChunk.pos);

            burn += 1f / 40f / 12f;

            float heat = 1 - (2 * burn - 1).Pow(2);

            temp += heat / 80f;

            if (self.State.alive) {
                state.health = Mathf.Min(state.health, (1 - burn).Pow(3));
            }

            int num = (int)Rng(0, heat * 12);
            for (int i = 0; i < num; i++) {
                BodyChunk chunk = self.RandomChunk;

                self.room.AddObject(new LavaFireSprite(chunk.pos + Random.insideUnitCircle * chunk.rad * 1.2f, true));
            }
        }
    }

    private static void CicadaGraphics_DrawSprites(On.CicadaGraphics.orig_DrawSprites orig, CicadaGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        self.ApplyPalette(sLeaser, rCam, rCam.currentPalette);

        orig(self, sLeaser, rCam, timeStacker, camPos);

        float burn = self.cicada.Burn();

        foreach (var sprite in sLeaser.sprites) {
            if (sprite is TriangleMesh mesh && mesh.verticeColors != null) {
                for (int i = 0; i < mesh.verticeColors.Length; i++) {
                    mesh.verticeColors[i] = Color.Lerp(mesh.verticeColors[i], rCam.currentPalette.blackColor with { a = sprite.color.a }, burn);
                }
            }
            else {
                sprite.color = Color.Lerp(sprite.color, rCam.currentPalette.blackColor with { a = sprite.color.a }, burn);
            }
        }

        for (int i = 0; i < 2; i++) {
            for (int j = 0; j < 2; j++) {
                FSprite sprite = sLeaser.sprites[self.WingSprite(i, j)];
                float alpha = Mathf.Lerp(sprite.color.a, 0, self.cicada.Burn());

                sprite.color = sprite.color with { a = alpha };
            }
        }
    }
}
