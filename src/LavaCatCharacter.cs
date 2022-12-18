using SlugBase;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LavaCat;

sealed class LavaCatCharacter : SlugBaseCharacter
{
    public LavaCatCharacter() : base("lavacat", FormatVersion.V1, useSpawns: 2, multiInstance: true)
    {
    }

    public override string DisplayName => "The Arson";
    public override string Description => 
        "With stone flesh and a fiery core, you must view the environment from a<LINE>" +
        "different lens to survive. The world was not ready for anomalies like you.";

    public override string StartRoom => "SH_S02";
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

    public override CustomSaveState CreateNewSave(PlayerProgression progression)
    {
        var ret = new LavaCatSaveState(progression);
        ret.deathPersistentSaveData.karma = 3;
        ret.food = 4;
        return ret;
    }
}

sealed class LavaCatSaveState : CustomSaveState
{
    public int burntPearls;
    public bool heldBurnable;

    public LavaCatSaveState(PlayerProgression progression) : base(progression, Plugin.Character) { }

    public override void SavePermanent(Dictionary<string, string> data, bool asDeath, bool asQuit)
    {
        data["heldBurnable"] = heldBurnable ? "Y" : "N";
    }

    public override void LoadPermanent(Dictionary<string, string> data)
    {
        heldBurnable = data.TryGetValue("heldBurnable", out string s) && s == "Y";
    }

    public override void Save(Dictionary<string, string> data)
    {
        data["burntPearls"] = ((char)burntPearls).ToString();
    }

    public override void Load(Dictionary<string, string> data)
    {
        burntPearls = data.TryGetValue("burntPearls", out string s) ? s[0] : 0;
    }
}
