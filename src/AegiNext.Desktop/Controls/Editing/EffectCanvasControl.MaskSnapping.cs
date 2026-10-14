using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    public static readonly StyledProperty<bool> IsMaskSnapEnabledProperty =
        AvaloniaProperty.Register<EffectCanvasControl, bool>(nameof(IsMaskSnapEnabled), true);

    private const double MASK_SNAP_DISTANCE = 8;
    private ProjectLayerGeometry? maskSnapReference;
    private MaskSnapResult maskSnapFeedback;
    private Point maskRectangleStart;

    static EffectCanvasControl()
    {
        AffectsRender<EffectCanvasControl>(IsMaskSnapEnabledProperty);
    }

    /// <summary>是否将蒙版边界和节点吸附到当前字幕的四边及横、竖中线。</summary>
    public bool IsMaskSnapEnabled
    {
        get => GetValue(IsMaskSnapEnabledProperty);
        set => SetValue(IsMaskSnapEnabledProperty, value);
    }

    internal MaskSnapResult MaskSnapFeedback => maskSnapFeedback;

    private bool HasActiveMaskGesture => maskDragging || !openContour.IsEmpty || creatingNodeHandle ||
        maskDeletionHandle is not null || maskInsertionHandle is not null;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMaskSnapEnabledProperty && !IsMaskSnapEnabled)
        {
            maskSnapFeedback = default;
        }
    }

    private void BeginMaskSnapping()
    {
        maskSnapReference = selected is null ? null : GetGeometry(EditorDocument(), selected.Id);
        if (maskSnapReference?.HasInk != true)
        {
            maskSnapReference = null;
        }
        maskSnapFeedback = default;
    }

    private MaskSnapGeometry? MaskSnappingGeometry()
    {
        if (!IsMaskSnapEnabled || maskSnapReference is not { } geometry)
        {
            return null;
        }
        var projection = geometry.Projection;
        if (projection.M31 != 0 || projection.M32 != 0 || projection.M33 != 1)
        {
            var worldBounds = geometry.WorldBounds;
            return MaskSnapGeometry.Create(new(worldBounds.Left, worldBounds.Top, worldBounds.Width, worldBounds.Height), Fit());
        }
        var bounds = geometry.LocalBounds;
        return MaskSnapGeometry.Create(new(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            ToMatrix(geometry.LocalToWorld) * Fit());
    }

    private Point SnapMaskPoint(Point point)
    {
        maskSnapFeedback = MaskSnappingGeometry()?.SnapPoint(point, MASK_SNAP_DISTANCE) ?? default;
        return point + maskSnapFeedback.Offset;
    }

    private ClipMask SnapMaskTranslation(ClipMask mask, Matrix inverseFit)
    {
        maskSnapFeedback = default;
        if (MaskSnappingGeometry() is not { } geometry)
        {
            return mask;
        }
        var bounds = MaskCanvasGeometry.Bounds(mask, MaskMatrix(mask) * geometry.ScreenToLocal);
        maskSnapFeedback = geometry.SnapBounds(bounds, MASK_SNAP_DISTANCE);
        var offset = new Point(maskSnapFeedback.Offset.X, maskSnapFeedback.Offset.Y) * inverseFit - default(Point) * inverseFit;
        return mask with { Transform = mask.Transform with { Position = Offset(mask.Transform.Position, offset) } };
    }

    private void DrawMaskSnapGuides(DrawingContext context)
    {
        if (!maskSnapFeedback.HasSnap || MaskSnappingGeometry() is not { } geometry)
        {
            return;
        }
        var bounds = geometry.LocalBounds;
        var matrix = geometry.LocalToScreen;
        var pen = new Pen(Brushes.DeepSkyBlue, 1, new DashStyle([4, 3], 0));
        if (maskSnapFeedback.XGuide is { } x)
        {
            context.DrawLine(pen, new Point(x, bounds.Top) * matrix, new Point(x, bounds.Bottom) * matrix);
        }
        if (maskSnapFeedback.YGuide is { } y)
        {
            context.DrawLine(pen, new Point(bounds.Left, y) * matrix, new Point(bounds.Right, y) * matrix);
        }
    }

    private void ResetMaskSnapping()
    {
        maskSnapReference = null;
        maskSnapFeedback = default;
    }
}
