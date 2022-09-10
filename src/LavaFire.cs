using UnityEngine;

namespace LavaCat;

sealed class LavaFireSprite : HolyFire.HolyFireSprite
{
    public LavaFireSprite(Vector2 pos) : base(pos)
    {
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
    {
        base.AddToContainer(sLeaser, rCam, newContatiner);

        // Fire should render behind the player
        sLeaser.sprites[0].MoveToBack();
    }
}
