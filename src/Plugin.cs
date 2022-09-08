using BepInEx;
using BepInEx.Logging;

namespace LavaCat
{
    [BepInPlugin("org.ozqlis.lavacat", nameof(LavaCat), "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static new ManualLogSource Logger { get; private set; }

        public void OnEnable()
        {
            Logger = base.Logger;

            On.RainWorld.Start += RainWorld_Start;
        }

        private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
        {
            orig(self);

            Logger.LogInfo("Hello, world!");
        }
    }
}
