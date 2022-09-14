using BepInEx;
using BepInEx.Logging;
using SlugBase;
using System;

namespace LavaCat;

[BepInPlugin("org.ozqlis.lavacat", nameof(LavaCat), "0.1.0")]
sealed class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger { get; private set; }
    public static LavaCatCharacter Character { get; private set; }

    public void OnEnable()
    {
        Logger = base.Logger;
        Character = new LavaCatCharacter();

        PlayerManager.RegisterCharacter(Character);

        try {
            MenuHooks.Apply();
            PlayerHooks.Apply();
            HeatHooks.Apply();
            ObjectHooks.Apply();
            OracleHooks.Apply();
        }
        catch (Exception e) {
            Logger.LogError(e);
        }
    }
}
