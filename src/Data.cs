﻿using System;
using UnityEngine;
using WeakTables;

namespace LavaCat;

static class ExtraData
{
    // Player data
    static readonly WeakTable<Player, PlayerData> plrData = new(_ => new());
    static readonly WeakTable<PlayerGraphics, PlayerGraphicsData> graphicsData = new(_ => new());

    // Burning
    static readonly WeakTable<SeedCob, CobData> cobData = new(_ => new());
    static readonly WeakTable<Creature, BurnData> critData = new(_ => new());

    // Misc
    static readonly WeakTable<AbstractPhysicalObject, ApoData> apoData = new(_ => new());
    static readonly WeakTable<PhysicalObject, PoData> poData = new(_ => new());

    public static ref float HeatProgress(this Player p) => ref plrData[p].eatProgress;
    public static ref int BlindTimer(this Player p) => ref plrData[p].blindTimer;
    public static CatLight[] Lights(this PlayerGraphics g) => graphicsData[g].lights;

    public static float[] SeedBurns(this SeedCob o) => cobData[o].seedBurns ??= new float[o.seedPositions.Length];
    public static ref float Burn(this Creature crit) => ref critData[crit].burn;

    public static ref bool AvoidsHeat(this AbstractCreature c) => ref apoData[c].avoidsHeat;
    public static ref float Temperature(this AbstractPhysicalObject o) => ref apoData[o].temperature;
    public static ref float Temperature(this PhysicalObject o) => ref apoData[o.abstractPhysicalObject].temperature;
    public static ref float TemperatureChange(this PhysicalObject o) => ref poData[o].temperatureChange;
    public static ref int SteamSound(this PhysicalObject o) => ref poData[o].steamSound;

    public static ref WeakRef<WispySmoke> WispySmokeRef(this PhysicalObject o, int i)
    {
        int len = poData[o].smoke.Length;
        if (len <= i) {
            Array.Resize(ref poData[o].smoke, i + 1);

            for (int n = len; n < i + 1; n++) {
                poData[o].smoke[n] = new();
            }
        }
        return ref poData[o].smoke[i];
    }
}

sealed class CobData
{
    public float[] seedBurns;
}

sealed class BurnData
{
    public float burn;
}

sealed class PlayerData
{
    public int blindTimer;
    public float eatProgress;
}

sealed class ApoData
{
    public float temperature;
    public bool avoidsHeat;
}

sealed class PoData
{
    public int steamSound = 0;
    public float temperatureChange;
    public WeakRef<WispySmoke>[] smoke = new WeakRef<WispySmoke>[0];
}

sealed class PlayerGraphicsData
{
    public CatLight[] lights = new CatLight[3];
}

struct CatLight
{
    public WeakRef<LightSource> source;
    public Vector2 offset;
    public Vector2 targetOffset;
    public float targetRad;
}
