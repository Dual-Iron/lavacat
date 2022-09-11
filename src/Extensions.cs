﻿using SlugBase;
using System.Linq;
using UnityEngine;

namespace LavaCat;

static class Extensions
{
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

    public static LavaSteam SteamManager(this Room room)
    {
        var instance = room.updateList.OfType<LavaSteam>().FirstOrDefault();
        if (instance == null) {
            instance = new LavaSteam(room);
            room.AddObject(instance);
        }
        return instance;
    }

    // -- Math --

    public static float Rng(int from, int to) => Random.Range(from, to);
    public static float Rng(float from, float to) => Mathf.Lerp(from, to, Random.value * 0.999999f);
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

    // Util

    public static HSLColor HSL(this Color color)
    {
        var hsl = RXColor.HSLFromColor(color);

        return new HSLColor(hsl.h, hsl.s, hsl.l);
    }

    public static PlayerGraphics Graf(this Player p) => (PlayerGraphics)p.graphicsModule;
    public static SlugcatHand Hand(this Player p, Creature.Grasp grasp) => p.Graf().hands[grasp.graspUsed];

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
