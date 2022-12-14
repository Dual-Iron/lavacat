using Mono.Cecil;
using RWCustom;
using SlugBase;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LavaCat;

static class Extensions
{
    // Fake extensions
    public const DataPearl.AbstractDataPearl.DataPearlType BurntPearl = (DataPearl.AbstractDataPearl.DataPearlType)(-2948);

    // RGB (255, 180, 60)
    public static readonly HSLColor LavaColor = new(0.10f, 1.00f, 0.60f);

    // -- API --

    public static bool ImmuneToHeat(this Creature c)
    {
        // default: false
        return c is Player p && p.IsLavaCat();
    }

    public static bool GeneratesHeat(this PhysicalObject o)
    {
        // default: false
        return o is Player p && !p.dead && p.IsLavaCat();
    }

    public static bool RetainsHeat(this AbstractPhysicalObject.AbstractObjectStick stick)
    {
        // default:
        return stick is AbstractPhysicalObject.CreatureGripStick or Player.AbstractOnBackStick && stick.A.realizedObject.GeneratesHeat();
    }
    
    public static bool RetainsHeat(this PhysicalObject o)
    {
        // util
        if (o.GeneratesHeat()) {
            return true;
        }

        foreach (var stick in o.abstractPhysicalObject.stuckObjects) {
            if (stick.RetainsHeat()) {
                return true;
            }
        }

        return false;
    }

    // -- Lava cat --

    public static bool IsLavaCat(this Player player) => Plugin.Character.IsMe(player);

    public static Color SkinColor(this Player player) => player.SkinColor(player.Temperature());
    public static Color SkinColor(this Player player, float temp)
    {
        HSLColor target = PlayerManager.GetSlugcatColor(player).HSL();

        float h = Mathf.Lerp(target.hue - 0.05f, target.hue, temp);
        float s = Mathf.Lerp(0.05f, 1, temp.Sqrt());
        float l = Mathf.Lerp(player.Malnourished ? 0.22f : 0.3f, target.lightness, temp.Pow(2));

        return new HSLColor(h, s, l).rgb;
    }

    public static float FoodHeat(this PhysicalObject o)
    {
        return o.HeatProperties().IsEdible ? o.TotalMass + 0.005f : 0;
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
    public static WispySmoke WispySmoke(this PhysicalObject o, int i = 0)
    {
        if (o.WispySmokeRef(i).TryGetTarget(out var smoke) && smoke.room == o.room) {
            return smoke;
        }
        o.room.AddObject(smoke = new(o.room));
        o.WispySmokeRef(i) = new(smoke);
        return smoke;
    }

    public static void Cool(this BodyChunk c, float temperatureLoss, float smokeIntensity, Vector2 smokePos, Vector2 smokeVel)
    {
        PhysicalObject o = c.owner;
        if (o.Temperature() > 0 && temperatureLoss > 0) {
            // 0.35 is chosen because that's one slugcat chunk
            float reduction = c.mass / 0.35f;

            o.Temperature() = Mathf.Clamp01(o.Temperature() - temperatureLoss / reduction);
            o.TemperatureChange() = Mathf.Min(o.TemperatureChange(), 0);

            o.room.Steam().Emit(smokePos, smokeVel, smokeIntensity);
            o.SteamSound() = 7;
        }
    }

    public static HeatProperties HeatProperties(this PhysicalObject self)
    {
        static HeatProperties Inedible(float conductivity) => new() { Conductivity = conductivity };
        static HeatProperties Edible(float conductivity, float eatSpeed) => new() { Conductivity = conductivity, EatSpeed = eatSpeed };

        return self switch {
            IPlayerEdible or WaterNut or FlyLure or BubbleGrass => Edible(0.2f, 1f),
            Spider => new HeatProperties { Conductivity = 0.15f, DryTemp = 0 },

            Spear => Inedible(0.50f),
            Rock => Inedible(0.05f),
            Player => Inedible(0.02f),
            DataPearl => Inedible(0.05f),
            SeedCob => Inedible(0.05f),

            Cicada => new HeatProperties { Conductivity = 0.15f, DryTemp = 0.4f },
            DropBug => new HeatProperties { Conductivity = 0.09f, DryTemp = 0.25f },
            Scavenger => new HeatProperties { Conductivity = 0.09f, DryTemp = 0.25f },
            BigSpider => new HeatProperties { Conductivity = 0.11f, DryTemp = 0.25f },
            Creature => Inedible(0.07f),

            _ => Inedible(0.025f),
        };
    }

    public static void EqualizeHeat(this PhysicalObject self, PhysicalObject other, float speed = 0.05f, bool drainHeat = false)
    {
        if (speed <= 0 || self == other) return;

        // Lighter objects should lose heat faster than heavier objects.
        float massRatio = other.TotalMass / (other.TotalMass + self.TotalMass);
        float conductivity = HeatProperties(self).Conductivity;
        float heatFlow = other.Temperature() - self.Temperature();

        bool canSelfCool = !self.GeneratesHeat() || drainHeat;
        bool canOtherCool = !other.GeneratesHeat() || drainHeat;

        if (canSelfCool || heatFlow > 0)
            self.Temperature() += heatFlow * conductivity * speed * massRatio;

        if (canOtherCool || heatFlow < 0)
            other.Temperature() -= heatFlow * conductivity * speed * (1 - massRatio);
    }


    public static void BurstIntoFlame(this PhysicalObject o, float intensity = 1f)
    {
        for (int i = 0; i < 10 + o.firstChunk.rad * intensity; i++) {
            LavaFireSprite particle = new(o.firstChunk.pos + Random.insideUnitCircle * o.firstChunk.rad * 0.8f, foreground: RngChance(0.5f));
            particle.vel.x *= 1.5f;
            particle.vel.y *= 2f;
            particle.lifeTime += 20;
            o.room.AddObject(particle);
        }
    }

    // -- Math --

    public static int RngInt(int from, int to) => Random.Range(from, to);
    public static float Rng(float from, float to) => Mathf.Lerp(from, to, Random.value * 0.999999f);
    public static bool RngChance(float percent) => Random.value < percent;

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

    public static Vector2 SeedWorldPos(this SeedCob cob, int seed)
    {
        // 0=root, 1=tip
        Vector2 root = cob.bodyChunks[0].pos;
        Vector2 tip = cob.bodyChunks[1].pos;
        Vector2 toRoot = (root - tip).normalized;
        Vector2 perpToRoot = Custom.PerpendicularVector(toRoot);

        return tip + toRoot * cob.seedPositions[seed].y * (Vector2.Distance(tip, root) - 10f) + perpToRoot * cob.seedPositions[seed].x * 3f;
    }

    public static Vector2 SeedWorldCenter(this SeedCob cob, int seed)
    {
        // 0=root, 1=tip
        Vector2 pos = cob.SeedWorldPos(seed);

        Vector2 root = cob.bodyChunks[0].pos;
        Vector2 tip = cob.bodyChunks[1].pos;
        Vector2 toRoot = (root - tip).normalized;
        Vector2 perpToRoot = Custom.PerpendicularVector(toRoot);

        float offset = 1f + Mathf.Sin(Mathf.PI * seed / cob.seedPositions.Length - 1);
        float offsetDir = Mathf.Sign(cob.seedPositions[seed].x);

        return pos + perpToRoot * Mathf.Pow(Mathf.Abs(cob.seedPositions[seed].x), Custom.LerpMap(offset, 1f, 2f, 1f, 0.5f)) * offsetDir * offset * 3.5f;
    }

    public static T RandomElement<T>(this T[] array) => array[Random.Range(0, array.Length)];

    public static IEnumerable<Indexed<T>> Enumerate<T>(this IEnumerable<T> source)
    {
        int i = -1;
        foreach (var value in source) {
            yield return new(value, ++i);
        }
    }

    static string Debug(this object value, int indent)
    {
        static string IndentString(int i) => new(' ', i * 2);

        if (value is string s) return $"\"{s}\"";
        if (value is IDictionary dict) {
            var sb = new System.Text.StringBuilder("{\n" + IndentString(indent));
            var enumerator = dict.Keys.GetEnumerator();
            if (enumerator.MoveNext()) {
                sb.Append(enumerator.Current.Debug(indent + 1));
                sb.Append(" = ");
                sb.Append(dict[enumerator.Current].Debug(indent + 1));
            }
            while (enumerator.MoveNext()) {
                sb.Append(",\n" + IndentString(indent));
                sb.Append(enumerator.Current.Debug(indent + 1));
                sb.Append(" = ");
                sb.Append(dict[enumerator.Current].Debug(indent + 1));
            }
            sb.Append("\n" + IndentString(indent - 1) + "}");
            return sb.ToString();
        }
        if (value is IList list) {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < list.Count - 1; i++) {
                sb.Append(list[i].Debug(indent + 1));
                sb.Append(", ");
            }
            sb.Append(list[list.Count - 1].Debug(indent + 1));
            sb.Append("]");
            return sb.ToString();
        }
        if (value is IEnumerable enumerable) {
            var sb = new System.Text.StringBuilder("(");

            var enumerator = enumerable.GetEnumerator();
            if (enumerator.MoveNext()) {
                sb.Append(enumerator.Current.Debug(indent + 1));
            }
            while (enumerator.MoveNext()) {
                sb.Append(", ");
                sb.Append(enumerator.Current.Debug(indent + 1));
            }

            sb.Append(")");
            return sb.ToString();
        }
        return value.ToString();
    }

    public static string Debug(this object value)
    {
        return Debug(value, 1);
    }
}

public static class MathExt
{
    public static float Pow(this float f, float pow) => Mathf.Pow(f, pow);
    public static float Sqrt(this float f) => Mathf.Sqrt(f);
    public static float Max(this float f, float other) => Mathf.Max(f, other);
    public static float Min(this float f, float other) => Mathf.Min(f, other);
    public static float Abs(this float f) => Mathf.Abs(f);
    public static float ClampUnit(this float f) => Mathf.Clamp01(f);
    public static float Clamp(this float f, float min, float max) => Mathf.Clamp(f, min, max);
}

public readonly struct Indexed<T> : System.IEquatable<Indexed<T>>
{
    public Indexed(T value, int index)
    {
        this.value = value;
        this.index = index;
        assigned = true;
    }

    private readonly bool assigned;
    private readonly T value;
    private readonly int index;

    public readonly T Value => assigned ? value : throw new System.InvalidOperationException("Null");
    public readonly int Index => assigned ? index : throw new System.InvalidOperationException("Null");

    public override bool Equals(object obj)
    {
        return obj is Indexed<T> indexed && Equals(indexed);
    }

    public bool Equals(Indexed<T> other)
    {
        return assigned == other.assigned &&
               EqualityComparer<T>.Default.Equals(value, other.value) &&
               index == other.index;
    }

    public override int GetHashCode()
    {
        int hashCode = -854830473;
        hashCode = hashCode * -1521134295 + assigned.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<T>.Default.GetHashCode(value);
        hashCode = hashCode * -1521134295 + index.GetHashCode();
        return hashCode;
    }

    public override string ToString()
    {
        return assigned ? $"[{index}] = {value}" : "<default>";
    }

    public static bool operator ==(Indexed<T> left, Indexed<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Indexed<T> left, Indexed<T> right)
    {
        return !(left == right);
    }
}
