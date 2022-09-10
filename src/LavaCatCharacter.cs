using SlugBase;
using UnityEngine;

namespace LavaCat;

sealed class LavaCatCharacter : SlugBaseCharacter
{
    // RGB (255, 180, 60)
    public static readonly HSLColor Lava = new(0.10f, 1.00f, 0.60f);

    public LavaCatCharacter() : base("lavacat", FormatVersion.V1, 2, true)
    {
    }

    public override string DisplayName => "The Hothead";
    public override string Description => "this cat is too hot! hot damn";

    public override string StartRoom => "SB_S04";
    public override bool CanSkipTempleGuards => false;
    public override bool GatesPermanentlyUnlock => false;
    public override bool HasGuideOverseer => false;

    public override Color? SlugcatColor(int slugcatCharacter, Color baseColor)
    {
        HSLColor color = Lava;

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
        maxFood = 8;
        foodToSleep = 7;
    }

    public override void GetStats(SlugcatStats stats)
    {
        stats.lungsFac = 0.01f;
        stats.bodyWeightFac = 1.5f;
        stats.loudnessFac = 1.5f;

        // The remaining stats depend on your temperature.
    }
}
