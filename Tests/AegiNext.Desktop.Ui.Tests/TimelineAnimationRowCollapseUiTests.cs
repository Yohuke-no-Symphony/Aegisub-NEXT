using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineAnimationRowCollapseUiTests
{
    [AvaloniaTheory]
    [InlineData(AnimationProperty.OPACITY)]
    [InlineData(AnimationProperty.SCALE)]
    [InlineData(AnimationProperty.FILL)]
    public void PointerCollapseUsesACompactHorizontalLineAndExpandingRestoresEveryComponentPosition(AnimationProperty property)
    {
        var firstValue = CreateValue(property);
        var secondValue = CreateValue(property, true);
        var layer = CreateLayer(property, [new(new(1), firstValue), new(new(3), secondValue)]);
        var document = new ProjectDocument { Layers = [layer] };
        var rowId = new TimelineEffectClipId(layer.Id, new(property));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(document, null, layer);
        timeline.TimelineViewState = TimelineEffectClipTestState.ExpandAll(document);
        var requests = new List<TimelineEffectClipCollapseEventArgs>();
        AcceptCollapseRequests(timeline, requests);
        var unrelatedEvents = 0;
        timeline.SeekRequested += (_, _) => unrelatedEvents++;
        timeline.CueSelected += (_, _) => unrelatedEvents++;
        timeline.LayerSelected += (_, _) => unrelatedEvents++;
        timeline.ClipSelectionChanged += (_, _) => unrelatedEvents++;
        timeline.TrackSelected += (_, _) => unrelatedEvents++;
        timeline.TimingChanged += (_, _) => unrelatedEvents++;
        timeline.KeyframeSelected += (_, _) => unrelatedEvents++;
        var window = new Window { Width = 850, Height = 420, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var expandedRectangle = timeline.GetEffectClipRectangle(rowId)!.Value;
            var expandedPoints = layer.Tracks[0].Keyframes.SelectMany(key =>
                Enumerable.Range(0, key.Value.ComponentCount).Select(component =>
                    timeline.GetKeyframePoint(layer.Id, property, key.Time, key.Value, component)!.Value)).ToArray();
            var expandedContentHeight = timeline.ContentHeight;

            ClickExpander(window, timeline, rowId);

            var request = Assert.Single(requests);
            Assert.Equal(rowId, request.Id);
            Assert.True(request.IsCollapsed);
            Assert.True(timeline.IsEffectClipCollapsed(rowId));
            var collapsedRectangle = timeline.GetEffectClipRectangle(rowId)!.Value;
            Assert.Equal(40, collapsedRectangle.Height);
            Assert.True(collapsedRectangle.Height < expandedRectangle.Height);
            Assert.Equal(expandedContentHeight - expandedRectangle.Height + 40, timeline.ContentHeight, 8);
            var collapsedPoints = layer.Tracks[0].Keyframes.SelectMany(key =>
                Enumerable.Range(0, key.Value.ComponentCount).Select(component =>
                    timeline.GetKeyframePoint(layer.Id, property, key.Time, key.Value, component)!.Value)).ToArray();
            Assert.All(collapsedPoints, point =>
            {
                Assert.Equal(collapsedPoints[0].Y, point.Y, 8);
                Assert.InRange(point.Y, collapsedRectangle.Top, collapsedRectangle.Bottom);
            });
            for (var index = 0; index < expandedPoints.Length; index++)
            {
                Assert.Equal(expandedPoints[index].X, collapsedPoints[index].X, 8);
            }
            Assert.Equal(2, timeline.KeyframeMarkers.Count);
            Assert.All(timeline.KeyframeMarkers, marker =>
                Assert.Equal(AllComponents(firstValue), marker.Components));
            Assert.Equal(new[] { property }, timeline.GetAnimationProperties(layer.Id));
            Assert.False(timeline.HasActiveDrag);
            Assert.Equal(0, unrelatedEvents);
            Assert.Empty(document.TimelineViewState.ExpandedAnimationClips);

            ClickExpander(window, timeline, rowId);

            Assert.Equal(2, requests.Count);
            Assert.Equal(rowId, requests[1].Id);
            Assert.False(requests[1].IsCollapsed);
            Assert.False(timeline.IsEffectClipCollapsed(rowId));
            Assert.Equal(expandedRectangle, timeline.GetEffectClipRectangle(rowId));
            var restoredPoints = layer.Tracks[0].Keyframes.SelectMany(key =>
                Enumerable.Range(0, key.Value.ComponentCount).Select(component =>
                    timeline.GetKeyframePoint(layer.Id, property, key.Time, key.Value, component)!.Value)).ToArray();
            Assert.Equal(expandedPoints, restoredPoints);
            Assert.Equal(expandedContentHeight, timeline.ContentHeight, 8);
            Assert.Equal(0, unrelatedEvents);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PointerCollapseOnlyRequestsTheStateAndWaitsForItsConsumerToAcceptIt()
    {
        var layer = CreateLayer(AnimationProperty.OPACITY, [new(new(1), 0.25)]);
        var rowId = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [rowId] };
        TimelineEffectClipCollapseEventArgs? request = null;
        timeline.EffectClipCollapseRequested += (_, e) => request = e;
        var window = new Window { Width = 700, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var originalRectangle = timeline.GetEffectClipRectangle(rowId)!.Value;

            ClickExpander(window, timeline, rowId);

            Assert.NotNull(request);
            Assert.Equal(rowId, request.Id);
            Assert.True(request.IsCollapsed);
            Assert.False(timeline.IsEffectClipCollapsed(rowId));
            Assert.Equal(originalRectangle, timeline.GetEffectClipRectangle(rowId));
            Assert.Equal(rowId, Assert.Single(timeline.TimelineViewState.ExpandedAnimationClips));

            timeline.TimelineViewState = new();
            Prepare(window);

            Assert.True(timeline.IsEffectClipCollapsed(rowId));
            Assert.Equal(40, timeline.GetEffectClipRectangle(rowId)!.Value.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BlocksAreIndependentAndSequentialClipsReuseTheMaximumStackHeight()
    {
        var first = CreateLayer(AnimationProperty.OPACITY, [new(new(1), 0.25)]) with
        {
            Start = new(0), End = new(3),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25)]), new(AnimationProperty.ROTATION, [new(new(1), 40)])]
        };
        var second = CreateLayer(AnimationProperty.OPACITY, [new(new(1), 0.75)]) with { Start = new(4), End = new(7) };
        var firstId = new TimelineEffectClipId(first.Id, new(AnimationProperty.OPACITY));
        var secondId = new TimelineEffectClipId(second.Id, new(AnimationProperty.OPACITY));
        var rotationId = new TimelineEffectClipId(first.Id, new(AnimationProperty.ROTATION));
        var document = new ProjectDocument { Layers = [first, second] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 70 };
        timeline.SetDocument(document, null, null);
        var requests = new List<TimelineEffectClipCollapseEventArgs>();
        AcceptCollapseRequests(timeline, requests);
        var window = new Window { Width = 900, Height = 500, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var compactHeight = timeline.ContentHeight;
            Assert.True(timeline.IsEffectClipCollapsed(firstId));
            Assert.True(timeline.IsEffectClipCollapsed(secondId));
            var firstBlock = timeline.GetEffectClipRectangle(firstId)!.Value;
            var secondBlock = timeline.GetEffectClipRectangle(secondId)!.Value;
            Assert.Equal(timeline.GetClipRectangle(first.Id)!.Value.Width, firstBlock.Width);
            Assert.Equal(timeline.GetClipRectangle(second.Id)!.Value.Left, secondBlock.Left);
            Assert.True(firstBlock.Bottom < secondBlock.Bottom);

            ClickExpander(window, timeline, secondId);

            Assert.False(timeline.IsEffectClipCollapsed(secondId));
            Assert.True(timeline.IsEffectClipCollapsed(firstId));
            Assert.True(timeline.IsEffectClipCollapsed(rotationId));
            Assert.Equal(76, timeline.GetEffectClipRectangle(secondId)!.Value.Height);
            Assert.Equal(compactHeight, timeline.ContentHeight);

            ClickExpander(window, timeline, firstId);

            Assert.Equal(compactHeight + 36, timeline.ContentHeight);
            Assert.Equal(40, timeline.GetEffectClipRectangle(rotationId)!.Value.Height);
            timeline.ToggleTrackCollapse(first.TrackId);
            Prepare(window);
            Assert.Null(timeline.GetEffectClipRectangle(firstId));
            Assert.Null(timeline.GetEffectClipRectangle(secondId));
            Assert.False(timeline.IsEffectClipCollapsed(firstId));
            Assert.False(timeline.IsEffectClipCollapsed(secondId));
            timeline.ToggleTrackCollapse(first.TrackId);
            Prepare(window);
            Assert.Equal(76, timeline.GetEffectClipRectangle(firstId)!.Value.Height);
            Assert.Equal(76, timeline.GetEffectClipRectangle(secondId)!.Value.Height);
            Assert.Equal(2, requests.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CollapsingATrackPreservesItsClipAnimationRowState()
    {
        var child = CreateLayer(AnimationProperty.OPACITY, [new(new(1), 0.25)]);
        var rowId = new TimelineEffectClipId(child.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 70 };
        timeline.SetDocument(new() { Layers = [child] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [rowId] };
        var requests = new List<TimelineEffectClipCollapseEventArgs>();
        AcceptCollapseRequests(timeline, requests);
        var window = new Window { Width = 800, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            ClickExpander(window, timeline, rowId);
            var groupExpander = timeline.GetTrackExpanderRectangle(child.TrackId)!.Value.Center;
            Assert.Same(timeline, window.InputHitTest(groupExpander));
            window.MouseDown(groupExpander, MouseButton.Left);
            window.MouseUp(groupExpander, MouseButton.Left);
            Prepare(window);

            Assert.NotNull(timeline.GetClipRectangle(child.Id));
            Assert.Null(timeline.GetEffectClipRectangle(rowId));
            Assert.True(timeline.IsEffectClipCollapsed(rowId));
            Assert.Empty(timeline.TimelineViewState.ExpandedAnimationClips);

            window.MouseDown(groupExpander, MouseButton.Left);
            window.MouseUp(groupExpander, MouseButton.Left);
            Prepare(window);

            Assert.NotNull(timeline.GetClipRectangle(child.Id));
            Assert.Equal(40, timeline.GetEffectClipRectangle(rowId)!.Value.Height);
            Assert.NotNull(timeline.GetKeyframePoint(child.Id, AnimationProperty.OPACITY, new(1), 0.25));
            Assert.Single(requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(AnimationProperty.OPACITY)]
    [InlineData(AnimationProperty.SCALE)]
    [InlineData(AnimationProperty.FILL)]
    public void CollapsedMarkerVerticalDragDoesNotEditAndDiagonalDragMovesOnlyTimeInOneUndo(AnimationProperty property)
    {
        var value = CreateValue(property);
        var key = new Keyframe(new(1), value, KeyframeInterpolation.EASE_OUT);
        var layer = CreateLayer(property, [key]);
        var source = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(source);
        var rowId = new TimelineEffectClipId(layer.Id, new(property));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(source, null, layer);
        timeline.TimelineViewState = TimelineEffectClipTestState.ExpandAll(source);
        AcceptCollapseRequests(timeline, []);
        var edits = 0;
        timeline.KeyframeSelected += (_, e) =>
        {
            Assert.Equal(AllComponents(value), e.Components);
            e.SelectionAccepted = true;
        };
        timeline.KeyframeMoved += (_, e) =>
        {
            edits++;
            Assert.Equal(value, e.NewValue);
            Assert.Equal(AllComponents(value), e.Components);
            editor.UpdateLayer(e.LayerId, current => current with
            {
                Tracks = [new(property, [key with { Time = e.NewTime, Value = e.NewValue!.Value }])]
            });
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers[0]);
        };
        var window = new Window { Width = 800, Height = 400, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            ClickExpander(window, timeline, rowId);
            var point = timeline.GetKeyframePoint(layer.Id, property, key.Time, value)!.Value;
            var marker = Assert.Single(timeline.KeyframeMarkers);
            Assert.Same(timeline, window.InputHitTest(point));
            foreach (var vertical in new[] { -100, 100 })
            {
                window.MouseDown(point, MouseButton.Left);
                Assert.True(timeline.HasActiveDrag);
                window.MouseMove(point + new Vector(0, vertical));
                Assert.Equal(marker, Assert.Single(timeline.KeyframeMarkers));
                window.MouseUp(point + new Vector(0, vertical), MouseButton.Left);
                Assert.False(timeline.HasActiveDrag);
                Assert.Equal(0, edits);
                Assert.Same(source, editor.Snapshot);
                Assert.False(editor.CanUndo);
            }

            var destination = point + new Vector(80, -100);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(destination);
            var preview = Assert.Single(timeline.KeyframeMarkers);
            Assert.Equal(new MediaTime(2), preview.Identity.Time);
            Assert.Equal(value, preview.Value);
            Assert.Equal(point.Y, preview.Position.Y, 8);
            Assert.Same(source, editor.Snapshot);
            window.MouseUp(destination, MouseButton.Left);

            Assert.Equal(1, edits);
            Assert.Equal(key with { Time = new(2) }, Assert.Single(editor.Snapshot.Layers[0].Tracks[0].Keyframes));
            Assert.True(timeline.IsEffectClipCollapsed(rowId));
            Assert.True(editor.Undo());
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers[0]);
            Assert.Same(source, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.True(editor.CanRedo);
            Assert.Equal(40, timeline.GetEffectClipRectangle(rowId)!.Value.Height);
            Assert.Equal(point, timeline.GetKeyframePoint(layer.Id, property, key.Time, value));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipEdgesAtTheCollapsedKeyTimesStillTrimTheClipInsteadOfDraggingAKey(bool rightEdge)
    {
        var layer = CreateLayer(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.25), new(new(3), 0.75)]) with
        {
            Start = new(1), End = new(4)
        };
        var rowId = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [rowId] };
        AcceptCollapseRequests(timeline, []);
        var timing = new List<TimelineTimingEventArgs>();
        var keyEvents = 0;
        var seeks = 0;
        timeline.TimingChanged += (_, e) => timing.Add(e);
        timeline.KeyframeSelected += (_, _) => keyEvents++;
        timeline.KeyframeMoved += (_, _) => keyEvents++;
        timeline.SeekRequested += (_, _) => seeks++;
        var window = new Window { Width = 800, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            ClickExpander(window, timeline, rowId);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var key = layer.Tracks[0].Keyframes[rightEdge ? 1 : 0];
            var marker = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, key.Time, key.Value)!.Value;
            Assert.Equal(rightEdge ? clip.Right : clip.Left, marker.X, 8);
            var origin = new Point(marker.X + (rightEdge ? -2 : 2), clip.Center.Y);
            var destination = origin + new Vector(rightEdge ? -80 : 80, 0);
            Assert.Same(timeline, window.InputHitTest(origin));
            window.MouseDown(origin, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(destination);
            window.MouseUp(destination, MouseButton.Left);

            var changed = Assert.Single(timing);
            Assert.Equal(layer.Id, changed.Id);
            Assert.False(changed.IsMove);
            Assert.Equal(new MediaTime(rightEdge ? 1 : 2), changed.Start);
            Assert.Equal(new MediaTime(rightEdge ? 3 : 4), changed.End);
            Assert.Equal(0, keyEvents);
            Assert.Equal(0, seeks);
            Assert.False(timeline.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExternalViewStateChangesClearHoverAndCancelAnUncommittedPointerGesture()
    {
        var layer = CreateLayer(AnimationProperty.OPACITY, [new(new(1), 0.25)]);
        var document = new ProjectDocument { Layers = [layer] };
        var rowId = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(document, null, layer);
        timeline.TimelineViewState = TimelineEffectClipTestState.ExpandAll(document);
        var requests = new List<TimelineEffectClipCollapseEventArgs>();
        AcceptCollapseRequests(timeline, requests);
        var edits = 0;
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, _) => edits++;
        var window = new Window { Width = 800, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            ClickExpander(window, timeline, rowId);
            var collapsed = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
            window.MouseMove(collapsed);
            Assert.NotNull(timeline.HoveredKeyframe);

            timeline.TimelineViewState = new() { ExpandedAnimationClips = [rowId] };
            Prepare(window);

            Assert.Null(timeline.HoveredKeyframe);
            Assert.Null(timeline.GetKeyframeLabelRectangle(layer.Id, AnimationProperty.OPACITY, new(1)));
            var expanded = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
            window.MouseMove(expanded);
            Assert.NotNull(timeline.HoveredKeyframe);
            window.MouseDown(expanded, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(expanded + new Vector(40, 0));

            timeline.TimelineViewState = new();
            Prepare(window);

            Assert.False(timeline.HasActiveDrag);
            Assert.Null(timeline.HoveredKeyframe);
            window.MouseUp(expanded + new Vector(40, 0), MouseButton.Left);
            Assert.Equal(0, edits);
            Assert.Single(requests);
            Assert.Equal(40, timeline.GetEffectClipRectangle(rowId)!.Value.Height);
            Assert.Equal(collapsed, timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(1), 0.25));
            Assert.Equal(new MediaTime(1), Assert.Single(layer.Tracks[0].Keyframes).Time);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer CreateLayer(AnimationProperty property, Keyframe[] keys)
    {
        return new()
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(1), End = new(7),
            Tracks = [new(property, [.. keys])]
        };
    }

    private static ProjectLayer CreateSubtitleLayer(SubtitleLine cue)
    {
        return new()
        {
            Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
        };
    }

    private static AnimationValue CreateValue(AnimationProperty property, bool second = false)
    {
        return property switch
        {
            AnimationProperty.OPACITY => AnimationValue.FromScalar(second ? 0.75 : 0.25),
            AnimationProperty.SCALE => AnimationValue.FromVector(second ? new(3, 4) : new(1, 2)),
            _ => AnimationValue.FromColor(second ? new(1, 2, 3, 0.9) : new(4, 0.25, 2, 0.5))
        };
    }

    private static TimelineComponentMask AllComponents(AnimationValue value)
    {
        return (TimelineComponentMask)((1 << value.ComponentCount) - 1);
    }

    private static void AcceptCollapseRequests(SubtitleTimelineControl timeline, List<TimelineEffectClipCollapseEventArgs> requests)
    {
        timeline.EffectClipCollapseRequested += (_, e) =>
        {
            requests.Add(e);
            var current = timeline.TimelineViewState.ExpandedAnimationClips;
            timeline.TimelineViewState = timeline.TimelineViewState with
            {
                ExpandedAnimationClips = e.IsCollapsed ? current.Remove(e.Id) : current.Add(e.Id)
            };
        };
    }

    private static void ClickExpander(Window window, SubtitleTimelineControl timeline, TimelineEffectClipId rowId)
    {
        var point = timeline.GetEffectClipExpanderRectangle(rowId)!.Value.Center;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Prepare(window);
    }

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
