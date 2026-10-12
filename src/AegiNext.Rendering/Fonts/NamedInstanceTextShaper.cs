using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace AegiNext.Rendering.Fonts;

internal sealed class NamedInstanceTextShaper : IDisposable
{
    private const int FONT_SIZE_SCALE = 512;
    private readonly Font font;
    private bool isDisposed;

    internal NamedInstanceTextShaper(SKTypeface typeface, int namedInstanceIndex, int collectionIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(namedInstanceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(namedInstanceIndex, 0xFFFF);
        ArgumentOutOfRangeException.ThrowIfNegative(collectionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(collectionIndex, 0xFFFF);
        using var stream = typeface.OpenStream()
            ?? throw new InvalidDataException("The variable font instance data could not be read.");
        using var blob = stream.ToHarfBuzzBlob();
        var faceIndex = (namedInstanceIndex << 16) | collectionIndex;
        using var face = new Face(blob, collectionIndex) { Index = faceIndex, UnitsPerEm = typeface.UnitsPerEm };
        font = new(face);
        try
        {
            font.SetScale(FONT_SIZE_SCALE, FONT_SIZE_SCALE);
            font.SetFunctionsOpenType();
        }
        catch
        {
            font.Dispose();
            throw;
        }
    }

    internal SKShaper.Result Shape(Buffer buffer, SKFont drawingFont)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        font.Shape(buffer);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var glyphs = new uint[infos.Length];
        var clusters = new uint[infos.Length];
        var points = new SKPoint[infos.Length];
        var scaleY = drawingFont.Size / FONT_SIZE_SCALE;
        var scaleX = scaleY * drawingFont.ScaleX;
        var advanceX = 0f;
        var advanceY = 0f;
        for (var index = 0; index < infos.Length; index++)
        {
            glyphs[index] = infos[index].Codepoint;
            clusters[index] = infos[index].Cluster;
            points[index] = new(advanceX + positions[index].XOffset * scaleX,
                advanceY - positions[index].YOffset * scaleY);
            advanceX += positions[index].XAdvance * scaleX;
            advanceY += positions[index].YAdvance * scaleY;
        }
        return new(glyphs, clusters, points, advanceX);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }
        font.Dispose();
        isDisposed = true;
    }
}
