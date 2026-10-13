using AegiNext.Application;
using AegiNext.Core.Editing;
using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipKeyframeUiTests
{
    [AvaloniaTheory]
    [InlineData(1, 0)]
    [InlineData(1000, 0)]
    [InlineData(1, -120)]
    [InlineData(1, 120)]
    public async Task RealWorkbenchSelectionAcceptsPointerCaptureAndKeyframeDragMovesOnlyTimeOnce(int seconds, int vertical)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        var cueId = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(8), "动画字幕");
        context.Session.SelectCue(cueId);
        var layer = Assert.Single(context.Session.DocumentSnapshot.Layers);
        context.Session.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(1), 0.25, KeyframeInterpolation.EASE_OUT));
        Assert.True(await context.Window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD));
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var point = timeline.TranslatePoint(timeline.GetKeyframePoint(layer.Id, new(1), 0.25)!.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        var before = context.Session.DocumentSnapshot;

        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        Assert.Equal(new MediaTime(1), context.Session.SelectedKeyTime);
        var destination = point + new Vector(seconds * timeline.PixelsPerSecond, vertical);
        context.Window.MouseMove(destination);
        context.Window.MouseUp(destination, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(timeline.HasActiveDrag);
        var expected = LayerAnimationTiming.ClampTime(layer, new(1 + seconds));
        var key = Assert.Single(Assert.Single(context.Session.DocumentSnapshot.Layers).Tracks[0].Keyframes);
        Assert.Equal(expected, key.Time);
        Assert.Equal(0.25, key.Value.Scalar, 8);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, key.Interpolation);
        Assert.Equal(expected, context.Session.SelectedKeyTime);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(context.Session.DocumentSnapshot));
        Assert.Equal(key, Assert.Single(Assert.Single(reloaded.Layers).Tracks[0].Keyframes));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(-120)]
    [InlineData(120)]
    public async Task VerticalKeyframeDragCreatesNoUndoAndValueIsEditedThroughThePanelTextBox(int vertical)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(8), "Textbox keyframe");
        context.Session.SelectCue(cueId);
        var layer = Assert.Single(context.Session.DocumentSnapshot.Layers);
        var key = new Keyframe(new(1), 0.25, KeyframeInterpolation.EASE_OUT);
        context.Session.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, key);
        var before = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(before);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var point = timeline.TranslatePoint(timeline.GetKeyframePoint(layer.Id, key.Time, key.Value)!.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        context.Window.MouseMove(point + new Vector(0, vertical));
        context.Window.MouseUp(point + new Vector(0, vertical), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(timeline.HasActiveDrag);
        Assert.Equal(key.Time, context.Session.SelectedKeyTime);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        var input = UiTestActions.PropertyInput(context.Window, AnimationProperty.OPACITY);
        input.BringIntoView();
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("0.65");
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsFocused);
        Assert.Equal(0.65, context.Session.PreviewDocument.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        var edited = Assert.Single(context.Session.DocumentSnapshot.Layers[0].Tracks[0].Keyframes);
        Assert.Equal(key with { Value = 0.65 }, edited);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Equal(edited, Assert.Single(context.Session.DocumentSnapshot.Layers[0].Tracks[0].Keyframes));
    }

    [AvaloniaFact]
    public async Task StandardDockActuallyHitsBothTheClipAndItsKeyframeInsideTheAvailableTimelineSpace()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        var cueId = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(8), "动画字幕");
        context.Session.SelectCue(cueId);
        var layer = Assert.Single(context.Window.DocumentSnapshot.Layers);
        context.Session.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(1), 0.25));
        Assert.True(await context.Window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD));
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var clipPoint = timeline.TranslatePoint(timeline.GetClipRectangle(layer.Id)!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(clipPoint));
        context.Window.MouseDown(clipPoint, MouseButton.Left);
        context.Window.MouseUp(clipPoint, MouseButton.Left);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var keyPoint = timeline.TranslatePoint(timeline.GetKeyframePoint(layer.Id, new(1), 0.25)!.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(keyPoint));
        context.Window.MouseDown(keyPoint, MouseButton.Left);
        context.Window.MouseUp(keyPoint, MouseButton.Left);
        Assert.Equal(new MediaTime(1), context.Session.SelectedKeyTime);
        Assert.Equal(0.25m, UiTestActions.PropertyInput(context.Window, AnimationProperty.OPACITY).Value);
    }

    [AvaloniaFact]
    public void SelectingAndMovingClipKeepsItsLocalKeysAndDraggingKeyStopsAtClipEnd()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(1), End = new(4),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25), new(new(2), 0.75)])]
        };
        var editor = new ProjectEditor(new() { Layers = [layer] });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60 };
        var window = new Window { Width = 600, Height = 240, Content = timeline };
        var selected = false;
        var keySelections = 0;
        timeline.SetDocument(editor.Snapshot, null, null);
        timeline.LayerSelected += (_, e) =>
        {
            Assert.Equal(layer.Id, e.Id);
            selected = true;
            timeline.SetDocument(editor.Snapshot, null, Assert.Single(editor.Snapshot.Layers));
        };
        timeline.KeyframeSelected += (_, e) =>
        {
            keySelections++;
            e.SelectionAccepted = true;
        };
        timeline.TimingChanged += (_, e) =>
        {
            editor.ShiftLayer(e.Id, e.Start - e.OriginalStart);
            timeline.SetDocument(editor.Snapshot, null, Assert.Single(editor.Snapshot.Layers));
        };
        timeline.KeyframeMoved += (_, e) =>
        {
            editor.UpdateLayer(e.LayerId, value => value with
            {
                Tracks = [new(e.Property, value.Tracks[0].Keyframes.Select(key =>
                    key.Time == e.OldTime ? key with { Time = e.NewTime, Value = e.NewValue!.Value } : key)
                    .OrderBy(key => key.Time).ToImmutableArray())]
            });
            timeline.SetDocument(editor.Snapshot, null, Assert.Single(editor.Snapshot.Layers));
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var initialFrame = window.CaptureRenderedFrame();
            Assert.NotNull(initialFrame);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value.Center;
            Assert.Same(timeline, window.InputHitTest(clip));
            window.MouseDown(clip, MouseButton.Left);
            window.MouseMove(clip + new Vector(60, 0));
            window.MouseUp(clip + new Vector(60, 0), MouseButton.Left);
            using var movedFrame = window.CaptureRenderedFrame();
            Assert.NotNull(movedFrame);
            Assert.True(selected);
            var moved = Assert.Single(editor.Snapshot.Layers);
            Assert.Equal(new MediaTime(2), moved.Start);
            Assert.Equal(new MediaTime(5), moved.End);
            Assert.Equal(layer.Tracks[0].Keyframes, moved.Tracks[0].Keyframes);

            var keyPoint = timeline.GetKeyframePoint(layer.Id, new(1), 0.25)!.Value;
            Assert.Same(timeline, window.InputHitTest(keyPoint));
            window.MouseDown(keyPoint, MouseButton.Left);
            Assert.Equal(1, keySelections);
            window.MouseMove(keyPoint + new Vector(1000, 0));
            window.MouseUp(keyPoint + new Vector(1000, 0), MouseButton.Left);
            var frames = Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks).Keyframes;
            Assert.Equal(new MediaTime(3), frames[^1].Time);
            Assert.All(frames, key => Assert.True(key.Time >= MediaTime.Zero && key.Time <= new MediaTime(3)));
            Assert.True(editor.Undo());
            Assert.Equal(layer.Tracks[0].Keyframes, Assert.Single(editor.Snapshot.Layers).Tracks[0].Keyframes);
        }
        finally
        {
            window.Close();
        }
    }
}
