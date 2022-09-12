using SlugBase;
using System.Linq;
using UnityEngine;

namespace LavaCat;

static class Extensions
{
    // Fake extensions
    public const DataPearl.AbstractDataPearl.DataPearlType BurntPearl = (DataPearl.AbstractDataPearl.DataPearlType)(-2948);

    // RGB (255, 180, 60)
    public static readonly HSLColor LavaColor = new(0.10f, 1.00f, 0.60f);

    // -- Lava cat --
    public static bool IsLavaCat(this Player player) => Plugin.Character.IsMe(player);

    public static Color SkinColor(this Player player) => player.SkinColor(player.Temperature());
    public static Color SkinColor(this Player player, float temp)
    {
        Color gray = Color.gray;

        if (player.Malnourished) {
            gray.r *= 0.75f;
            gray.g *= 0.75f;
            gray.b *= 0.8f;
        }

        return Color.Lerp(PlayerManager.GetSlugcatColor(player), gray, 1 - temp);
    }

    static T Instance<T>(this Room room, System.Func<Room, T> factory) where T : UpdatableAndDeletable
    {
        var instance = room.updateList.OfType<T>().FirstOrDefault();
        if (instance == null) {
            instance = factory(room);
            room.AddObject(instance);
        }
        return instance;
    }

    public static LavaSteam Steam(this Room room) => room.Instance<LavaSteam>(r => new(r));
    public static FireSmokeFixed FireSmoke(this Room room) => room.Instance<FireSmokeFixed>(r => new(r));
    public static WispySmoke WispySmoke(this PhysicalObject o)
    {
        if (o.WispySmokeRef().TryGetTarget(out var smoke) && smoke.room == o.room) {
            return smoke;
        }
        smoke = new(o.room);
        o.WispySmokeRef() = new(smoke);
        return smoke;
    }

    public static void Cool(this PhysicalObject o, float temperatureLoss, float smokeIntensity, Vector2 smokePos, Vector2 smokeVel)
    {
        if (o.Temperature() > 0 && temperatureLoss > 0) {
            o.Temperature() = Mathf.Clamp01(o.Temperature() - temperatureLoss);
            o.TemperatureChange() = Mathf.Min(o.TemperatureChange(), 0);

            o.room.Steam().Emit(smokePos, smokeVel, smokeIntensity);
            o.SteamSound() = 7;
        }
    }

    public static void BurstIntoFlame(this PhysicalObject o)
    {
        for (int i = 0; i < 10 + o.firstChunk.rad; i++) {
            LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.8f, foreground: RngChance(0.5f));
            particle.vel.x *= 1.5f;
            particle.vel.y *= 2f;
            particle.lifeTime += 80;
            o.room.AddObject(particle);
        }
    }

    // -- Math --

    public static float Rng(int from, int to) => Random.Range(from, to);
    public static float Rng(float from, float to) => Mathf.Lerp(from, to, Random.value * 0.999999f);
    public static bool RngChance(float percent) => Random.value < percent;
    public static Vector2 RngUnitVec() => RWCustom.Custom.RNV();
    public static T RandomElement<T>(this T[] array) => array[Random.Range(0, array.Length)];

    public static Vector2 RandomPositionInChunk(this BodyChunk chunk, float distanceMultiplier = 1f)
    {
        return chunk.pos + Random.insideUnitCircle * chunk.rad * distanceMultiplier;
    }

    public static bool MagnitudeLt(this Vector2 vec, float operand) => vec.sqrMagnitude < operand * operand;
    public static bool MagnitudeGt(this Vector2 vec, float operand) => vec.sqrMagnitude > operand * operand;

    // Util

    public static HSLColor HSL(this Color color)
    {
        var hsl = RXColor.HSLFromColor(color);

        return new HSLColor(hsl.h, hsl.s, hsl.l);
    }

    public static PlayerGraphics Graf(this Player p) => (PlayerGraphics)p.graphicsModule;
    public static SlugcatHand Hand(this Player p, Creature.Grasp grasp) => p.Graf().hands[grasp.graspUsed];
    public static SlugcatHand Hand(this Player p, int grasp) => p.Graf().hands[grasp];

    public static void ReduceFood(this Player player, bool allowMalnourishment = true)
    {
        var foodHud = player.room.game.cameras[0].hud.foodMeter;
        if (foodHud == null) {
            return;
        }

        foodHud.refuseCounter = 100;

        if (player.playerState.quarterFoodPoints > 0) {
            player.playerState.quarterFoodPoints = 0;
            foodHud.quarterPipShower.Reset();
        }
        else if (player.playerState.foodInStomach > 0) {
            player.playerState.foodInStomach--;
            foodHud.showCount--;

            var pip = foodHud.circles[player.playerState.foodInStomach];
            pip.eatCounter = 50;
            pip.eaten = true;
            pip.rads[0, 0] = pip.circles[0].snapRad + 1.5f;
            pip.rads[0, 1] += 0.6f;
        }
        else if (!player.Malnourished && allowMalnourishment) {
            player.slugcatStats.malnourished = true;
            player.SetMalnourished(true);
            foodHud.survivalLimit = player.slugcatStats.maxFood;
        }
    }
}
