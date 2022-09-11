using UnityEngine;

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
