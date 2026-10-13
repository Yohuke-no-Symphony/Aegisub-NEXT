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
    public Guid? AnimationRangeId { get; init; }
    public SubtitleAnimationRange? AnimationRange { get; init; }
    internal ProjectiveMapping? ProjectionMapping { get; init; }
    internal ProjectiveMapping? ParentProjectionMapping { get; init; }
    internal IReadOnlyList<SKPoint>? WorldOutline { get; init; }
    internal IReadOnlyList<ProjectedSubtitleGrapheme>? WorldGraphemes { get; init; }
    public ProjectionMatrix3x3 Projection => (ProjectionMapping ?? ProjectiveMapping.Affine(LocalToWorld)).Matrix;
    public ProjectionMatrix3x3 ParentProjection => (ParentProjectionMapping ?? ProjectiveMapping.Affine(ParentToWorld)).Matrix;
    public SKPoint WorldPivot => TryMapLocalPoint(LocalPivot, out var point) ? point : new(float.NaN, float.NaN);
    public IReadOnlyList<SKPoint> VisiblePolygon => WorldCorners;
    public SKRect WorldBounds => ProjectiveMapping.Bounds(VisiblePolygon);
    public IReadOnlyList<SKPoint> WorldCorners => WorldOutline ??
    [
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Bottom),
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Bottom)
    ];

    /// <summary>使用各阶段前向可见条件映射局部点。</summary>
    public bool TryMapLocalPoint(SKPoint point, out SKPoint world) =>
        (ProjectionMapping ?? ProjectiveMapping.Affine(LocalToWorld)).TryMap(point, out world);

    /// <summary>逆映射世界点，并拒绝已被近裁剪的源坐标。</summary>
    public bool TryUnmapWorldPoint(SKPoint point, out SKPoint local) =>
        (ProjectionMapping ?? ProjectiveMapping.Affine(LocalToWorld)).TryUnmap(point, out local);

    /// <summary>将世界点逆映射至当前目标的父坐标，供冻结几何的拖动使用。</summary>
    public bool TryUnmapParentPoint(SKPoint point, out SKPoint parent) =>
        (ParentProjectionMapping ?? ProjectiveMapping.Affine(ParentToWorld)).TryUnmap(point, out parent);

    /// <summary>将目标父坐标映射至世界坐标，并验证外层各阶段的前向可见条件。</summary>
    public bool TryMapParentPoint(SKPoint point, out SKPoint world) =>
        (ParentProjectionMapping ?? ProjectiveMapping.Affine(ParentToWorld)).TryMap(point, out world);

    /// <summary>按外层与文字局部变换逆映射测试可见字素，奇异变换不产生虚假命中。</summary>
    public bool ContainsWorldPoint(SKPoint point)
    {
        if (!HasInk)
        {
            return false;
        }
        if (WorldGraphemes is { } worldGraphemes)
        {
            return worldGraphemes.Any(glyph => glyph.Projection.TryUnmap(point, out var original) &&
                                             glyph.OriginalBounds.Contains(original));
        }
        if (!TryUnmapWorldPoint(point, out var local))
        {
            return false;
        }
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
                if (glyph.TryUnmapVisiblePoint(local, out var original) && bounds.Contains(original))
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
