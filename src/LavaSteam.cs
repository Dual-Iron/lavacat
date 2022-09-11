using RWCustom;
using Smoke;
using UnityEngine;
using static UnityEngine.Mathf;

namespace LavaCat;

sealed class LavaSteam : SmokeSystem
{
    public LavaSteam(Room room) : base(SmokeType.Steam, room, 2, 0f)
    {
    }

    public override SmokeSystemParticle CreateParticle()
    {
        return new LavaSteamParticle();
    }

    public void EmitSmoke(Vector2 pos, Vector2 vel, float intensity)
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
