﻿using BepInEx;
using BepInEx.Logging;
using SlugBase;

namespace LavaCat
{
    [BepInPlugin("org.ozqlis.lavacat", nameof(LavaCat), "0.1.0")]
    sealed class Plugin : BaseUnityPlugin
    {
        public static new ManualLogSource Logger { get; private set; }
        public static LavaCatCharacter Char { get; private set; }

        public void OnEnable()
        {
            Logger = base.Logger;
            Char = new LavaCatCharacter();

            PlayerManager.RegisterCharacter(Char);

            On.Player.Update += Player_Update;
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            // If not lavacat, do nothing.
            if (!Char.IsMe(self)) {
                return;
            }

            // Emit some fire particles from the center of the head, just to verify that the mod is working
            if (self.room != null && UnityEngine.Random.value < 0.2f) {
                self.room.AddObject(new HolyFire.HolyFireSprite(self.firstChunk.pos));
            }
        }
    }
}
