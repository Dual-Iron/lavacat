using BepInEx;
using BepInEx.Logging;
using SlugBase;
using System;

namespace LavaCat;

[BepInPlugin("org.ozqlis.lavacat", nameof(LavaCat), "0.1.0")]
sealed class Plugin : BaseUnityPlugin
{
    // RGB (255, 180, 60)
    public static readonly HSLColor LavaColor = new(0.10f, 1.00f, 0.60f);

    public static new ManualLogSource Logger { get; private set; }
    public static LavaCatCharacter Character { get; private set; }

    public void OnEnable()
    {
        Logger = base.Logger;
        Character = new LavaCatCharacter();

        PlayerManager.RegisterCharacter(Character);

        try {
            PlayerHooks.Apply();
            HeatHooks.Apply();
        }
        catch (Exception e) {
            Logger.LogError(e);
        }
    }
}
