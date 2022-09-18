using RWCustom;
using Smoke;
using UnityEngine;
using static UnityEngine.Mathf;
using static LavaCat.Extensions;

namespace LavaCat;

sealed class LavaFireSprite : HolyFire.HolyFireSprite
{
    private readonly bool foreground;

    public LavaFireSprite(Vector2 pos, bool foreground = false) : base(pos)
    {
        this.foreground = foreground;
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
    {
        if (foreground) {
            newContainer ??= rCam.ReturnFContainer("Items");
        }

        base.AddToContainer(sLeaser, rCam, newContainer);

        // Fire should render behind the player
        if (foreground) {
            sLeaser.sprites[0].MoveToFront();
        }
        else {
            sLeaser.sprites[0].MoveToBack();
        }
    }
}

sealed class WispySmoke : MeshSmoke
{
    public WispySmoke(Room room) : base((SmokeType)(-1), room, 6, 6f)
    {
    }

    public Color fireColor;

    public override float ParticleLifeTime => Rng(70f, 120f);

    public override bool ObjectAffectWind(PhysicalObject obj) => true;

    public override SmokeSystemParticle AddParticle(Vector2 emissionPoint, Vector2 emissionForce, float lifeTime)
    {
        if (room.PointSubmerged(emissionPoint)) {
            if (Random.value < 0.1f) {
                room.AddObject(new Bubble(emissionPoint, emissionForce, false, false));
            }
            return null;
        }
        if (base.AddParticle(emissionPoint, emissionForce, lifeTime) is BombSmoke.ThickSmokeSegment seg) {
            seg.stationaryMode = false;
            if (seg.nextParticle == null) {
                seg.startConDist = 5f;
            }
            else {
                seg.startConDist = Vector2.Distance(seg.pos, seg.nextParticle.pos) * 0.8f;
            }
            seg.life = 1f;
            seg.lastLife = seg.life;

            return seg;
        }
        return null;
    }

    public override SmokeSystemParticle CreateParticle()
    {
        return new ThickSmokeSegmentFixed(fireColor);
    }

    public void Emit(Vector2 pos, Vector2 vel, Color fireColor)
    {
        this.fireColor = fireColor;
        AddParticle(pos, vel + Custom.RNV() * Random.value * 0.01f, ParticleLifeTime);
    }

    sealed class ThickSmokeSegmentFixed : BombSmoke.ThickSmokeSegment
    {
        private readonly Color fireColor;

        public ThickSmokeSegmentFixed(Color fireColor)
        {
            this.fireColor = fireColor;
        }

        public override Color MyColor(float timeStacker)
        {
            return Color.Lerp(fireColor, colorB, InverseLerp(2f, 5f, colorCounter + timeStacker)); ;
        }
    }
}

sealed class FireSmokeFixed : SmokeSystem
{
    bool foreground;

    public FireSmokeFixed(Room room) : base(SmokeType.FireSmoke, room, 2, 0f)
    {
    }

    public override SmokeSystemParticle CreateParticle()
    {
        return new FireSmokeParticle() { foreground = foreground };
    }

    public void Emit(Vector2 pos, Vector2 vel, Color effectColor, int colorFadeTime, bool foreground = false)
    {
        this.foreground = foreground;
        if (AddParticle(pos, vel, Lerp(10f, 40f, Random.value)) is FireSmokeParticle particle) {
            particle.effectColor = effectColor;
            particle.colorFadeTime = colorFadeTime;
        }
    }

    sealed class FireSmokeParticle : SpriteSmoke
    {
        public bool foreground;

        public override void Reset(SmokeSystem newOwner, Vector2 pos, Vector2 vel, float lifeTime)
        {
            base.Reset(newOwner, pos, vel, lifeTime);
            col = 0f;
            lastCol = 0f;
            rad = Lerp(28f, 46f, Random.value);
            moveDir = Random.value * 360f;
        }

        public override void Update(bool eu)
        {
            base.Update(eu);
            if (resting) {
                return;
            }
            vel *= 0.7f + 0.3f / Pow(vel.magnitude, 0.5f);
            moveDir += Lerp(-1f, 1f, Random.value) * 50f;
            vel += Custom.DegToVec(moveDir) * 0.6f * Lerp(vel.magnitude, 1f, 0.6f);
            if (room.PointSubmerged(pos)) {
                pos.y = room.FloatWaterLevel(pos.x);
            }
            lastCol = col;
            col += 1f;
            if (room.GetTile(pos).Solid && !room.GetTile(lastPos).Solid) {
                IntVector2? intVector = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, room.GetTilePosition(lastPos), room.GetTilePosition(pos));
                FloatRect floatRect = Custom.RectCollision(pos, lastPos, room.TileRect(intVector.Value).Grow(2f));
                pos = floatRect.GetCorner(FloatRect.CornerLabel.D);
                if (floatRect.GetCorner(FloatRect.CornerLabel.B).x < 0f) {
                    vel.x = Abs(vel.x);
                }
                else if (floatRect.GetCorner(FloatRect.CornerLabel.B).x > 0f) {
                    vel.x = -Abs(vel.x);
                }
                else if (floatRect.GetCorner(FloatRect.CornerLabel.B).y < 0f) {
                    vel.y = Abs(vel.y);
                }
                else if (floatRect.GetCorner(FloatRect.CornerLabel.B).y > 0f) {
                    vel.y = -Abs(vel.y);
                }
            }
        }

        public override float Rad(int type, float useLife, float useStretched, float timeStacker)
        {
            if (type == 0) {
                return Lerp(4f, rad, Pow(1f - useLife, 0.6f) + useStretched);
            }
            if (type != 1) {
                return Lerp(4f, rad, Pow(1f - useLife, 0.6f));
            }
            return 1.5f * Lerp(2f, rad, Pow(1f - useLife, 0.6f));
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            base.InitiateSprites(sLeaser, rCam);
            for (int i = 0; i < 2; i++) {
                sLeaser.sprites[i].shader = room.game.rainWorld.Shaders["FireSmoke"];
            }
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            if (!resting) {
                float num = Lerp(lastLife, life, timeStacker);
                float t = InverseLerp(colorFadeTime, 0.5f, Lerp(lastCol, col, timeStacker));
                sLeaser.sprites[0].color = Color.Lerp(colorA, effectColor, t);
                sLeaser.sprites[1].color = Color.Lerp(colorA, effectColor, t);
                sLeaser.sprites[0].alpha = Pow(num, 0.25f) * (1f - stretched);
                sLeaser.sprites[1].alpha = 0.3f + Pow(Sin(num * 3.1415927f), 0.7f) * 0.65f * (1f - stretched);
            }
        }

        public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
        {
            colorA = Color.Lerp(palette.blackColor, palette.fogColor, 0.2f);
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            if (foreground) {
                newContainer ??= rCam.ReturnFContainer("Items");
            }

            base.AddToContainer(sLeaser, rCam, newContainer);

            if (foreground) {
                foreach (var sprite in sLeaser.sprites) {
                    sprite.MoveToFront();
                }
            }
        }

        public Color effectColor;
        public Color colorA;
        public float col;
        public float lastCol;
        public int colorFadeTime;
        public float moveDir;
    }
}

sealed class LavaSteam : SmokeSystem
{
    public LavaSteam(Room room) : base(SmokeType.Steam, room, 2, 0f)
    {
    }

    public override SmokeSystemParticle CreateParticle()
    {
        return new LavaSteamParticle();
    }

    public void Emit(Vector2 pos, Vector2 vel, float intensity)
    {
        if (AddParticle(pos, vel, Lerp(60f, 180f, Random.value * intensity)) is LavaSteamParticle particle) {
            particle.intensity = intensity;
            particle.rad = Lerp(108f, 286f, Random.value) * Lerp(0.5f, 1f, intensity);
        }
    }

    sealed class LavaSteamParticle : SpriteSmoke
    {
        private float upForce;
        public float moveDir;
        public float intensity;

        public override float ToMidSpeed => 0.4f;

        public override void Reset(SmokeSystem newOwner, Vector2 pos, Vector2 vel, float lifeTime)
        {
            base.Reset(newOwner, pos, vel, lifeTime);
            upForce = Random.value * 100f / lifeTime;
            moveDir = Random.value * 360f;
        }

        public override void Update(bool eu)
        {
            if (nextParticle != null && (pos - nextParticle.pos).MagnitudeGt(50)) {
                nextParticle = null;

                lingerPos = lastLingerPos = pos;
            }

            base.Update(eu);

            if (!resting) {
                moveDir += Lerp(-1f, 1f, Random.value) * 50f;
                vel *= 0.8f;
                vel += Custom.DegToVec(moveDir) * 1.8f * intensity * life;
                vel.y += 2.8f * intensity * upForce;
                if (room.PointSubmerged(pos)) {
                    vel.y += 1.4f * intensity * upForce;
                }
            }
        }

        public override float Rad(int type, float useLife, float useStretched, float timeStacker)
        {
            float val = Pow(Lerp(Sin(useLife * 3.1415927f), 1f - useLife, 0.7f), 0.8f);
            if (type == 0) {
                return Lerp(4f, rad, val + useStretched);
            }
            if (type != 1) {
                return Lerp(4f, rad, val);
            }
            return 1.5f * Lerp(2f, rad, val);
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            base.InitiateSprites(sLeaser, rCam);

            for (int i = 0; i < 2; i++) {
                sLeaser.sprites[i].shader = room.game.rainWorld.Shaders["Steam"];
            }
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            base.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            if (!resting) {
                for (int i = 0; i < 2; i++) {
                    sLeaser.sprites[i].alpha = life;
                }
            }
        }

        public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
        {
            base.ApplyPalette(sLeaser, rCam, palette);

            for (int i = 0; i < 2; i++) {
                sLeaser.sprites[i].color = Color.Lerp(palette.fogColor, new Color(1f, 1f, 1f), Lerp(0.03f, 0.35f, palette.texture.GetPixel(30, 7).r));
            }
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            newContainer = rCam.ReturnFContainer("Water");

            base.AddToContainer(sLeaser, rCam, newContainer);
        }
    }
}
