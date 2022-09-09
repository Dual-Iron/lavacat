using SlugBase;

namespace LavaCat
{
    // See https://github.com/SlimeCubed/SlugBase/wiki/Developer-Guide
    sealed class LavaCatCharacter : SlugBaseCharacter
    {
        public LavaCatCharacter() : base("lavacat", FormatVersion.V1, 2, true)
        {
        }

        public override string DisplayName => "The Hothead";
        public override string Description => "this cat is too hot! hot damn";
        public override string StartRoom => "SB_S04";
    }
}
