using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineAnimationRowCollapseRenderingUiTests
{
    [AvaloniaFact]
    public void CollapsedNodeBlocksKeepTheirIdentitiesAndHitNodesOverrideTheCurrentSelection()
    {
        using var environment = new UiTestEnvironment();
        var firstNode = new MaskNode { Position = new(10, 20) };
        var secondNode = new MaskNode { Position = new(30, 40) };
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, firstNode.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, secondNode.Id);
        var cue = new SubtitleLine { End = new(7) };
        var layer = CreateMaskedLayer(cue, firstNode, secondNode) with
        {
            Tracks = [new(firstTarget, [new(new(1), firstNode.Position)]), new(secondTarget, [new(new(1), secondNode.Position)])]
        };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(document, cue.Id, layer);
        AcceptCollapseRequests(timeline);
        var selections = new List<TimelineKeyframeEventArgs>();
        var acceptDrag = false;
        timeline.KeyframeSelected += (_, e) =>
        {
            selections.Add(e);
            e.SelectionAccepted = acceptDrag;
        };
        var edits = 0;
        timeline.KeyframeMoved += (_, e) =>
        {
            edits++;
            Assert.Equal(secondTarget, e.Target);
            Assert.Equal(AnimationValue.FromVector(secondNode.Position), e.NewValue);
            Assert.Null(e.OperationId);
            editor.UpdateLayer(layer.Id, current => current with
            {
                Tracks = current.Tracks.Select(track => track.Target == e.Target
                    ? track with { Keyframes = [track.Keyframes[0] with { Time = e.NewTime }] } : track).ToImmutableArray()
            });
            timeline.SetDocument(editor.Snapshot, cue.Id, editor.Snapshot.Layers[0]);
        };
        var window = new Window { Width = 850, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var firstPoint = timeline.GetKeyframePoint(layer.Id, firstTarget, new(1), firstNode.Position)!.Value;
            var secondPoint = timeline.GetKeyframePoint(layer.Id, secondTarget, new(1), secondNode.Position)!.Value;
            Assert.NotEqual(firstPoint.Y, secondPoint.Y);
            Assert.Equal(2, timeline.KeyframeMarkers.Count);
            Assert.Equal(2, timeline.KeyframeMarkers.Select(marker => marker.Identity.Target).Distinct().Count());
            Assert.All(timeline.KeyframeMarkers, marker =>
                Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, marker.Components));

            foreach (var target in new[] { firstTarget, secondTarget })
            {
                timeline.SelectedMaskNodeId = target == firstTarget ? secondNode.Id : firstNode.Id;
                var hitPoint = target == firstTarget ? firstPoint : secondPoint;
                window.MouseMove(hitPoint);
                Assert.Equal(target, timeline.HoveredKeyframe!.Identity.Target);
                Assert.Same(timeline, window.InputHitTest(hitPoint));
                window.MouseDown(hitPoint, MouseButton.Left);
                window.MouseUp(hitPoint, MouseButton.Left);
                Assert.Equal(target, selections[^1].Target);
                Assert.Null(selections[^1].OperationId);
                Assert.False(timeline.HasActiveDrag);
            }

            acceptDrag = true;
            window.MouseDown(secondPoint, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(secondPoint + new Vector(80, -30));
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(secondPoint + new Vector(80, -30), MouseButton.Left);

            Assert.Equal(1, edits);
            Assert.Same(layer.Tracks[0], editor.Snapshot.Layers[0].Tracks[0]);
            Assert.Equal(new MediaTime(2), Assert.Single(editor.Snapshot.Layers[0].Tracks[1].Keyframes).Time);
            Assert.Equal(secondNode.Position, Assert.Single(editor.Snapshot.Layers[0].Tracks[1].Keyframes).Value.Vector);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsedTransformEndpointsAndOrdinaryKeysAtTheSameTimesRetainTheirCompleteIdentities(bool startEndpoint)
    {
        using var environment = new UiTestEnvironment();
        var operationNode = new MaskNode { Position = new(10, 20) };
        var keyNode = new MaskNode { Position = new(30, 40) };
        var operationTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, operationNode.Id);
        var keyTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, keyNode.Id);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), new ScenePoint(100, 150), 2.5);
        var operationTrack = new AnimationTrack(operationTarget, []) { InitialValue = operationNode.Position, Transforms = [operation] };
        var keyTrack = new AnimationTrack(keyTarget, [new(new(1), keyNode.Position), new(new(3), new ScenePoint(50, 60))]);
        var cue = new SubtitleLine { End = new(7) };
        var layer = CreateMaskedLayer(cue, operationNode, keyNode) with { Tracks = [operationTrack, keyTrack] };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 80, IsSnapEnabled = false, SelectedMaskNodeId = operationNode.Id, EffectTarget = operationTarget
        };
        timeline.SetDocument(document, cue.Id, layer);
        AcceptCollapseRequests(timeline);
        TimelineKeyframeEventArgs? selected = null;
        timeline.KeyframeSelected += (_, e) =>
        {
            selected = e;
            e.SelectionAccepted = e.Target == operationTarget;
        };
        var edits = 0;
        timeline.KeyframeMoved += (_, e) =>
        {
            edits++;
            Assert.Equal(operationTarget, e.Target);
            Assert.Equal(operation.Id, e.OperationId);
            Assert.Equal(startEndpoint, e.IsOperationStart);
            Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, e.Components);
            var changed = startEndpoint ? operation with { Start = e.NewTime } : operation with { End = e.NewTime };
            editor.SetAnimationTransform(layer.Id, operationTarget, operationTrack.InitialValue!.Value, changed);
            timeline.SetDocument(editor.Snapshot, cue.Id, editor.Snapshot.Layers[0]);
        };
        var window = new Window { Width = 850, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var markers = timeline.KeyframeMarkers.ToArray();
            Assert.Equal(4, markers.Length);
            Assert.Equal(4, markers.Select(marker => marker.Identity).Distinct().Count());
            var operationMarker = Assert.Single(markers, marker => marker.Identity.OperationId == operation.Id &&
                marker.Identity.IsOperationStart == startEndpoint);
            var ordinaryMarker = Assert.Single(markers, marker => marker.Identity.Target == keyTarget &&
                marker.Identity.Time == operationMarker.Identity.Time);
            Assert.Null(ordinaryMarker.Identity.OperationId);
            Assert.Equal(operationMarker.Position.X, ordinaryMarker.Position.X);
            Assert.NotEqual(operationMarker.Position.Y, ordinaryMarker.Position.Y);
            Assert.Equal(2, markers.Select(marker => marker.Position.Y).Distinct().Count());
            var point = operationMarker.Position;
            window.MouseMove(point);
            Assert.Equal(operation.Id, timeline.HoveredKeyframe!.Identity.OperationId);
            Assert.Equal(startEndpoint, timeline.HoveredKeyframe.Identity.IsOperationStart);
            window.MouseDown(point, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            Assert.NotNull(selected);
            Assert.Equal(operation.Id, selected.OperationId);
            Assert.Equal(startEndpoint, selected.IsOperationStart);
            window.MouseMove(point + new Vector(80, -30));
            window.MouseUp(point + new Vector(80, -30), MouseButton.Left);

            Assert.Equal(1, edits);
            var changedOperation = Assert.Single(editor.Snapshot.Layers[0].Tracks.Single(track => track.Target == operationTarget).Transforms);
            Assert.Equal(startEndpoint ? operation with { Start = new(2) } : operation with { End = new(4) }, changedOperation);
            Assert.Same(keyTrack, editor.Snapshot.Layers[0].Tracks.Single(track => track.Target == keyTarget));
            Assert.True(editor.Undo());
            timeline.SetDocument(editor.Snapshot, cue.Id, editor.Snapshot.Layers[0]);
            Assert.Same(document, editor.Snapshot);
            timeline.SelectedMaskNodeId = operationNode.Id;
            point = ordinaryMarker.Position;
            window.MouseMove(point);
            Assert.Equal(keyTarget, timeline.HoveredKeyframe!.Identity.Target);
            Assert.Null(timeline.HoveredKeyframe.Identity.OperationId);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            Assert.NotNull(selected);
            Assert.Equal(keyTarget, selected.Target);
            Assert.Null(selected.OperationId);
            Assert.Equal(1, edits);
            Assert.False(timeline.HasActiveDrag);
            Assert.Equal(4, timeline.KeyframeMarkers.Select(marker => marker.Identity).Distinct().Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LightAndDarkCollapsedRowsRenderAVisibleHorizontalLineAndDistinctKeyframeDiamonds(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var layer = new ProjectLayer
        {
            Name = "动画 Line ABC 123", Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(1), End = new(5),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25), new(new(3), 0.75)])]
        };
        var rowId = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [rowId] };
        AcceptCollapseRequests(timeline);
        var window = new Window
        {
            Width = 850, Height = 300, Content = timeline, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            Flush(window);
            using var expanded = Capture(timeline);
            SaveCapture(expanded, dark ? "timeline-animation-expanded-dark.png" : "timeline-animation-expanded-light.png");
            ClickExpander(window, timeline, rowId);
            using var collapsed = Capture(timeline);
            SaveCapture(collapsed, dark ? "timeline-animation-collapsed-dark.png" : "timeline-animation-collapsed-light.png");
            var markers = timeline.KeyframeMarkers.OrderBy(marker => marker.Position.X).ToArray();
            Assert.Equal(2, markers.Length);
            Assert.Equal(markers[0].Position.Y, markers[1].Position.Y, 8);
            var lineY = (int)Math.Round(markers[0].Position.Y);
            var firstX = (int)Math.Round(markers[0].Position.X);
            var lastX = (int)Math.Round(markers[1].Position.X);
            var samples = new[] { firstX + 24, firstX + 48, lastX - 24 };
            foreach (var x in samples)
            {
                var line = collapsed.GetPixel(x, lineY);
                var above = collapsed.GetPixel(x, lineY - 8);
                var below = collapsed.GetPixel(x, lineY + 7);
                Assert.True(ColorDistance(line, above) > 60);
                Assert.Equal(above, below);
                Assert.InRange(ColorDistance(line, collapsed.GetPixel(samples[0], lineY)), 0, 8);
            }
            foreach (var marker in markers)
            {
                var x = (int)Math.Round(marker.Position.X);
                var fill = collapsed.GetPixel(x, lineY - 3);
                var background = collapsed.GetPixel(x + 12, lineY - 3);
                Assert.True(ColorDistance(fill, background) > 50);
                Assert.True(ColorDistance(fill, collapsed.GetPixel(samples[0], lineY)) > 20);
            }
            Assert.Equal(40, timeline.GetEffectClipRectangle(rowId)!.Value.Height);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer CreateMaskedLayer(SubtitleLine cue, MaskNode first, MaskNode second)
    {
        return new()
        {
            Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] }
        };
    }

    private static void AcceptCollapseRequests(SubtitleTimelineControl timeline)
    {
        timeline.EffectClipCollapseRequested += (_, e) =>
        {
            var current = timeline.TimelineViewState.ExpandedAnimationClips;
            timeline.TimelineViewState = new()
            {
                ExpandedAnimationClips = e.IsCollapsed ? current.Remove(e.Id) : current.Add(e.Id)
            };
        };
    }

    private static void ClickExpander(Window window, SubtitleTimelineControl timeline, TimelineEffectClipId id)
    {
        var point = timeline.GetEffectClipExpanderRectangle(id)!.Value.Center;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static int ColorDistance(SKColor first, SKColor second)
    {
        return Math.Max(Math.Abs(first.Red - second.Red), Math.Max(Math.Abs(first.Green - second.Green), Math.Abs(first.Blue - second.Blue)));
    }

    private static SKBitmap Capture(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void SaveCapture(SKBitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(directory, name));
        image.SaveTo(stream);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
