using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>实际绘制内容与编辑画布共享的局部边界、轴心及父子变换；字形边界不包括描边和阴影。</summary>
public sealed record ProjectLayerGeometry(SKRect LocalBounds, SKPoint LocalPivot, SKPoint BasePosition,
    SKMatrix LocalToWorld, SKMatrix ParentToWorld, bool HasInk = true)
{
    public IReadOnlyList<SubtitleGraphemeGeometry>? VisibleGraphemes { get; init; }
    public LayerShape? Shape { get; init; }
    public double ShapeStrokeWidth { get; init; }
    public bool ShapeHasFill { get; init; }
    public SKPoint WorldPivot => LocalToWorld.MapPoint(LocalPivot);
    public IReadOnlyList<SKPoint> WorldCorners =>
    [
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Bottom),
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Bottom)
    ];

    /// <summary>按外层与文字局部变换逆映射测试可见字素，奇异变换不产生虚假命中。</summary>
    public bool ContainsWorldPoint(SKPoint point)
    {
        if (!LocalToWorld.TryInvert(out var outer))
        {
            return false;
        }
        var local = outer.MapPoint(point);
        if (Shape is { } shape)
        {
            using var path = ProjectShapeGeometry.CreatePath(shape);
            if (ShapeHasFill && ShapeGeometry.CreatePath(shape).Closed && path.Contains(local.X, local.Y))
            {
                return true;
            }
            if (ShapeStrokeWidth <= 0)
            {
                return false;
            }
            using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)ShapeStrokeWidth,
                StrokeJoin = SKStrokeJoin.Round };
            using var outline = new SKPath();
            stroke.GetFillPath(path, outline);
            return outline.Contains(local.X, local.Y);
        }
        if (VisibleGraphemes is null)
        {
            return LocalBounds.Contains(local);
        }
        foreach (var glyph in VisibleGraphemes)
        {
            if (glyph.UntransformedBounds is { } bounds)
            {
                if (glyph.LocalToVisible.TryInvert(out var inverse) && bounds.Contains(inverse.MapPoint(local)))
                {
                    return true;
                }
            }
            else if (glyph.Bounds.Contains(local))
            {
                return true;
            }
        }
        return false;
    }
}
