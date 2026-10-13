using AegiNext.Application;
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

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineExpandedKeyframeUiTests
{
    [AvaloniaTheory]
    [InlineData(AnimationProperty.OPACITY, 0, 2, -1)]
    [InlineData(AnimationProperty.OPACITY, 0, 0, -2)]
    [InlineData(AnimationProperty.POSITION, 1, 2, -1)]
    [InlineData(AnimationProperty.POSITION, 1, 0, -2)]
    public async Task RealLeftExpansionAllowsKeyframeDragIntoTheNewRangeAndClampsAtTheClipStartOnce(
        AnimationProperty property, int component, int destinationSeconds, int expectedContentSeconds)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var originalKey = CreateKeyframe(property);
        var source = CreateDocument(context.Session.DocumentSnapshot, property, originalKey);
        context.Session.Editor.Reset(source);
        context.Session.SelectCue(source.Subtitles[0].Id);
        var timeline = await PrepareAsync(context);
        ExpandLeft(context, timeline, source);
        var expanded = context.Session.DocumentSnapshot;
        var layer = Assert.Single(expanded.Layers);
        Assert.Same(originalKey, Assert.Single(Assert.Single(layer.Tracks).Keyframes));
        var local = timeline.GetKeyframePoint(layer.Id, property, originalKey.Time, originalKey.Value, component)!.Value;
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        var expectedTime = new MediaTime(expectedContentSeconds);
        var changes = 0;
        context.Session.Editor.Changed += (_, _) => changes++;
        Assert.Same(timeline, context.Window.InputHitTest(point));

        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        Assert.Equal(originalKey.Time, context.Session.SelectedKeyTime);
        var destination = point + new Vector((destinationSeconds - 4) * timeline.PixelsPerSecond, -20);
        context.Window.MouseMove(destination);

        Assert.True(timeline.HasActiveDrag);
        Assert.Same(expanded, context.Session.DocumentSnapshot);
        Assert.Equal(0, changes);
        Assert.All(timeline.KeyframeMarkers.Where(marker => marker.Identity.LayerId == layer.Id), marker =>
        {
            Assert.Equal(expectedTime, marker.Identity.Time);
            Assert.Equal(originalKey.Value, marker.Value);
        });
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.False(timeline.HasActiveDrag);
        Assert.Equal(1, changes);
        var moved = context.Session.DocumentSnapshot;
        var movedLayer = Assert.Single(moved.Layers);
        var movedKey = Assert.Single(Assert.Single(movedLayer.Tracks).Keyframes);
        Assert.Equal(originalKey with { Time = expectedTime }, movedKey);
        Assert.Equal(expectedTime, context.Session.SelectedKeyTime);
        Assert.Equal(new MediaTime(Math.Max(1, destinationSeconds)), movedLayer.Start + movedKey.Time - movedLayer.AnimationOffset);
        Assert.Same(expanded.Subtitles[0], moved.Subtitles[0]);
        Assert.Equal(layer.Start, movedLayer.Start);
        Assert.Equal(layer.End, movedLayer.End);
        Assert.Equal(layer.AnimationOffset, movedLayer.AnimationOffset);
        var reloaded = await SaveReloadAsync(moved);
        AssertKeyframeEqual(movedKey, Assert.Single(Assert.Single(Assert.Single(reloaded.Layers).Tracks).Keyframes));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(expanded, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(expanded, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(moved, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanRedo);
        context.Session.Editor.Reset(reloaded);
        context.Session.SelectCue(layer.SubtitleId!.Value);
        Flush(context.Window);
        var restoredPoint = timeline.GetKeyframePoint(layer.Id, property, expectedTime, movedKey.Value, component)!.Value;
        Assert.Equal(timeline.HeaderWidth + (Math.Max(1, destinationSeconds) - timeline.ViewStart) * timeline.PixelsPerSecond,
            restoredPoint.X, 6);
    }

    [AvaloniaFact]
    public async Task AddKeyframeButtonCreatesASavedKeyframeInTheNewLeftRangeWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var originalKey = CreateKeyframe(AnimationProperty.OPACITY);
        var source = CreateDocument(context.Session.DocumentSnapshot, AnimationProperty.OPACITY, originalKey);
        context.Session.Editor.Reset(source);
        context.Session.SelectCue(source.Subtitles[0].Id);
        var timeline = await PrepareAsync(context);
        ExpandLeft(context, timeline, source);
        context.ViewModel.Effects.Target = new(AnimationProperty.OPACITY);
        await context.Controller.SeekAsync(new(2));
        Flush(context.Window);
        var before = context.Session.DocumentSnapshot;
        Assert.True(context.ViewModel.Effects.CanAddKeyframe);
        var button = UiTestActions.PropertyKeyframeButton(context.Window, AnimationProperty.OPACITY);
        Assert.True(button.IsEffectivelyEnabled);
        var changes = 0;
        context.Session.Editor.Changed += (_, _) => changes++;

        UiTestActions.Click(context.Window, button);
        Flush(context.Window);

        Assert.Equal(1, changes);
        var after = context.Session.DocumentSnapshot;
        var layer = Assert.Single(after.Layers);
        var track = Assert.Single(layer.Tracks);
        Assert.Equal(2, track.Keyframes.Length);
        var added = Assert.Single(track.Keyframes, frame => frame.Time == new MediaTime(-1));
        Assert.Equal(originalKey.Value, added.Value);
        Assert.Same(originalKey, Assert.Single(track.Keyframes, frame => frame.Time == originalKey.Time));
        Assert.Equal(new MediaTime(2), layer.Start + added.Time - layer.AnimationOffset);
        Assert.Equal(added.Time, context.Session.SelectedKeyTime);
        var reloaded = await SaveReloadAsync(after);
        AssertKeyframeEqual(added, Assert.Single(Assert.Single(Assert.Single(reloaded.Layers).Tracks).Keyframes,
            frame => frame.Time == added.Time));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(after, context.Session.DocumentSnapshot);
    }

    private static ProjectDocument CreateDocument(ProjectDocument document, AnimationProperty property, Keyframe keyframe)
    {
        var cue = new SubtitleLine { Start = new(3), End = new(7), Text = "Expanded animation" };
        var layer = new ProjectLayer
        {
            Id = cue.Id,
            Kind = LayerKind.SUBTITLE,
            SubtitleId = cue.Id,
            Start = cue.Start,
            End = cue.End,
            Tracks = [new(property, [keyframe])]
        };
        return document with { Subtitles = [cue], Layers = [layer] };
    }

    private static Keyframe CreateKeyframe(AnimationProperty property)
    {
        return new(new(1), property == AnimationProperty.OPACITY ? AnimationValue.FromScalar(0.25) :
            AnimationValue.FromVector(new(10, 20)), KeyframeInterpolation.EASE_OUT)
        {
            CurveStart = 0.2,
            CurveEnd = 0.8,
            Exponent = 2.5,
            ComponentCurves = property == AnimationProperty.POSITION
                ? [new(KeyframeInterpolation.POWER, 0.1, 0.9) { Exponent = 3.5 }] : []
        };
    }

    private static async Task<SubtitleTimelineControl> PrepareAsync(MainWindowTestContext context)
    {
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Assert.True(await context.Window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        context.ViewModel.Timeline.PixelsPerSecond = 60;
        context.ViewModel.Timeline.ViewStart = 0;
        Flush(context.Window);
        Assert.Equal(context.ViewModel.Timeline.Viewport, timeline.Viewport);
        return timeline;
    }

    private static void ExpandLeft(MainWindowTestContext context, SubtitleTimelineControl timeline, ProjectDocument source)
    {
        var layer = Assert.Single(source.Layers);
        var clip = timeline.GetClipRectangle(layer.Id)!.Value;
        var point = timeline.TranslatePoint(new(clip.Left + 2, clip.Center.Y), context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        var destination = point - new Vector(2 * timeline.PixelsPerSecond, 0);
        context.Window.MouseMove(destination);
        Assert.Same(source, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);
        Assert.False(timeline.HasActiveDrag);
        var expandedLayer = Assert.Single(context.Session.DocumentSnapshot.Layers);
        Assert.Equal(new MediaTime(1), expandedLayer.Start);
        Assert.Equal(layer.End, expandedLayer.End);
        Assert.Equal(new MediaTime(-2), expandedLayer.AnimationOffset);
    }

    private static async Task<ProjectDocument> SaveReloadAsync(ProjectDocument document)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext.ExpandedKeyframe.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "expanded.aegiproject");
        try
        {
            await ProjectStore.SaveAsync(document, path, TestContext.Current.CancellationToken);
            return await ProjectStore.LoadAsync(path, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void AssertKeyframeEqual(Keyframe expected, Keyframe actual)
    {
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Interpolation, actual.Interpolation);
        Assert.Equal(expected.CurveStart, actual.CurveStart);
        Assert.Equal(expected.CurveEnd, actual.CurveEnd);
        Assert.Equal(expected.Exponent, actual.Exponent);
        Assert.Equal(expected.ComponentCurves.ToArray(), actual.ComponentCurves.ToArray());
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
