using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineEffectRowReorderUiTests
{
    [AvaloniaTheory]
    [InlineData("scalar", false)]
    [InlineData("scalar", true)]
    [InlineData("vector", false)]
    [InlineData("vector", true)]
    [InlineData("color", false)]
    [InlineData("color", true)]
    [InlineData("state", false)]
    [InlineData("state", true)]
    [InlineData("node", false)]
    [InlineData("node", true)]
    [InlineData("instance", false)]
    [InlineData("instance", true)]
    public void EveryLeafKindReordersFromItsActualBodyWithoutChangingNativeData(string kind, bool expanded)
    {
        var document = AllKinds();
        var layer = document.Layers[0];
        var firstRange = document.Subtitles[0].AnimationRanges[0].Id;
        var source = kind switch
        {
            "scalar" => new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY)),
            "vector" => new(layer.Id, new(AnimationProperty.SCALE)),
            "color" => new(layer.Id, new(AnimationProperty.FILL)),
            "state" => new(layer.Id, new(AnimationProperty.FILL, TextRangeId: firstRange, State: SubtitleAnimationState.ACTIVE)),
            "node" => new(layer.Id, new(AnimationProperty.MASK_NODE_POSITION, ((VectorClipMask)layer.Mask!).Contours[0].Nodes[0].Id)),
            _ => InstanceId(layer, 0)
        };
        using var state = new TimelineEffectRowReorderTestState(document, 900);
        if (expanded && source.InstanceId is null)
        {
            state.Timeline.TimelineViewState = document.TimelineViewState with { ExpandedAnimationClips = [source] };
            state.Flush();
        }
        var before = state.VisualOrder(Ids(layer));
        var upward = before[0] != source;
        var target = state.Block(upward ? before[0] : before[^1]);
        var origin = state.Body(source, expanded);
        var destination = new Point(origin.X, upward ? target.Top + 1 : target.Bottom - 1);

        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        Assert.Same(document, state.Editor.Snapshot);
        Assert.NotNull(state.Timeline.EffectInsertionY);
        state.Window.MouseUp(destination, MouseButton.Left);
        state.Flush();

        var expected = before.Where(id => id != source).ToList();
        expected.Insert(upward ? 0 : expected.Count, source);
        Assert.Equal(expected, state.VisualOrder(Ids(layer)));
        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Equal(source, state.LastReorder!.Id);
        Assert.Equal(expected.Select(TimelineEffectRowReorderTestState.Identity), state.Editor.Snapshot.Layers[0].EffectRowOrder);
        Assert.Equal(layer.Tracks, state.Editor.Snapshot.Layers[0].Tracks);
        Assert.Equal(document.Subtitles, state.Editor.Snapshot.Subtitles);
        Assert.Equal(layer.Mask, state.Editor.Snapshot.Layers[0].Mask);
        Assert.True(state.Editor.Undo());
        Assert.Same(document, state.Editor.Snapshot);
        Assert.False(state.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void BodyDragReordersInstancesAcrossDisjointWindowsAndKeepsEveryWindowUnchanged(bool upward, bool disjointWindows)
    {
        var document = Instances(3, disjointWindows);
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, upward ? 0 : 2);
        var target = state.Block(InstanceId(layer, upward ? 2 : 0));
        var origin = state.Body(source);
        var destination = new Point(origin.X, upward ? target.Top + 1 : target.Bottom - 1);
        var indexes = state.Timeline.VisibleClipIndexBuildCount;

        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        state.Flush();
        Assert.True(state.Timeline.HasActiveDrag);
        Assert.NotNull(state.Timeline.EffectInsertionY);
        Assert.Same(document, state.Editor.Snapshot);
        Assert.Equal(indexes, state.Timeline.VisibleClipIndexBuildCount);
        state.Window.MouseUp(destination, MouseButton.Left);
        state.Flush();

        var expected = upward
            ? new[] { InstanceId(layer, 0), InstanceId(layer, 2), InstanceId(layer, 1), new(layer.Id, new(AnimationProperty.OPACITY)) }
            : new[] { InstanceId(layer, 1), InstanceId(layer, 0), InstanceId(layer, 2), new(layer.Id, new(AnimationProperty.OPACITY)) };
        Assert.Equal(expected, state.VisualOrder(Ids(layer)));
        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.All(layer.Effects, instance => Assert.Same(instance, state.Editor.Snapshot.Layers[0].Effects.Single(value => value.Id == instance.Id)));
        Assert.Equal(layer.Tracks, state.Editor.Snapshot.Layers[0].Tracks);
        Assert.Equal(layer.Start, state.Editor.Snapshot.Layers[0].Start);
        Assert.Equal(layer.End, state.Editor.Snapshot.Layers[0].End);
        Assert.True(state.Editor.Undo());
        Assert.Same(document, state.Editor.Snapshot);
        Assert.False(state.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RangePropertyReordersAsOneLeafWhileSiblingPropertiesAndRangeMembershipStayIntact(bool upward)
    {
        var document = Ranges();
        var layer = document.Layers[0];
        var rangeId = document.Subtitles[0].AnimationRanges[0].Id;
        var source = new TimelineEffectClipId(layer.Id, new(AnimationProperty.FONT_SIZE, TextRangeId: rangeId));
        using var state = new TimelineEffectRowReorderTestState(document);
        state.Timeline.TimelineViewState = document.TimelineViewState with { ExpandedAnimationClips = [source] };
        state.Flush();
        var before = state.VisualOrder(Ids(layer));
        var target = state.Block(upward ? before[0] : before[^1]);
        var origin = state.Body(source, curveArea: true);
        var destination = new Point(origin.X, upward ? target.Top + 1 : target.Bottom - 1);

        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        state.Window.MouseUp(destination, MouseButton.Left);
        state.Flush();

        var expected = before.Where(id => id != source).ToList();
        expected.Insert(upward ? 0 : expected.Count, source);
        var after = state.VisualOrder(Ids(layer));
        Assert.Equal(expected, after);
        Assert.Equal(before.Where(id => id != source), after.Where(id => id != source));
        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.False(state.Timeline.IsEffectClipCollapsed(source));
        Assert.Equal(document.Subtitles, state.Editor.Snapshot.Subtitles);
        Assert.Equal(layer.Tracks, state.Editor.Snapshot.Layers[0].Tracks);
        Assert.True(state.Editor.Undo());
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaFact]
    public async Task RealWorkbenchNativeOpacityBodyDragReachesSessionEditorAndOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var document = Native();
        var layer = document.Layers[0];
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        context.Window.Height = 1040;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Flush(context.Window);
        var opacity = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        var scale = new TimelineEffectClipId(layer.Id, new(AnimationProperty.SCALE));
        var source = timeline.GetEffectClipRectangle(opacity)!.Value;
        var target = timeline.GetEffectClipRectangle(scale)!.Value;
        var local = new Point((Math.Max(source.Left, timeline.HeaderWidth) + Math.Min(source.Right, timeline.Bounds.Width)) / 2, source.Top + 10);
        var origin = timeline.TranslatePoint(local, context.Window)!.Value;
        var destination = timeline.TranslatePoint(new(local.X, target.Bottom - 1), context.Window)!.Value;
        var position = context.Session.ProjectPosition;

        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Assert.Same(document, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (ReferenceEquals(document, context.Session.DocumentSnapshot))
        {
            Assert.True(DateTime.UtcNow < deadline, context.DescribeTaskState());
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Flush(context.Window);
        }

        var reordered = context.Session.DocumentSnapshot;
        Assert.Equal(layer.Tracks, reordered.Layers[0].Tracks);
        Assert.Empty(reordered.Layers[0].Effects);
        Assert.Equal([new TimelineEffectRowIdentity(Target: scale.Target), new(Target: opacity.Target)], reordered.Layers[0].EffectRowOrder);
        Assert.Equal(position, context.Session.ProjectPosition);
        Assert.Equal(opacity, timeline.SelectedEffectClipId);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(reordered, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task RealWorkbenchBodyReorderChangesNativeAndScriptCompositionWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var layer = Instances(1).Layers[0] with
        {
            Transform = new() { Position = new(2, 0) },
            Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(10, 0))])]
        };
        var document = new ProjectDocument { Layers = [layer] };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        context.Window.Height = 1040;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Flush(context.Window);
        var native = new TimelineEffectClipId(layer.Id, new(AnimationProperty.POSITION));
        var script = InstanceId(layer, 0);
        Assert.Equal(20, SceneEvaluator.EvaluateLayer(layer, null, new(1)).Transform.Position.X);
        var source = timeline.GetEffectClipRectangle(native)!.Value;
        var target = timeline.GetEffectClipRectangle(script)!.Value;
        var local = new Point((Math.Max(source.Left, timeline.HeaderWidth) + Math.Min(source.Right, timeline.Bounds.Width)) / 2, source.Top + 10);
        var origin = timeline.TranslatePoint(local, context.Window)!.Value;
        var destination = timeline.TranslatePoint(new(local.X, target.Top + 1), context.Window)!.Value;

        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Assert.Same(document, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (ReferenceEquals(document, context.Session.DocumentSnapshot))
        {
            Assert.True(DateTime.UtcNow < deadline, context.DescribeTaskState());
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Flush(context.Window);
        }

        var reordered = context.Session.DocumentSnapshot;
        Assert.Equal(10, SceneEvaluator.EvaluateLayer(reordered.Layers[0], null, new(1)).Transform.Position.X);
        Assert.Equal(layer.Tracks, reordered.Layers[0].Tracks);
        Assert.Equal(layer.Effects, reordered.Layers[0].Effects);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.Equal(20, SceneEvaluator.EvaluateLayer(context.Session.DocumentSnapshot.Layers[0], null, new(1)).Transform.Position.X);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("move")]
    [InlineData("start")]
    [InlineData("end")]
    public void HorizontalAxisLocksInstanceMoveOrTrimEvenAfterLargeVerticalMotion(string mode)
    {
        var document = TimedInstance();
        var layer = document.Layers[0];
        var instance = layer.Effects[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var id = InstanceId(layer, 0);
        var block = state.Block(id);
        var origin = mode == "start" ? new Point(block.Left + 2, block.Top + 10) :
            mode == "end" ? new(block.Right - 2, block.Top + 10) : state.Body(id);
        var sign = mode == "end" ? -1 : 1;

        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(sign * 8, 0));
        var destination = origin + new Vector(sign * 60, -100);
        state.Window.MouseMove(destination);
        Assert.Same(document, state.Editor.Snapshot);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(1, state.TimingRequests);
        var result = state.Editor.Snapshot.Layers[0].Effects.Single(value => value.Id == instance.Id);
        Assert.Equal(instance.Start + (mode == "end" ? MediaTime.Zero : new(1)), result.Start);
        Assert.Equal(instance.End + (mode == "move" ? new(1) : mode == "end" ? new(-1) : MediaTime.Zero), result.End);
        Assert.Empty(state.Editor.Snapshot.Layers[0].EffectRowOrder);
        Assert.True(state.Editor.Undo());
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaTheory]
    [InlineData("move")]
    [InlineData("start")]
    [InlineData("end")]
    public void SelectingAnotherRowCancelsALockedInstanceMoveOrTrimWithoutCommitting(string mode)
    {
        var document = TimedInstance();
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, 0);
        var other = InstanceId(layer, 1);
        var block = state.Block(source);
        var otherBlock = state.Block(other);
        var origin = mode == "start" ? new Point(block.Left + 2, block.Top + 10) :
            mode == "end" ? new(block.Right - 2, block.Top + 10) : state.Body(source);
        var sign = mode == "end" ? -1 : 1;
        var destination = origin + new Vector(sign * 60, 0);

        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(sign * 8, 0));
        state.Window.MouseMove(destination);
        Assert.True(state.Timeline.HasActiveDrag);
        Assert.NotEqual(block, state.Block(source));
        Assert.Same(document, state.Editor.Snapshot);

        state.Timeline.SelectedEffectClipId = other;

        Assert.False(state.Timeline.HasActiveDrag);
        state.Window.MouseUp(destination, MouseButton.Left);
        state.Flush();

        Assert.Equal(other, state.Timeline.SelectedEffectClipId);
        Assert.Equal(0, state.TimingRequests);
        Assert.Equal(0, state.ReorderRequests);
        Assert.False(state.Timeline.HasActiveDrag);
        Assert.Null(state.Timeline.EffectInsertionY);
        Assert.Equal(block, state.Block(source));
        Assert.Equal(otherBlock, state.Block(other));
        Assert.Same(document, state.Editor.Snapshot);
        Assert.Same(layer, state.Editor.Snapshot.Layers[0]);
        Assert.False(state.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void NativeHorizontalAxisStaysSelectionOnlyAndCannotTurnIntoReorder()
    {
        var document = Native();
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = new TimelineEffectClipId(document.Layers[0].Id, new(AnimationProperty.OPACITY));
        var origin = state.Body(source);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(8, 0));
        state.Window.MouseMove(origin + new Vector(60, 80));
        state.Window.MouseUp(origin + new Vector(60, 80), MouseButton.Left);

        Assert.Equal(source, state.Timeline.SelectedEffectClipId);
        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Same(document, state.Editor.Snapshot);
        Assert.False(state.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmbiguousDiagonalWaitsUntilOneAxisWins(bool vertical)
    {
        var document = TimedInstance();
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, 0);
        var origin = state.Body(source);
        var originalBlock = state.Block(source);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(6, 6));
        Assert.Null(state.Timeline.EffectInsertionY);
        Assert.Equal(originalBlock, state.Block(source));
        Assert.Same(document, state.Editor.Snapshot);
        var destination = vertical ? new Point(origin.X + 6, state.Block(InstanceId(layer, 2)).Top + 1) : origin + new Vector(60, 6);
        state.Window.MouseMove(destination);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.Equal(vertical ? 1 : 0, state.ReorderRequests);
        Assert.Equal(vertical ? 0 : 1, state.TimingRequests);
        Assert.True(state.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void VerticalAxisCannotTurnIntoTimingAfterTheHorizontalDistanceBecomesLarger()
    {
        var document = TimedInstance();
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, 0);
        var origin = state.Body(source);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(0, -8));
        var destination = new Point(origin.X + 180, state.Block(InstanceId(layer, 1)).Top + 1);
        state.Window.MouseMove(destination);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.All(layer.Effects, instance => Assert.Same(instance, state.Editor.Snapshot.Layers[0].Effects.Single(value => value.Id == instance.Id)));
    }

    [AvaloniaTheory]
    [InlineData("click", false)]
    [InlineData("threshold", false)]
    [InlineData("diagonal", false)]
    [InlineData("return", true)]
    [InlineData("escape", false)]
    [InlineData("escape", true)]
    [InlineData("capture", false)]
    [InlineData("capture", true)]
    [InlineData("replacement", false)]
    [InlineData("replacement", true)]
    [InlineData("selection", false)]
    [InlineData("selection", true)]
    [InlineData("detach", false)]
    [InlineData("detach", true)]
    public void PendingAndLockedCancelledGesturesNeverCommitOrFallThrough(string mode, bool locked)
    {
        var document = Instances(3);
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, 0);
        var origin = state.Body(source);
        var destination = locked ? new Point(origin.X, state.Block(InstanceId(layer, 2)).Top + 1) :
            origin + (mode == "diagonal" ? new Vector(6, 6) : mode == "threshold" ? new(2, 2) : default);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        switch (mode)
        {
            case "return":
                state.Window.MouseMove(origin);
                destination = origin;
                break;
            case "escape":
                UiTestActions.Press(state.Window, Key.Escape);
                break;
            case "capture":
                Assert.NotNull(state.Pointer);
                state.Pointer.Capture(null);
                break;
            case "replacement":
                state.Bind(document with { Name = "Replacement" });
                break;
            case "selection":
                state.Timeline.SelectedEffectClipId = InstanceId(layer, 1);
                break;
            case "detach":
                state.Window.Content = null;
                break;
        }
        state.Window.MouseUp(destination, MouseButton.Left);
        state.Flush();

        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.False(state.Timeline.HasActiveDrag);
        Assert.Null(state.Timeline.EffectInsertionY);
        Assert.Same(document, state.Editor.Snapshot);
        Assert.False(state.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedSelectionCannotStartBodyGesture(bool vertical)
    {
        var document = Instances(3);
        using var state = new TimelineEffectRowReorderTestState(document) { AcceptSelection = false };
        var origin = state.Body(InstanceId(document.Layers[0], 0));
        var destination = origin + (vertical ? new Vector(0, -80) : new(60, 0));
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.False(state.Timeline.HasActiveDrag);
        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaTheory]
    [InlineData("collapsed")]
    [InlineData("expanded")]
    [InlineData("ordered")]
    public void ActualKeyframeAndOrderedEndpointHitsKeepPriorityOverBodyReorder(string mode)
    {
        var document = Native();
        var layer = document.Layers[0];
        var target = new AnimationTrackTarget(mode == "ordered" ? AnimationProperty.OPACITY : AnimationProperty.SCALE);
        if (mode == "ordered")
        {
            layer = layer with
            {
                Tracks = layer.Tracks.Where(track => track.Target != target).Append(new AnimationTrack(target, [])
                {
                    InitialValue = 0.2d, Transforms = [new(Guid.NewGuid(), new(1), new(2), 0.8d)]
                }).ToImmutableArray()
            };
            document = document with { Layers = [layer] };
        }
        using var state = new TimelineEffectRowReorderTestState(document);
        if (mode != "collapsed")
        {
            state.Timeline.TimelineViewState = document.TimelineViewState with { ExpandedAnimationClips = [new(layer.Id, target)] };
            state.Flush();
        }
        var marker = state.Timeline.KeyframeMarkers.First(item => item.Identity.LayerId == layer.Id && item.Identity.Target == target);
        var origin = marker.Position;
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(0, -80));
        state.Window.MouseUp(origin + new Vector(0, -80), MouseButton.Left);

        Assert.Equal(1, state.KeyframeSelections);
        Assert.Equal(0, state.KeyframeMoves);
        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaFact]
    public void EntireExpanderHitAreaConsumesDragWithoutStartingBodyGesture()
    {
        var document = Native();
        using var state = new TimelineEffectRowReorderTestState(document);
        var id = new TimelineEffectClipId(document.Layers[0].Id, new(AnimationProperty.OPACITY));
        var button = state.Timeline.GetEffectClipExpanderRectangle(id)!.Value;
        var origin = new Point(button.Right - 1, button.Bottom - 1);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(80, 80));
        state.Window.MouseUp(origin + new Vector(80, 80), MouseButton.Left);

        Assert.Equal(1, state.CollapseRequests);
        Assert.Equal(0, state.SelectionRequests);
        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.False(state.Timeline.IsEffectClipCollapsed(id));
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaFact]
    public void OnePixelWideInstanceStillReordersVerticallyFromItsBody()
    {
        var document = Instances(2);
        var layer = document.Layers[0];
        layer = layer with { Effects = layer.Effects.SetItem(0, layer.Effects[0] with { End = new(1, 1000) }) };
        document = document with { Layers = [layer] };
        using var state = new TimelineEffectRowReorderTestState(document);
        var source = InstanceId(layer, 0);
        Assert.Equal(1, state.Block(source).Width);
        var origin = state.Body(source);
        var destination = new Point(origin.X, state.Block(InstanceId(layer, 1)).Top + 1);
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(destination);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Equal(new MediaTime(1, 1000), state.Editor.Snapshot.Layers[0].Effects.Single(instance => instance.Id == source.InstanceId).End);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodyReorderRejectsAnotherParentHorizontallyOrVertically(bool otherTrack)
    {
        var document = Instances(3);
        var layer = document.Layers[0];
        var track = new ProjectTrack { Name = "Other" };
        var other = Instances(3).Layers[0] with { TrackId = otherTrack ? track.Id : layer.TrackId, Start = otherTrack ? new(0) : new(8), End = otherTrack ? new(8) : new(16) };
        document = document with { Tracks = document.Tracks.Add(track), Layers = [layer, other] };
        using var state = new TimelineEffectRowReorderTestState(document, 800);
        var origin = state.Body(InstanceId(layer, 0));
        var destination = state.Body(InstanceId(other, 1));
        state.Window.MouseDown(origin, MouseButton.Left);
        state.Window.MouseMove(origin + new Vector(0, -8));
        state.Window.MouseMove(destination);
        Assert.Null(state.Timeline.EffectInsertionY);
        state.Window.MouseUp(destination, MouseButton.Left);

        Assert.Equal(0, state.ReorderRequests);
        Assert.Equal(0, state.TimingRequests);
        Assert.Same(document, state.Editor.Snapshot);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BodyReorderAutoScrollsTheFullParentStackAndStopsAfterRelease(bool upward)
    {
        var document = Instances(24);
        var layer = document.Layers[0];
        using var state = new TimelineEffectRowReorderTestState(document, 180);
        if (upward)
        {
            state.Timeline.SetViewport(state.Timeline.Viewport with { VerticalOffset = state.Timeline.ContentHeight }, 8);
        }
        var source = InstanceId(layer, upward ? 0 : 23);
        var origin = state.Body(source);
        var destination = new Point(origin.X, upward ? state.Timeline.RulerHeight - 5 : state.Timeline.Bounds.Height + 5);
        var beforeOffset = state.Timeline.Viewport.VerticalOffset;
        state.Window.MouseDown(origin, MouseButton.Left);
        var indexes = state.Timeline.VisibleClipIndexBuildCount;
        state.Window.MouseMove(destination);
        var deadline = DateTime.UtcNow.AddSeconds(6);
        var lastOffset = -1d;
        var unchanged = 0;
        while (unchanged < 3)
        {
            Assert.True(DateTime.UtcNow < deadline, "Attached stack scrolling did not settle.");
            await Task.Delay(25, TestContext.Current.CancellationToken);
            state.Flush();
            var offset = state.Timeline.Viewport.VerticalOffset;
            unchanged = offset == lastOffset ? unchanged + 1 : 0;
            lastOffset = offset;
            Assert.Equal(indexes, state.Timeline.VisibleClipIndexBuildCount);
        }
        Assert.True(upward ? lastOffset < beforeOffset : lastOffset > beforeOffset);
        Assert.NotNull(state.Timeline.EffectInsertionY);
        Assert.Same(document, state.Editor.Snapshot);
        state.Window.MouseUp(destination, MouseButton.Left);
        Assert.Equal(1, state.ReorderRequests);
        Assert.Equal(source, state.VisualOrder(Ids(layer))[upward ? 0 : ^1]);
        var released = state.Timeline.Viewport.VerticalOffset;
        await Task.Delay(80, TestContext.Current.CancellationToken);
        state.Flush();
        Assert.Equal(released, state.Timeline.Viewport.VerticalOffset);
    }

    private static IEnumerable<TimelineEffectClipId> Ids(ProjectLayer layer) =>
        layer.Tracks.Select(track => new TimelineEffectClipId(layer.Id, track.Target))
            .Concat(layer.Effects.Select(instance => new TimelineEffectClipId(layer.Id, instance.TargetContext ?? new(AnimationProperty.OPACITY), instance.Id)));

    private static TimelineEffectClipId InstanceId(ProjectLayer layer, int index) => new(layer.Id, new(AnimationProperty.OPACITY), layer.Effects[index].Id);

    private static ProjectDocument TimedInstance()
    {
        var document = Instances(3);
        var layer = document.Layers[0];
        return document with { Layers = [layer with { Effects = layer.Effects.SetItem(0, layer.Effects[0] with { Start = new(1), End = new(3) }) }] };
    }

    private static ProjectDocument Native() => new()
    {
        Layers = [new()
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(8),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5d)]), new(AnimationProperty.SCALE, [new(new(1), new ScenePoint(1, 2))])]
        }]
    };

    private static ProjectDocument Instances(int count, bool disjoint = false)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(8),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5d)])],
            Effects = Enumerable.Range(0, count).Select(index => new EffectInstance
            {
                Name = "Instance " + index, Start = disjoint ? new(index * 2) : new(0), End = disjoint ? new(index * 2 + 1) : new(8),
                Source = "effect \"row-order\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\nat 0 position offset(10, 0)\nat 1 position offset(10, 0)\nend\nend"
            }).ToImmutableArray()
        };
        return new() { Layers = [layer] };
    }

    private static ProjectDocument Ranges()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var hidden = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "ABCD", End = new(8), AnimationRanges = [first, hidden, second] };
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Tracks =
                [
                    new(AnimationProperty.OPACITY, [new(new(0), 0.5d)]),
                    new(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: first.Id), [new(new(0), 30d)]),
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id), [new(new(0), SceneColor.White)]),
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id, State: SubtitleAnimationState.ACTIVE), [new(new(0), SceneColor.White)]),
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id, State: SubtitleAnimationState.INACTIVE), [new(new(0), SceneColor.White)]),
                    new(new AnimationTrackTarget(AnimationProperty.SCALE, TextRangeId: second.Id), [new(new(0), new ScenePoint(2, 2))])
                ]
            }]
        };
    }

    private static ProjectDocument AllKinds()
    {
        var document = Ranges();
        var layer = document.Layers[0];
        var node = new MaskNode { Position = new(20, 30) };
        return document with
        {
            Layers = [layer with
            {
                Mask = new VectorClipMask { Contours = [new() { Nodes = [node, new() { Position = new(100, 50) }] }] },
                Effects = Instances(2).Layers[0].Effects,
                Tracks = [.. layer.Tracks,
                    new(AnimationProperty.SCALE, [new(new(1), new ScenePoint(1, 2))]),
                    new(AnimationProperty.FILL, [new(new(1), SceneColor.White)]),
                    new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), [new(new(1), node.Position)])]
            }]
        };
    }

    private static void Flush(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
