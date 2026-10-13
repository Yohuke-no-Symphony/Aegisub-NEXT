using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>完整字素的逻辑选区及两侧光标；连字内部按字素分配同一 cluster 的推进宽度。</summary>
public sealed record SubtitleGraphemeGeometry(int Utf16Start, int Utf16Length, int LineIndex,
    SKRect Bounds, SKRect LeadingCaret, SKRect TrailingCaret)
{
    public SKMatrix LocalToVisible { get; init; } = SKMatrix.Identity;
    public SKRect? UntransformedBounds { get; init; }
    internal ProjectiveMapping? ProjectionMapping { get; init; }

    /// <summary>将可见排版点逆映射到原始字素，拒绝投影后方与奇异映射。</summary>
    public bool TryUnmapVisiblePoint(SKPoint point, out SKPoint original)
    {
        if (ProjectionMapping is { } projection)
        {
            return projection.TryUnmap(point, out original);
        }
        if (LocalToVisible.TryInvert(out var inverse))
        {
            original = inverse.MapPoint(point);
            return float.IsFinite(original.X) && float.IsFinite(original.Y);
        }
        original = default;
        return false;
    }
}
