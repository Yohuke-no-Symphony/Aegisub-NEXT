namespace AegiNext.Rendering.Fonts;

[Flags]
internal enum HarfBuzzSubsetFlags : uint
{
    RETAIN_GIDS = 0x00000002,
    NAME_LEGACY = 0x00000008,
    PASSTHROUGH_UNRECOGNIZED = 0x00000020,
    NOTDEF_OUTLINE = 0x00000040,
    GLYPH_NAMES = 0x00000080
}
