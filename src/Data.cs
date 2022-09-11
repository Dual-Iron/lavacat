using Smoke;
using UnityEngine;
using WeakTables;

namespace LavaCat;

static class ExtraData
{
    static readonly WeakTable<Player, PlayerData> plrData = new(_ => new PlayerData());
    static readonly WeakTable<AbstractPhysicalObject, ApoData> apoData = new(_ => new ApoData());
    static readonly WeakTable<PhysicalObject, PoData> poData = new(_ => new PoData());
    static readonly WeakTable<PlayerGraphics, PlayerGraphicsData> graphicsData = new(_ => new PlayerGraphicsData());

    public static ref float HeatProgress(this Player p) => ref plrData[p].eatProgress;
    public static ref WeakRef<BombSmoke> Smoke(this Player p) => ref plrData[p].smoke;

    public static ref float Temperature(this AbstractPhysicalObject o) => ref apoData[o].temperature;
    public static ref float Temperature(this PhysicalObject o) => ref apoData[o.abstractPhysicalObject].temperature;
    public static ref float TemperatureChange(this PhysicalObject o) => ref poData[o].temperatureChange;

    public static ref int SteamSound(this PhysicalObject o) => ref poData[o].steamSound;

    public static CatLight[] Lights(this PlayerGraphics g) => graphicsData[g].lights;
}

sealed class PlayerData
{
    public float eatProgress;
    public WeakRef<BombSmoke> smoke = new();
}

sealed class ApoData
{
    public float temperature;
}

sealed class PoData
{
    public int steamSound = 0;
    public float temperatureChange;
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
