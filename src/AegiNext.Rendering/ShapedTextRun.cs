using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>
/// 拥有已塑形文字的不可变绘制资源，可在 TextShaper 释放后继续使用；调用方负责释放。
/// </summary>
public sealed class ShapedTextRun : IDisposable
{
    private readonly SKTextBlob blob;
    private readonly ShapedGlyph[] glyphs;
    private readonly ShapedTextCluster[] clusters;
    private readonly SKFont font;
    private readonly Dictionary<int, SKTextBlob> clusterBlobs = [];
    private ILookup<int, ShapedGlyph>? clusterGlyphs;
    private Dictionary<int, SKRect>? clusterInkBounds;
    private bool isDisposed;

    internal ShapedTextRun(SKTextBlob blob, ShapedGlyph[] glyphs, ShapedTextCluster[] clusters,
        float advanceWidth, SKRect inkBounds, SKFontMetrics metrics, SKFont sourceFont)
    {
        this.blob = blob;
        this.glyphs = glyphs;
        this.clusters = clusters;
        AdvanceWidth = advanceWidth;
        InkBounds = inkBounds;
        FontMetrics = metrics;
        font = new(sourceFont.Typeface, sourceFont.Size, sourceFont.ScaleX, sourceFont.SkewX)
        {
            Edging = sourceFont.Edging, Hinting = sourceFont.Hinting, Subpixel = sourceFont.Subpixel,
            Embolden = sourceFont.Embolden
        };
    }

    public float AdvanceWidth { get; }
    public SKRect InkBounds { get; }
    public SKFontMetrics FontMetrics { get; }
    public ReadOnlySpan<ShapedGlyph> Glyphs => glyphs;
    public ReadOnlySpan<ShapedTextCluster> Clusters => clusters;

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        blob.Dispose();
        foreach (var clusterBlob in clusterBlobs.Values)
        {
            clusterBlob.Dispose();
        }
        clusterBlobs.Clear();
        font.Dispose();
        isDisposed = true;
    }

    internal SKTextBlob GetBlob()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return blob;
    }

    internal SKTextBlob GetClusterBlob(int utf16Cluster)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (clusterBlobs.TryGetValue(utf16Cluster, out var existing))
        {
            return existing;
        }
        clusterGlyphs ??= glyphs.ToLookup(glyph => glyph.Utf16Cluster);
        var members = clusterGlyphs[utf16Cluster].ToArray();
        if (members.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Cluster));
        }
        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(members.Select(glyph => glyph.GlyphId).ToArray(), font,
            members.Select(glyph => new SKPoint(glyph.Position.X, glyph.Position.Y)).ToArray());
        var clusterBlob = builder.Build() ?? throw new InvalidOperationException("The shaped cluster did not produce drawable glyphs.");
        clusterBlobs.Add(utf16Cluster, clusterBlob);
        return clusterBlob;
    }

    internal SKRect GetClusterInkBounds(int utf16Cluster)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        clusterInkBounds ??= clusters.ToDictionary(cluster => cluster.Utf16Start, cluster => cluster.InkBounds);
        return clusterInkBounds[utf16Cluster];
    }
}
