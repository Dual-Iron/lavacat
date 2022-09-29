using BepInEx;
using BepInEx.Logging;
using SlugBase;
using System;
using System.IO;
using System.Linq;

namespace LavaCat;

[BepInPlugin("org.ozqlis.lavacat", nameof(LavaCat), "0.1.0")]
sealed class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger { get; private set; }
    public static LavaCatCharacter Character { get; private set; }
    public static FAtlas Atlas { get; private set; }

    public void OnEnable()
    {
        Logger = base.Logger;
        Character = new LavaCatCharacter();
        try {
            PlayerManager.RegisterCharacter(Character);

            On.RainWorld.Start += RainWorld_Start;

            MenuHooks.Apply();
            CatGraphicsHooks.Apply();
            PlayerHooks.Apply();
            HeatHooks.Apply();
            ObjectHooks.Apply();
            OracleHooks.Apply();
        }
        catch (Exception e) {
            Logger.LogError(e);
        }
    }

    private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
    {
        orig(self);

        Atlas = LoadAtlas();
    }

    static FAtlas LoadAtlas()
    {
        using Stream texture = typeof(Plugin).Assembly.GetManifestResourceStream("LavaCat.png");
        using Stream slicerData = typeof(Plugin).Assembly.GetManifestResourceStream("LavaCat.json");

        if (texture == null || slicerData == null) {
            throw new InvalidOperationException("LavaCat atlas couldn't be found!");
        }

        return CustomAtlasLoader.LoadCustomAtlas("LavaCat", texture, slicerData);
    }
}
