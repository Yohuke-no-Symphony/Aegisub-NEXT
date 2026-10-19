using System.Collections.Immutable;
using AegiNext.Core.Presets;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal sealed class ApplicationFontFaceSource
{
    private readonly Lazy<ImmutableArray<byte>> prepared;

    internal ApplicationFontFaceSource(EmbeddedSubtitleFont file, int collectionIndex,
        ImmutableDictionary<uint, float> coordinates)
    {
        File = file;
        prepared = new(() => PreparedFontFace.Create(file, collectionIndex, coordinates));
    }

    internal EmbeddedSubtitleFont File { get; }

    internal SKTypeface Open()
    {
        using var data = SKData.CreateCopy(prepared.Value.AsSpan());
        return SKTypeface.FromData(data)
            ?? throw new InvalidDataException("The prepared application font could not be opened.");
    }
}
