using UnityEngine;
using static LavaCat.Extensions;
using static UnityEngine.Mathf;

namespace LavaCat;

static class PlayerHooks
{
    public static void Apply()
    {
        // Fix grabbing food like spiders
        On.Creature.Grab += Creature_Grab1;
        On.Player.Grabability += Player_Grabability;

        // Fix arena scoring
        On.ArenaGameSession.ScoreOfPlayer += ArenaGameSession_ScoreOfPlayer;

        // Prevent the silly super-jump when holding heavy objects
        On.Player.Jump += Player_Jump;
        On.Player.HeavyCarry += Player_HeavyCarry;

        // Sleep in to avoid most of the raindrops at the start of the cycle
        On.RainCycle.ctor += RainCycle_ctor;

        // Make food meter reflect temperature
        On.Player.FoodInRoom_Room_bool += Player_FoodInRoom_Room_bool;
        On.RainWorldGame.ctor += RainWorldGame_ctor;
        On.HUD.FoodMeter.ctor += FoodMeter_ctor;
        On.HUD.FoodMeter.Update += FoodMeter_Update;

        On.Room.AddObject += Room_AddObject;
        On.Player.Update += Player_Update;

        // Reduce water droplet spawns
        On.Creature.TerrainImpact += Creature_TerrainImpact;

        // Fix underwater movement
        On.JellyFish.Update += JellyFish_Update;
        On.Creature.Grab += Creature_Grab;
        On.Player.MovementUpdate += Player_MovementUpdate;
        On.Player.GrabUpdate += Player_GrabUpdate;
        On.Room.FloatWaterLevel += Room_FloatWaterLevel;

        // Graphics
        On.PlayerGraphics.PlayerObjectLooker.HowInterestingIsThisObject += PlayerObjectLooker_HowInterestingIsThisObject;
        On.Player.ShortCutColor += Player_ShortCutColor;
        On.PlayerGraphics.ApplyPalette += PlayerGraphics_ApplyPalette;
        On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
        On.PlayerGraphics.Update += PlayerGraphics_Update;
    }

    private static bool Creature_Grab1(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
    {
        if (self is Player p && p.IsLavaCat() && obj is Scavenger) {
            pacifying = false;
        }
        return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
    }

    private static int Player_Grabability(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
    {
        // TODO fix leeches lagging behind player hand in water
        if (self.IsLavaCat()) return obj switch {
            BigSpider or Scavenger => (int)Player.ObjectGrabability.Drag,
            //Leech => (int)Player.ObjectGrabability.OneHand,
            _ => orig(self, obj),
        };
        return orig(self, obj);
    }

    private static int ArenaGameSession_ScoreOfPlayer(On.ArenaGameSession.orig_ScoreOfPlayer orig, ArenaGameSession self, Player player, bool inHands)
    {
        if (player != null && player.IsLavaCat()) {
            float temperatureTotal = player.Temperature();

            if (inHands) {
                foreach (var grasp in player.grasps) {
                    if (grasp?.grabbed != null) {
                        temperatureTotal += grasp.grabbed.FoodHeat();
                    }
                }
            }

            int foodScoreTotal = (int)(temperatureTotal * 10) * self.arenaSitting.gameTypeSetup.foodScore;

            return orig(self, player, false) + foodScoreTotal;
        }
        return orig(self, player, inHands);
    }

    static bool jumping = false;
    private static void Player_Jump(On.Player.orig_Jump orig, Player self)
    {
        jumping = true;
        try {
            orig(self);
        }
        finally {
            jumping = false;
        }
    }
    private static bool Player_HeavyCarry(On.Player.orig_HeavyCarry orig, Player self, PhysicalObject obj)
    {
        return !jumping && orig(self, obj);
    }

    private static void RainCycle_ctor(On.RainCycle.orig_ctor orig, RainCycle self, World world, float minutes)
    {
        orig(self, world, minutes);

        if (Plugin.Character.IsMe(world.game)) {
            // Sleep in for 2-5 cycle pips
            self.timer += (int)(Rng(1f, 4f) * 30 * 40);
        }
    }

    private static int Player_FoodInRoom_Room_bool(On.Player.orig_FoodInRoom_Room_bool orig, Player self, Room checkRoom, bool eatAndDestroy)
    {
        return self.IsLavaCat() ? self.FoodInStomach : orig(self, checkRoom, eatAndDestroy);
    }

    private static void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager)
    {
        orig(self, manager);

        foreach (var player in self.Players) {
            int food = 0;
            if (self.session is StoryGameSession session) {
                food = session.saveState.food;
            }
            else if (player?.state is PlayerState state) {
                food = state.foodInStomach;
            }

            player.Temperature() = (float)(food / (float)SlugcatStats.SlugcatFoodMeter(Plugin.Character.SlugcatIndex).x);
        }
    }

    private static void FoodMeter_ctor(On.HUD.FoodMeter.orig_ctor orig, HUD.FoodMeter self, HUD.HUD hud, int maxFood, int survivalLimit)
    {
        orig(self, hud, maxFood, survivalLimit);

        // Bugfix for vanilla.
        self.quarterPipShower ??= new(self);
    }

    private static void FoodMeter_Update(On.HUD.FoodMeter.orig_Update orig, HUD.FoodMeter foodMeter)
    {
        if (foodMeter.hud.owner is Player plr && Plugin.Character.IsMe(plr.abstractPhysicalObject.world.game)) {
            // Prevent food meter from popping up every two seconds
            foodMeter.lastCount = foodMeter.hud.owner.CurrentFood;

            // Set food meter according to temperature
            float percent = Clamp01(plr.Temperature());
            float pips = foodMeter.maxFood * percent;
            int fullPips = FloorToInt(pips);
            int quarterPips = FloorToInt(4 * (pips - fullPips));

            // These get overridden later
            plr.playerState.foodInStomach = fullPips;
            plr.playerState.quarterFoodPoints = quarterPips;
            foodMeter.quarterPipShower.displayQuarterFood = quarterPips;

            while (foodMeter.showCount > fullPips) {
                foodMeter.showCount--;

                var pip = foodMeter.circles[foodMeter.showCount];
                pip.eatCounter = 50;
                pip.eaten = true;
                pip.rads[0, 0] = pip.circles[0].snapRad + 1.5f;
                pip.rads[0, 1] += 0.6f;
            }

            while (foodMeter.showCount < fullPips) {
                var pip = foodMeter.circles[foodMeter.showCount];
                pip.foodPlopped = true;
                pip.rads[0, 0] = pip.circles[0].snapRad + 1.5f;
                pip.rads[0, 1] += 0.6f;

                foodMeter.showCount++;
            } 
        }

        orig(foodMeter);
    }

    // -- Player updates --

    private static void Room_AddObject(On.Room.orig_AddObject orig, Room self, UpdatableAndDeletable obj)
    {
        if (allowWaterDrips || obj is not WaterDrip) {
            orig(self, obj);
        }
    }

    static bool allowWaterDrips = true;
    private static void Player_Update(On.Player.orig_Update orig, Player player, bool eu)
    {
        if (!player.IsLavaCat()) {
            orig(player, eu);
            return;
        }

        allowWaterDrips = false;
        player.Template.canSwim = false;

        try {
            orig(player, eu);
        }
        finally {
            allowWaterDrips = true;
            player.Template.canSwim = true;
        }

        ref float temperature = ref player.Temperature();

        if (player.abstractCreature.world.game.IsStorySession) {
            player.playerState.foodInStomach = FloorToInt(player.MaxFoodInStomach * Clamp01(temperature));
        }

        player.dontEatExternalFoodSourceCounter = 20;

        if (player.eatExternalFoodSourceCounter < 20) {
            player.eatExternalFoodSourceCounter = 20;
            player.handOnExternalFoodSource = null;
        }

        // If too hot, cool down quickly
        if (temperature > 1) {
            temperature = Max(temperature * 0.995f, 1);
        }

        if (player.Malnourished && temperature > 0.9f) {
            player.SetMalnourished(false);
        }

        player.waterFriction = Lerp(0.96f, 0.8f, temperature);
        player.buoyancy = Lerp(0.3f, 1.1f, temperature);

        // self cat doesn't breathe.
        player.airInLungs = 1f;
        player.aerobicLevel = 0f;

        UpdateStats(player, temperature);

        static void UpdateStats(Player player, float temperature)
        {
            var stats = player.slugcatStats;

            stats.loudnessFac = 1.5f;
            stats.lungsFac = 0.01f;

            float malnourishedMultiplier = player.Malnourished ? 0.9f : 1f;

            stats.bodyWeightFac = Lerp(1.3f, 1.5f, temperature) * malnourishedMultiplier;
            stats.generalVisibilityBonus = Lerp(0f, 0.2f, temperature);
            stats.corridorClimbSpeedFac = Lerp(0.9f, 1.2f, temperature) * malnourishedMultiplier;
            stats.poleClimbSpeedFac = Lerp(0.9f, 1.2f, temperature) * malnourishedMultiplier;
            stats.runspeedFac = Lerp(0.95f, 1.25f, temperature) * malnourishedMultiplier;

            float mass = 0.7f * player.slugcatStats.bodyWeightFac;
            player.bodyChunks[0].mass = mass / 2f;
            player.bodyChunks[1].mass = mass / 2f;
        }
    }

    // Balance changes

    private static void Creature_TerrainImpact(On.Creature.orig_TerrainImpact orig, Creature self, int chunk, RWCustom.IntVector2 direction, float speed, bool firstContact)
    {
        if (self is Player p && p.IsLavaCat()) {
            // Prevent dying from big heights
            bool invul = self.room.game.rainWorld.setup.invincibility;
            self.room.game.rainWorld.setup.invincibility = true;

            orig(self, chunk, direction, speed, firstContact);

            self.room.game.rainWorld.setup.invincibility = invul;
        }
        else {
            orig(self, chunk, direction, speed, firstContact);
        }
    }

    // -- Player water physics --

    private static void JellyFish_Update(On.JellyFish.orig_Update orig, JellyFish self, bool eu)
    {
        orig(self, eu);

        // Make jellyfish get pulled by lavacat
        float withdrawn = Lerp(10f, 1f, self.tentaclesWithdrawn);

        for (int i = 0; i < self.tentacles.Length; i++) {
            Vector2[,] tentacle = self.tentacles[i];
            BodyChunk latchedOnto = self.latchOnToBodyChunks[i];
            if (latchedOnto != null && (self.Electric || self.room.PointSubmerged(tentacle[tentacle.GetLength(0) - 1, 0]))) {
                Vector2 diff = self.firstChunk.pos - latchedOnto.pos;

                if (diff.MagnitudeGt(tentacle.GetLength(0) * withdrawn * 1.4f)) {
                    var weight = self.firstChunk.mass / (self.firstChunk.mass + latchedOnto.mass);
                    var pull = diff.normalized * (tentacle.GetLength(0) * withdrawn * 1.4f - diff.magnitude) * (1 - weight);
                    self.firstChunk.pos += pull;
                    self.firstChunk.vel += pull;
                }
            }
        }
    }

    private static bool Creature_Grab(On.Creature.orig_Grab orig, Creature crit, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
    {
        // Prevent leeches from softlocking the game
        if (crit is Leech leech && obj is Player player && player.IsLavaCat()) {
            leech.HeardSnailClick(player.firstChunk.pos);
            leech.firstChunk.vel *= -1.5f;
            return false;
        }
        return orig(crit, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
    }

    private static bool movementUpdate = false;
    private static void Player_MovementUpdate(On.Player.orig_MovementUpdate orig, Player player, bool eu)
    {
        movementUpdate = true;
        try {
            orig(player, eu);
        }
        finally {
            movementUpdate = false;
        }
    }

    private static void Player_GrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
    {
        movementUpdate = false;

        orig(self, eu);
    }

    private static float Room_FloatWaterLevel(On.Room.orig_FloatWaterLevel orig, Room room, float horizontalPos)
    {
        // Ignore water level when performing movement update.
        return movementUpdate ? 0 : orig(room, horizontalPos);
    }

    // -- Player graphics --

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

    private static void PlayerGraphics_ApplyPalette(On.PlayerGraphics.orig_ApplyPalette orig, PlayerGraphics player, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
    {
        orig(player, sLeaser, rCam, palette);

        // Make tail creamy white :)
        if (player.player.IsLavaCat() && sLeaser.sprites[2] is TriangleMesh mesh) {
            mesh.verticeColors = new Color[mesh.vertices.Length];
            mesh.customColor = true;

            for (int i = 0; i < mesh.verticeColors.Length; i++) {
                mesh.verticeColors[i] = Color.Lerp(player.player.SkinColor(), Color.white * (player.player.Temperature() + 0.25f), i / (float)mesh.verticeColors.Length);
            }

            for (int i = 0; i < sLeaser.sprites.Length; i++) {
                if (i != 2 && i != 9 && i != 10 && i != 11) {
                    sLeaser.sprites[i].color = player.player.SkinColor();
                }
            }
        }
    }

    private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics graf, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(graf, sLeaser, rCam, timeStacker, camPos);

        if (graf.player.IsLavaCat()) {
            graf.ApplyPalette(sLeaser, rCam, rCam.currentPalette);
        }
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
}
