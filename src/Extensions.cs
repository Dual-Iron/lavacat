using SlugBase;
using UnityEngine;

namespace LavaCat;

static class Extensions
{
    // -- Lava cat --
    public static bool IsLavaCat(this Player player) => Plugin.Character.IsMe(player);

    public static Color SkinColor(this Player player) => player.SkinColor(player.Temp());
    public static Color SkinColor(this Player player, float temp)
    {
        return Color.Lerp(PlayerManager.GetSlugcatColor(player), Color.gray, 1 - temp);
    }

    public static ref float Temp(this Player player) => ref Hooks.plrData[player].temperature;
    public static ref float TempLast(this Player player) => ref Hooks.plrData[player].temperatureLast;

    // -- Math --

    public static float Rng(float from, float to) => Mathf.Lerp(from, to, Random.value);
    public static bool RngChance(float percent) => Random.value < percent;
    public static Vector2 RngUnitVec() => RWCustom.Custom.RNV();
    public static T RngElement<T>(this T[] array) => array[Random.Range(0, array.Length)];

    public static Vector2 RandomPositionInChunk(this BodyChunk chunk, float distanceMultiplier = 1f)
    {
        return chunk.pos + Random.insideUnitCircle * chunk.rad * distanceMultiplier;
    }

    public static bool MagnitudeLessThan(this Vector2 vec, float operand)
    {
        return vec.sqrMagnitude < operand * operand;
    }
}
