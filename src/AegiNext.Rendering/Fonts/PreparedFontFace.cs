using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Rendering.Fonts;

internal static class PreparedFontFace
{
    private const HarfBuzzSubsetFlags PRESERVATION_FLAGS = HarfBuzzSubsetFlags.RETAIN_GIDS | HarfBuzzSubsetFlags.NAME_LEGACY |
        HarfBuzzSubsetFlags.PASSTHROUGH_UNRECOGNIZED | HarfBuzzSubsetFlags.NOTDEF_OUTLINE | HarfBuzzSubsetFlags.GLYPH_NAMES;

    internal static unsafe ImmutableArray<byte> Create(EmbeddedSubtitleFont font, int collectionIndex,
        ImmutableDictionary<uint, float> coordinates)
    {
        nint blob;
        fixed (byte* data = font.Data.AsSpan())
        {
            blob = HarfBuzzSubsetNativeMethods.CreateBlob(data, checked((uint)font.Data.Length), 0, 0, 0);
        }
        nint face = 0;
        nint input = 0;
        nint prepared = 0;
        nint output = 0;
        try
        {
            if (blob == 0)
            {
                throw new InvalidDataException("The original OpenType font could not be loaded.");
            }
            face = HarfBuzzSubsetNativeMethods.CreateFace(blob, checked((uint)collectionIndex));
            var glyphs = face == 0 ? 0 : HarfBuzzSubsetNativeMethods.GetGlyphCount(face);
            if (glyphs == 0)
            {
                throw new InvalidDataException("The original OpenType face contains no usable glyphs.");
            }
            input = HarfBuzzSubsetNativeMethods.CreateInput();
            if (input == 0)
            {
                throw new InvalidDataException("The OpenType face preparation input could not be allocated.");
            }
            HarfBuzzSubsetNativeMethods.KeepEverything(input);
            HarfBuzzSubsetNativeMethods.SetFlags(input, HarfBuzzSubsetNativeMethods.GetFlags(input) | (uint)PRESERVATION_FLAGS);
            foreach (var coordinate in coordinates)
            {
                if (HarfBuzzSubsetNativeMethods.PinAxis(input, face, coordinate.Key, coordinate.Value) == 0)
                {
                    throw new InvalidDataException("The OpenType named instance axis could not be pinned.");
                }
            }
            prepared = HarfBuzzSubsetNativeMethods.Prepare(face, input);
            if (prepared == 0 || HarfBuzzSubsetNativeMethods.GetGlyphCount(prepared) != glyphs)
            {
                throw new InvalidDataException("The OpenType face could not be prepared without losing glyphs.");
            }
            output = HarfBuzzSubsetNativeMethods.ReferenceBlob(prepared);
            var bytes = HarfBuzzSubsetNativeMethods.GetBlobData(output, out var length);
            if (bytes is null || length is 0 or > FontFamilyBundleValidator.MAXIMUM_FONT_BYTES)
            {
                throw new InvalidDataException("The prepared OpenType face is empty or exceeds the font byte budget.");
            }
            return ImmutableArray.Create(new ReadOnlySpan<byte>(bytes, checked((int)length)));
        }
        finally
        {
            if (output != 0)
            {
                HarfBuzzSubsetNativeMethods.DestroyBlob(output);
            }
            if (prepared != 0)
            {
                HarfBuzzSubsetNativeMethods.DestroyFace(prepared);
            }
            if (input != 0)
            {
                HarfBuzzSubsetNativeMethods.DestroyInput(input);
            }
            if (face != 0)
            {
                HarfBuzzSubsetNativeMethods.DestroyFace(face);
            }
            if (blob != 0)
            {
                HarfBuzzSubsetNativeMethods.DestroyBlob(blob);
            }
        }
    }
}
