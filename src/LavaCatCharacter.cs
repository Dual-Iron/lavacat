using SlugBase;
using System.IO;
using UnityEngine;

namespace LavaCat;

sealed class LavaCatCharacter : SlugBaseCharacter
{
    public LavaCatCharacter() : base("lavacat", FormatVersion.V1, 2, true)
    {
    }

    public override string DisplayName => "The Hothead";
    public override string Description => "this cat is too hot! hot damn";

    public override string StartRoom => "SB_S04";
    public override bool CanSkipTempleGuards => false;
    public override bool GatesPermanentlyUnlock => false;
    public override bool HasGuideOverseer => false;

    public override bool CanEatMeat(Player player, Creature creature)
    {
        return false;
    }

    public override Color? SlugcatColor(int slugcatCharacter, Color baseColor)
    {
        HSLColor color = Extensions.LavaColor;

        bool isStoryMode = slugcatCharacter == -1;
        if (!isStoryMode) {
            // Shift hue for different players
            color.hue += 0.2f * slugcatCharacter;
        }

        return color.rgb;
    }

    public override Color? SlugcatEyeColor(int slugcatCharacter)
    {
        return Color.white;
    }

    public override void GetFoodMeter(out int maxFood, out int foodToSleep)
    {
        maxFood = 10;
        foodToSleep = 6;
    }

    public override Stream GetResource(params string[] path)
    {
        string pathFull = string.Join(".", path);

        return typeof(LavaCatCharacter).Assembly.GetManifestResourceStream(pathFull);
    }
}
