using System.Buffers;
using System.Numerics;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace AegiNext.Rendering;

/// <summary>
/// 使用调用方指定的字体塑形单行、单方向、单脚本文字；不做系统字体回退或段落布局。实例限单线程使用。
/// </summary>
public sealed class TextShaper : IDisposable
{
    private readonly SKTypeface typeface;
    private readonly SKShaper? shaper;
    private readonly NamedInstanceTextShaper? namedInstanceShaper;
    private readonly bool synthesizeBold;
    private readonly bool synthesizeItalic;
    private bool isDisposed;

    internal TextShaper(SKTypeface ownedTypeface, bool bold = false, bool italic = false,
        SystemFontFace? face = null, bool explicitVariant = false)
    {
        typeface = ownedTypeface;
        FontFamily = face?.FamilyName ?? typeface.FamilyName;
        ResolvedFontVariant = face?.Variant;
        synthesizeBold = !explicitVariant && bold && typeface.FontWeight < (int)SKFontStyleWeight.Bold;
        synthesizeItalic = italic && typeface.FontSlant == SKFontStyleSlant.Upright;
        try
        {
            if (face is { IsVariable: true })
            {
                namedInstanceShaper = new(typeface, face.NamedInstanceIndex, face.CollectionIndex);
            }
            else
            {
                shaper = new(typeface);
            }
        }
        catch
        {
            typeface.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 复制字体数据并打开指定字体 face；损坏字体会抛出异常，不静默回退到系统字体。
    /// </summary>
    public TextShaper(ReadOnlySpan<byte> fontData, int faceIndex = 0) : this(CreateTypeface(fontData, faceIndex))
    {
    }

    private static SKTypeface CreateTypeface(ReadOnlySpan<byte> fontData, int faceIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        if (fontData.IsEmpty)
        {
            throw new ArgumentException("Font data cannot be empty.", nameof(fontData));
        }

        using var data = SKData.CreateCopy(fontData);
        return SKTypeface.FromData(data, faceIndex)
            ?? throw new ArgumentException("The specified font could not be parsed.", nameof(fontData));
    }

    /// <summary>
    /// 塑形非空单行文本，显式指定像素字号、方向和语言；缺字抛出异常，cluster 使用 UTF-16 下标。
    /// </summary>
    public ShapedTextRun Shape(string text, float fontSize, TextDirection direction, string language, float letterSpacing = 0)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ValidateText(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        RenderValidation.Finite(fontSize, nameof(fontSize));
        RenderValidation.Finite(letterSpacing, nameof(letterSpacing));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        if (!float.IsNormal(fontSize))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), "The font size cannot be a subnormal number that causes shaping scale underflow.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        using var buffer = new Buffer();
        buffer.AddUtf16(text);
        buffer.Direction = direction == TextDirection.LEFT_TO_RIGHT ? Direction.LeftToRight : Direction.RightToLeft;
        buffer.Language = new(language);
        buffer.GuessSegmentProperties();
        using var font = new SKFont(typeface, fontSize);
        font.Edging = SKFontEdging.Antialias;
        font.Hinting = SKFontHinting.None;
        font.Subpixel = true;
        font.Embolden = synthesizeBold;
        font.SkewX = synthesizeItalic ? -0.25f : 0;
        var metrics = font.Metrics;
        RenderValidation.Finite(metrics.Top, nameof(fontSize));
        RenderValidation.Finite(metrics.Bottom, nameof(fontSize));
        RenderValidation.Finite(metrics.Ascent, nameof(fontSize));
        RenderValidation.Finite(metrics.Descent, nameof(fontSize));
        RenderValidation.Finite(metrics.Leading, nameof(fontSize));
        RenderValidation.Finite(metrics.MaxCharacterWidth, nameof(fontSize));
        var result = namedInstanceShaper?.Shape(buffer, font) ?? shaper!.Shape(buffer, font);
        var ids = new ushort[result.Codepoints.Length];
        var glyphs = new ShapedGlyph[ids.Length];
        RenderValidation.Finite(result.Width, nameof(fontSize));
        var physicalClusters = result.Clusters.Select((cluster, index) => (Cluster: checked((int)cluster), X: result.Points[index].X))
            .GroupBy(item => item.Cluster).Select(group => (Cluster: group.Key, X: group.Min(item => item.X))).OrderBy(item => item.X).ToArray();
        var clusterIndices = physicalClusters.Select((item, index) => (item.Cluster, Index: index)).ToDictionary(item => item.Cluster, item => item.Index);
        var clusters = new ShapedTextCluster[physicalClusters.Length];
        for (var index = 0; index < physicalClusters.Length; index++)
        {
            var item = physicalClusters[index];
            var end = index + 1 < physicalClusters.Length ? physicalClusters[index + 1].X + (index + 1) * letterSpacing :
                result.Width + index * letterSpacing;
            clusters[index] = new(item.Cluster, item.X + index * letterSpacing, end)
            {
                ContentEnd = index + 1 < physicalClusters.Length ? end - letterSpacing : end
            };
        }
        var advanceWidth = result.Width + Math.Max(0, physicalClusters.Length - 1) * letterSpacing;
        RenderValidation.Finite(advanceWidth, nameof(letterSpacing));
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = checked((ushort)result.Codepoints[i]);
            if (ids[i] == 0)
            {
                throw new InvalidOperationException($"The font is missing the glyphs required for UTF-16 cluster {result.Clusters[i]}.");
            }

            var original = result.Points[i];
            result.Points[i] = new(original.X + clusterIndices[checked((int)result.Clusters[i])] * letterSpacing, original.Y);
            var point = new Vector2(result.Points[i].X, result.Points[i].Y);
            RenderValidation.Point(point, nameof(fontSize));
            glyphs[i] = new(ids[i], checked((int)result.Clusters[i]), point);
        }

        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(ids, font, result.Points);
        var blob = builder.Build() ?? throw new InvalidOperationException("The text did not produce drawable glyphs.");
        try
        {
            var bounds = blob.Bounds;
            RenderValidation.Finite(bounds.Left, nameof(fontSize));
            RenderValidation.Finite(bounds.Top, nameof(fontSize));
            RenderValidation.Finite(bounds.Right, nameof(fontSize));
            RenderValidation.Finite(bounds.Bottom, nameof(fontSize));
            _ = font.GetGlyphWidths(ids.AsSpan(), out var glyphBounds);
            var inkBounds = SKRect.Empty;
            for (var index = 0; index < glyphBounds.Length; index++)
            {
                var glyphBound = glyphBounds[index];
                if (glyphBound.IsEmpty)
                {
                    continue;
                }

                glyphBound.Offset(result.Points[index]);
                var clusterIndex = clusterIndices[checked((int)result.Clusters[index])];
                var cluster = clusters[clusterIndex];
                clusters[clusterIndex] = cluster with
                {
                    InkBounds = cluster.InkBounds.IsEmpty ? glyphBound : SKRect.Union(cluster.InkBounds, glyphBound)
                };
                RenderValidation.Finite(glyphBound.Left, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Top, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Right, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Bottom, nameof(fontSize));
                inkBounds = inkBounds.IsEmpty ? glyphBound : SKRect.Union(inkBounds, glyphBound);
            }

            return new(blob, glyphs, clusters, advanceWidth, inkBounds, metrics, font);
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    internal string FontFamily { get; }
    internal SubtitleFontVariant? ResolvedFontVariant { get; }
    internal bool SynthesizesBold => synthesizeBold;
    internal bool SynthesizesItalic => synthesizeItalic;

    internal bool UsesTypeface(SKTypeface candidate)
    {
        return ReferenceEquals(typeface, candidate);
    }

    internal bool ContainsGlyphs(string text)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        using var font = new SKFont(typeface);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsShapingControl(rune) && !font.ContainsGlyph(rune.Value))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsShapingControl(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format || rune.Value is >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        shaper?.Dispose();
        namedInstanceShaper?.Dispose();
        typeface.Dispose();
        isDisposed = true;
    }

    private static void ValidateText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done
                || Rune.IsControl(rune) || rune.Value is 0x2028 or 0x2029)
            {
                throw new ArgumentException("Valid single-line UTF-16 text is required; line breaks and control characters are not supported.", nameof(text));
            }

            remaining = remaining[consumed..];
        }
    }
}
