using UnityEngine;

namespace LavaCat;

interface IHeatable
{
    bool IsFood { get; }
    float Conductivity { get; }

    void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos);
    void Update(PhysicalObject o);
}

struct Simple : IHeatable
{
    public bool IsFood { get; set; }
    public float Conductivity { get; set; }

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos) { }
    public void Update(PhysicalObject o) { }
}

struct HeatSpear : IHeatable
{
    public bool IsFood => false;
    public float Conductivity => 0.10f;

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos)
    {
        Spear spear = (Spear)o;
        HSLColor spearHsl = spear.color.HSL();

        float temp = o.Temperature();
        float hue = Mathf.Lerp(0f, Plugin.LavaColor.hue, temp);
        float sat = Mathf.Lerp(0.5f, 1f, temp);
        float light = Mathf.Lerp(spearHsl.lightness, 1f, temp * temp);

        sLeaser.sprites[0].color = new HSLColor(hue, sat, light).rgb;
    }
    public void Update(PhysicalObject o)
    {
        if (o.room != null && Extensions.RngChance(0.50f * o.Temperature() * o.Temperature())) {
            const float halfLength = 22;

            LavaFireSprite sprite = new(o.firstChunk.pos + Random.insideUnitCircle * 2 + ((Spear)o).rotation * Extensions.Rng(-halfLength, halfLength));
            sprite.life *= 0.7f;
            o.room.AddObject(sprite);
        }
    }
}

struct HeatRock : IHeatable
{
    public bool IsFood => false;
    public float Conductivity => 0.05f;

    public void DrawSprites(PhysicalObject o, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, Vector2 camPos)
    {
        Rock rock = (Rock)o;
        HSLColor rockHsl = rock.color.HSL();

        float temp = o.Temperature();
        float hue = Mathf.Lerp(0f, Plugin.LavaColor.hue, temp);
        float sat = Mathf.Lerp(0.5f, 1f, temp);
        float light = Mathf.Lerp(rockHsl.lightness, 1f, temp * temp);

        sLeaser.sprites[0].color = new HSLColor(hue, sat, light).rgb;
        sLeaser.sprites[1].color = new HSLColor(hue, sat / 2, light / 2).rgb;
    }
    public void Update(PhysicalObject o)
    {
        if (o.room != null && Extensions.RngChance(0.50f * o.Temperature() * o.Temperature())) {
            o.room.AddObject(new LavaFireSprite(o.firstChunk.pos + Random.insideUnitCircle * 3));
        }
    }
}
