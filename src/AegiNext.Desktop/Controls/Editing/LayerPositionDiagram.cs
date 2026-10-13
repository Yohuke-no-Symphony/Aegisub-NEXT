using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Controls;

/// <summary>共用父画布投影的只读布局示意：边距、内容逻辑边界、anchor、pivot 和像素偏移。</summary>
public sealed class LayerPositionDiagram : Control
{
    private const double PADDING = 12;
    private const double LEGEND_HEIGHT = 20;
    public static readonly StyledProperty<LayerPositionDraftGeometry?> GeometryProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, LayerPositionDraftGeometry?>(nameof(Geometry));
    public static readonly StyledProperty<LayerPosition?> PositionProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, LayerPosition?>(nameof(Position));
    public static readonly StyledProperty<bool> ShowMarginsProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, bool>(nameof(ShowMargins));
    public static readonly StyledProperty<SubtitleMargins?> MarginsProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, SubtitleMargins?>(nameof(Margins));
    public static readonly StyledProperty<int> AlignmentProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, int>(nameof(Alignment), 7);
    public static readonly StyledProperty<bool> IsExplicitProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, bool>(nameof(IsExplicit));
    public static readonly StyledProperty<int> CanvasWidthProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, int>(nameof(CanvasWidth), 1920);
    public static readonly StyledProperty<int> CanvasHeightProperty =
        AvaloniaProperty.Register<LayerPositionDiagram, int>(nameof(CanvasHeight), 1080);
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LayerPositionDiagram>();

    static LayerPositionDiagram()
    {
        AffectsRender<LayerPositionDiagram>(GeometryProperty, PositionProperty, ShowMarginsProperty, MarginsProperty,
            AlignmentProperty, IsExplicitProperty, CanvasWidthProperty, CanvasHeightProperty, ForegroundProperty);
    }

    /// <summary>禁用输入命中；示意不创建任何编辑事务。</summary>
    public LayerPositionDiagram()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public LayerPositionDraftGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }
    public LayerPosition? Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }
    public bool ShowMargins
    {
        get => GetValue(ShowMarginsProperty);
        set => SetValue(ShowMarginsProperty, value);
    }
    public SubtitleMargins? Margins
    {
        get => GetValue(MarginsProperty);
        set => SetValue(MarginsProperty, value);
    }
    public int Alignment
    {
        get => GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }
    public bool IsExplicit
    {
        get => GetValue(IsExplicitProperty);
        set => SetValue(IsExplicitProperty, value);
    }
    public int CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }
    public int CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>将边距与测量结果绘制到同一父局部投影；超出画布的字形和偏移仍完整可见。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= PADDING * 2 || Bounds.Height <= PADDING * 2 + LEGEND_HEIGHT)
        {
            return;
        }
        var geometry = Geometry;
        var position = Position;
        var hasGeometry = geometry is not null && double.IsFinite(geometry.ParentSize.X) && double.IsFinite(geometry.ParentSize.Y) &&
                          geometry.ParentSize.X > 0 && geometry.ParentSize.Y > 0 &&
                          double.IsFinite(geometry.OffsetScale) && geometry.OffsetScale > 0;
        var parentSize = hasGeometry ? geometry!.ParentSize : new ScenePoint(CanvasWidth, CanvasHeight);
        if (parentSize.X <= 0 || parentSize.Y <= 0 || (!ShowMargins && (!hasGeometry || position is null)))
        {
            return;
        }
        var validMargins = Margins is { } margins && IsValidMargin(margins.Left) && IsValidMargin(margins.Right) &&
                           IsValidMargin(margins.Vertical) && Enum.IsDefined((ProjectTextAlignment)Alignment);
        var drawGeometry = hasGeometry && position is not null && (!ShowMargins || validMargins);
        var anchor = default(Point);
        var offset = default(Point);
        var pivot = default(Point);
        var hasProjectedPivot = false;
        var glyph = Array.Empty<Point>();
        if (drawGeometry)
        {
            var measured = geometry!;
            var placement = position! with { Offset = new(position!.Offset.X * measured.OffsetScale, position.Offset.Y * measured.OffsetScale) };
            anchor = new(placement.Anchor.X * parentSize.X, placement.Anchor.Y * parentSize.Y);
            var basePosition = LayerPositionGeometry.BasePosition(placement, parentSize);
            offset = new(basePosition.X, basePosition.Y);
            var center = new ScenePoint(offset.X + measured.Transform.X, offset.Y + measured.Transform.Y);
            var normalizedPivot = LayerPositionGeometry.LocalPivot(placement, measured.GlyphOrigin, measured.GlyphSize);
            var localPivot = new ScenePoint(normalizedPivot.X + measured.Transform.Pivot.X,
                normalizedPivot.Y + measured.Transform.Pivot.Y);
            var projection = SceneProjection.CreateMatrix(measured.Transform, localPivot, center);
            if (projection.TryMap(localPivot, out var projectedPivot, SceneProjection.NEAR_PLANE_RATIO))
            {
                pivot = new(projectedPivot.X, projectedPivot.Y);
                hasProjectedPivot = true;
            }
            ScenePoint[] localCorners =
            [
                measured.GlyphOrigin,
                new(measured.GlyphOrigin.X + measured.GlyphSize.X, measured.GlyphOrigin.Y),
                new(measured.GlyphOrigin.X + measured.GlyphSize.X, measured.GlyphOrigin.Y + measured.GlyphSize.Y),
                new(measured.GlyphOrigin.X, measured.GlyphOrigin.Y + measured.GlyphSize.Y)
            ];
            glyph = projection.Clip(localCorners).Select(point =>
            {
                var projected = projection.Map(point);
                return new Point(projected.X, projected.Y);
            }).ToArray();
        }
        var points = drawGeometry ? glyph.Append(offset).Concat(hasProjectedPivot ? [pivot] : Array.Empty<Point>()).ToArray() : [];
        var minimumX = points.Length == 0 ? 0 : Math.Min(0, points.Min(point => point.X));
        var minimumY = points.Length == 0 ? 0 : Math.Min(0, points.Min(point => point.Y));
        var maximumX = points.Length == 0 ? parentSize.X : Math.Max(parentSize.X, points.Max(point => point.X));
        var maximumY = points.Length == 0 ? parentSize.Y : Math.Max(parentSize.Y, points.Max(point => point.Y));
        var visible = new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
        var viewport = new Rect(PADDING, PADDING, Bounds.Width - PADDING * 2,
            Bounds.Height - PADDING * 2 - LEGEND_HEIGHT);
        var scale = Math.Min(viewport.Width / visible.Width, viewport.Height / visible.Height);
        var origin = viewport.Center - new Vector(visible.Width * scale / 2, visible.Height * scale / 2);
        var canvas = new Rect(Map(new(0, 0)), Map(new(parentSize.X, parentSize.Y)));
        context.DrawRectangle(null, new Pen(Foreground, 1), canvas);
        if (ShowMargins)
        {
            if (!validMargins)
            {
                return;
            }
            var marginScale = hasGeometry ? geometry!.OffsetScale : 1;
            var scaledMargins = new SubtitleMargins(Margins!.Value.Left * marginScale,
                Margins.Value.Right * marginScale, Margins.Value.Vertical * marginScale);
            DrawMargins(context, scaledMargins, parentSize, canvas, Map);
        }
        if (!drawGeometry)
        {
            return;
        }

        if (glyph.Length >= 3)
        {
            var outline = new StreamGeometry();
            using (var path = outline.Open())
            {
                path.BeginFigure(Map(glyph[0]), true);
                foreach (var point in glyph.Skip(1))
                {
                    path.LineTo(Map(point));
                }
                path.EndFigure(true);
            }
            var glyphPen = new Pen(Brushes.SeaGreen, 1.5,
                geometry!.HasInk ? null : new DashStyle([3, 3], 0));
            context.DrawGeometry(new SolidColorBrush(Color.Parse("#1832CD32")), glyphPen, outline);
        }
        var mappedAnchor = Map(anchor);
        var mappedOffset = Map(offset);
        context.DrawLine(new Pen(Foreground, 1), mappedAnchor, mappedOffset);
        var direction = mappedOffset - mappedAnchor;
        var distance = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (distance > 8)
        {
            var unit = direction / distance;
            var perpendicular = new Vector(-unit.Y, unit.X);
            context.DrawLine(new Pen(Foreground, 1), mappedOffset, mappedOffset - unit * 5 + perpendicular * 3);
            context.DrawLine(new Pen(Foreground, 1), mappedOffset, mappedOffset - unit * 5 - perpendicular * 3);
        }
        if (hasProjectedPivot && offset != pivot)
        {
            context.DrawLine(new Pen(Foreground, 1, new DashStyle([2, 2], 0)), mappedOffset, Map(pivot));
        }
        DrawAnchor(context, mappedAnchor);
        if (hasProjectedPivot)
        {
            DrawPivot(context, Map(pivot));
        }
        var legendY = Bounds.Height - LEGEND_HEIGHT + 3;
        DrawAnchor(context, new(9, legendY + 6));
        DrawText(context, "A", new(16, legendY), Brushes.RoyalBlue);
        DrawPivot(context, new(40, legendY + 6));
        DrawText(context, "P", new(48, legendY), Brushes.DarkOrange);
        var delta = string.Create(CultureInfo.CurrentCulture, $"Δ {position!.Offset.X:0.##}, {position.Offset.Y:0.##} px");
        DrawText(context, delta, new(67, legendY), Foreground);

        Point Map(Point point)
        {
            return origin + new Vector((point.X - visible.Left) * scale, (point.Y - visible.Top) * scale);
        }
    }

    private static bool IsValidMargin(double value)
    {
        return double.IsFinite(value) && value is >= 0 and <= 32768;
    }

    private void DrawMargins(DrawingContext context, SubtitleMargins margins, ScenePoint parentSize, Rect canvas, Func<Point, Point> map)
    {
        var left = Math.Min(margins.Left, parentSize.X);
        var right = Math.Max(left, parentSize.X - Math.Min(margins.Right, parentSize.X));
        using (context.PushOpacity(0.18))
        {
            context.DrawRectangle(Foreground, null, new Rect(map(new(0, 0)), map(new(left, parentSize.Y))));
            context.DrawRectangle(Foreground, null, new Rect(map(new(right, 0)), map(new(parentSize.X, parentSize.Y))));
        }
        var pen = new Pen(Foreground, 1, new DashStyle([2, 2], 0));
        context.DrawLine(pen, map(new(left, 0)), map(new(left, parentSize.Y)));
        context.DrawLine(pen, map(new(right, 0)), map(new(right, parentSize.Y)));
        DrawMarginLabel(context, "L", new(canvas.Left - 6, canvas.Center.Y));
        DrawMarginLabel(context, "R", new(canvas.Right + 6, canvas.Center.Y));
        var row = Alignment / 3;
        if (IsExplicit || row == 1 || right <= left)
        {
            return;
        }
        var vertical = Math.Min(margins.Vertical, parentSize.Y);
        var edge = row == 0 ? vertical : parentSize.Y - vertical;
        var band = row == 0
            ? new Rect(map(new(left, 0)), map(new(right, edge)))
            : new Rect(map(new(left, edge)), map(new(right, parentSize.Y)));
        using (context.PushOpacity(0.18))
        {
            context.DrawRectangle(Foreground, null, band);
        }
        context.DrawLine(pen, map(new(left, edge)), map(new(right, edge)));
        DrawMarginLabel(context, "V", new(canvas.Center.X, row == 0 ? canvas.Top - 6 : canvas.Bottom + 6));
    }

    private void DrawMarginLabel(DrawingContext context, string text, Point center)
    {
        using var layout = WorkbenchTextFormatting.CreateLayout(this, text, 9, Foreground, 10);
        var x = Math.Clamp(center.X - layout.Width / 2, 0, Math.Max(0, Bounds.Width - layout.Width));
        var y = Math.Clamp(center.Y - layout.Height / 2, 0, Math.Max(0, Bounds.Height - layout.Height));
        layout.Draw(context, new(x, y));
    }

    private static void DrawAnchor(DrawingContext context, Point point)
    {
        context.DrawEllipse(Brushes.RoyalBlue, null, point, 3, 3);
    }

    private static void DrawPivot(DrawingContext context, Point point)
    {
        var pen = new Pen(Brushes.DarkOrange, 1.5);
        context.DrawEllipse(null, pen, point, 4, 4);
        context.DrawLine(pen, point - new Vector(2, 0), point + new Vector(2, 0));
        context.DrawLine(pen, point - new Vector(0, 2), point + new Vector(0, 2));
    }

    private void DrawText(DrawingContext context, string text, Point point, IBrush? brush)
    {
        using var layout = WorkbenchTextFormatting.CreateLayout(this, text, 10, brush, LEGEND_HEIGHT);
        layout.Draw(context, point);
    }
}
