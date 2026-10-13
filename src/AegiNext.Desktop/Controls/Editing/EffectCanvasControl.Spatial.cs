using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Styling;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private AnimationTrackTarget spatialTarget;
    private CanvasSpatialGesture? spatialGesture;
    private ProjectDocument? spatialDraftDocument;
    private bool spatialBeginPending;
    private long spatialCancellationRevision;

    public event EventHandler<CanvasSpatialGestureStartingEventArgs>? SpatialGestureStarting;
    public event EventHandler<CanvasSpatialEditEventArgs>? SpatialEdited;

    internal AnimationTrackTarget SpatialTarget
    {
        get => spatialTarget;
        set
        {
            if (spatialTarget != value)
            {
                spatialCancellationRevision++;
                CancelDrag();
                spatialTarget = value;
                InvalidateVisual();
            }
        }
    }

    internal IReadOnlyDictionary<AnimationProperty, Point> SpatialHandlePoints()
    {
        var result = new Dictionary<AnimationProperty, Point>();
        var scene = spatialDraftDocument ?? EditorDocument();
        if (!TryGetSpatialGeometry(scene, out var geometry, out var transform) ||
            !CanvasSpatialGeometry.TryProjectVector(default, transform, geometry, Fit(), out var center))
        {
            return result;
        }
        var fit = Fit();
        var radius = SpatialRadius(geometry, fit);
        foreach (var property in CanvasSpatialGeometry.Properties.Take(3))
        {
            if (HasOrderedSpatialTarget(property))
            {
                continue;
            }
            var angle = property == AnimationProperty.ROTATION ? -Math.PI / 4 : Math.PI / 4;
            if (CanvasSpatialGeometry.TryProjectVector(CanvasSpatialGeometry.RingPoint(property, radius, angle),
                    transform, geometry, fit, out var point))
            {
                result.Add(property, point);
            }
        }
        if (!HasOrderedSpatialTarget(AnimationProperty.PERSPECTIVE_DISTANCE))
        {
            result.Add(AnimationProperty.PERSPECTIVE_DISTANCE, center + new Vector(radius * Math.Abs(fit.M11) + 28, 0));
        }
        return result;
    }

    private bool TryGetSpatialGeometry(ProjectDocument scene, out ProjectLayerGeometry geometry, out LayerTransform transform)
    {
        geometry = null!;
        transform = null!;
        if (selected is null)
        {
            return false;
        }
        try
        {
            renderer ??= new(new DirectoryProjectAssetResolver(directory));
            var found = spatialTarget.TextRangeId is { } rangeId
                ? renderer.GetAnimationRangeGeometry(scene, position, selected.Id, rangeId)
                : renderer.GetLayerGeometry(scene, position, selected.Id);
            if (found is null)
            {
                return false;
            }
            geometry = found;
            if (geometry.AnimationRange is { } range)
            {
                transform = CanvasSpatialGeometry.RangeTransform(range);
                return true;
            }
            var evaluated = SceneEvaluator.Evaluate(scene, position).FirstOrDefault(layer => layer.Source.Id == selected.Id);
            if (evaluated is null)
            {
                return false;
            }
            transform = evaluated.Transform;
            return true;
        }
        catch (Exception error) when (IsRenderingFailure(error))
        {
            ReportRenderingFailure(error);
            return false;
        }
    }

    private static double SpatialRadius(ProjectLayerGeometry geometry, Matrix fit)
    {
        var scale = Math.Max(1e-9, Math.Abs(fit.M11));
        return Math.Clamp(Math.Max(geometry.LocalBounds.Width, geometry.LocalBounds.Height) * 0.6, 30 / scale, 90 / scale);
    }

    private void DrawSpatialOverlay(DrawingContext context, ProjectDocument scene)
    {
        if (!TryGetSpatialGeometry(scene, out var geometry, out var transform))
        {
            return;
        }
        var fit = Fit();
        var outline = geometry.VisiblePolygon;
        var border = new Pen(Brushes.DeepSkyBlue, 1);
        for (var index = 0; index < outline.Count; index++)
        {
            var next = outline[(index + 1) % outline.Count];
            context.DrawLine(border, new Point(outline[index].X, outline[index].Y) * fit, new Point(next.X, next.Y) * fit);
        }
        var radius = SpatialRadius(geometry, fit);
        foreach (var property in CanvasSpatialGeometry.Properties.Take(3))
        {
            var pen = new Pen(SpatialBrush(property), spatialGesture?.HandleProperty == property ? 2.5 : 1.5);
            Point? previous = null;
            for (var index = 0; index <= 72; index++)
            {
                var vector = CanvasSpatialGeometry.RingPoint(property, radius, index * Math.PI / 36);
                if (CanvasSpatialGeometry.TryProjectVector(vector, transform, geometry, fit, out var point))
                {
                    if (previous is { } start)
                    {
                        context.DrawLine(pen, start, point);
                    }
                    previous = point;
                }
                else
                {
                    previous = null;
                }
            }
        }
        if (CanvasSpatialGeometry.TryProjectVector(default, transform, geometry, fit, out var center))
        {
            context.DrawEllipse(Brushes.White, border, center, 3, 3);
            if (SpatialHandlePoints().TryGetValue(AnimationProperty.PERSPECTIVE_DISTANCE, out var distance))
            {
                context.DrawLine(new Pen(Brushes.Gold, 1), center, distance);
            }
        }
        foreach (var (property, point) in SpatialHandlePoints())
        {
            var brush = SpatialBrush(property);
            context.DrawEllipse(brush, new Pen(Brushes.White, 1), point, 5, 5);
            var axis = property switch
            {
                AnimationProperty.ROTATION_X => "X",
                AnimationProperty.ROTATION_Y => "Y",
                AnimationProperty.ROTATION => "Z",
                _ => "D"
            };
            var label = $"{axis} {CanvasSpatialGeometry.Value(transform, property).ToString("0.##", CultureInfo.CurrentCulture)}";
            using var layout = WorkbenchTextFormatting.CreateLayout(this, label, 11, brush, lineHeight: 16);
            var rectangle = new Rect(point.X + 8, point.Y - layout.Height / 2, layout.Width, layout.Height);
            layout.Draw(context, WorkbenchTextFormatting.CenteredOrigin(layout, rectangle));
        }
    }

    private static IBrush SpatialBrush(AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.ROTATION_X => Brushes.Tomato,
            AnimationProperty.ROTATION_Y => Brushes.LimeGreen,
            AnimationProperty.ROTATION => Brushes.DeepSkyBlue,
            _ => Brushes.Gold
        };
    }

    private static AnimationTrackTarget SpatialIdentity(AnimationProperty property, Guid? rangeId)
    {
        return new(property == AnimationProperty.PERSPECTIVE_DISTANCE ? property : AnimationProperty.ROTATION_3D,
            TextRangeId: rangeId);
    }

    private static int SpatialComponent(AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.ROTATION_Y => 1,
            AnimationProperty.ROTATION => 2,
            _ => 0
        };
    }

    private bool HasOrderedSpatialTarget(AnimationProperty property)
    {
        var identity = SpatialIdentity(property, spatialTarget.TextRangeId);
        return selected!.Tracks.Any(track => !track.Transforms.IsEmpty &&
            (track.Target == identity || identity.Property == AnimationProperty.ROTATION_3D &&
                track.Target.TextRangeId == identity.TextRangeId && track.Target.NodeId is null &&
                track.Target.State == SubtitleAnimationState.NORMAL &&
                track.Property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y));
    }

    private AnimationValue SpatialValue(AnimationTrackTarget identity, MediaTime localTime)
    {
        var source = selected!;
        var line = source.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        var tracks = identity.Property == AnimationProperty.ROTATION_3D
            ? RotationAnimationTrackMigration.Normalize(source, line) : source.Tracks;
        var track = tracks.FirstOrDefault(track => track.Target == identity);
        return track is null ? SubtitleAnimationEvaluation.GetBaseValue(source, line, identity) :
            SceneEvaluator.EvaluateTrack(track, localTime);
    }

    private bool SpatialPointerPressed(PointerPressedEventArgs e)
    {
        if (EditMode != CanvasEditMode.SPATIAL_TRANSFORM)
        {
            return false;
        }
        e.Handled = true;
        var point = e.GetPosition(this);
        var nearest = SpatialHandlePoints().Where(pair => DistanceSquared(pair.Value, point) <= 100)
            .OrderBy(pair => DistanceSquared(pair.Value, point)).ToArray();
        if (nearest.Length == 0 || !BeginSpatialDrag(point, nearest[0].Key))
        {
            return true;
        }
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        return true;
    }

    internal bool BeginSpatialDrag(Point point, AnimationProperty property)
    {
        if (disposed || spatialBeginPending || gestureCancellationPending || selected is null || EditMode != CanvasEditMode.SPATIAL_TRANSFORM ||
            !CanvasSpatialGeometry.Properties.Contains(property))
        {
            return false;
        }
        CancelDrag();
        var identity = SpatialIdentity(property, spatialTarget.TextRangeId);
        var starting = new CanvasSpatialGestureStartingEventArgs(selected.Id, identity);
        var cancellation = spatialCancellationRevision;
        spatialBeginPending = true;
        try
        {
            SpatialGestureStarting?.Invoke(this, starting);
        }
        finally
        {
            spatialBeginPending = false;
        }
        if (starting.Cancel || disposed || EditMode != CanvasEditMode.SPATIAL_TRANSFORM || cancellation != spatialCancellationRevision ||
            selected?.Id != starting.LayerId || spatialTarget.TextRangeId != identity.TextRangeId ||
            !TryGetSpatialGeometry(EditorDocument(), out var geometry, out var transform) ||
            HasOrderedSpatialTarget(property) ||
            !CanvasSpatialGeometry.TryProjectVector(default, transform, geometry, Fit(), out var center))
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
            return false;
        }
        var localTime = starting.LocalTime ?? position - selected.Start + selected.AnimationOffset;
        var value = SpatialValue(identity, localTime);
        spatialGesture = new(document, selected.Id, identity, property, localTime, starting.IsKeyframe, transform, Fit(), center,
            point, value, value, CanvasSpatialGeometry.PointerAngle(point, center), 0);
        return true;
    }

    private bool SpatialPointerMoved(PointerEventArgs e)
    {
        if (spatialGesture is not { } gesture)
        {
            return EditMode == CanvasEditMode.SPATIAL_TRANSFORM;
        }
        var point = e.GetPosition(this);
        var angle = CanvasSpatialGeometry.PointerAngle(point, gesture.Center);
        var accumulated = CanvasSpatialGeometry.AccumulateAngle(gesture.AccumulatedAngle, gesture.LastAngle, angle);
        var component = SpatialComponent(gesture.HandleProperty);
        var scalar = CanvasSpatialGeometry.DragValue(gesture.HandleProperty, gesture.OriginalValue.GetComponent(component), gesture.Start,
            point, accumulated, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        var value = gesture.OriginalValue.WithComponent(component, scalar);
        spatialGesture = gesture with { Value = value, LastAngle = angle, AccumulatedAngle = accumulated };
        var source = gesture.Source;
        var layer = source.Layers.Single(layer => layer.Id == gesture.LayerId);
        var line = layer.SubtitleId is { } subtitleId ? source.Subtitles.Single(line => line.Id == subtitleId) : null;
        var tracks = gesture.Target.Property == AnimationProperty.ROTATION_3D
            ? RotationAnimationTrackMigration.Normalize(layer, line) : layer.Tracks;
        source = ReplaceLayer(source, layer with { Tracks = tracks.Where(track => track.Target != gesture.Target).ToImmutableArray() });
        spatialDraftDocument = SubtitleAnimationEditing.SetBaseValue(source, gesture.LayerId, gesture.Target, value);
        if (editingPose && selected is not null && position == selected.End)
        {
            spatialDraftDocument = spatialDraftDocument with { Layers = ExtendEditorEndpoint(spatialDraftDocument.Layers, selected.Id) };
        }
        renderedDocument = null;
        sceneRevision++;
        e.Handled = true;
        InvalidateVisual();
        return true;
    }

    private bool SpatialPointerReleased(PointerReleasedEventArgs e)
    {
        if (EditMode != CanvasEditMode.SPATIAL_TRANSFORM)
        {
            return false;
        }
        var result = spatialGesture;
        CancelDrag(false);
        if (result is not null && result.Value != result.OriginalValue)
        {
            SpatialEdited?.Invoke(this, new(result.LayerId, result.Target, result.Value));
        }
        else if (result is not null)
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
        return true;
    }
}
