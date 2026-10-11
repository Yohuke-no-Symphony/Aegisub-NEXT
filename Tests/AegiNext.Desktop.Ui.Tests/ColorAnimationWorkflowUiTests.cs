using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ColorAnimationWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWorkbenchMergedMarkerDragMovesOnlyTimeAndPreservesCompleteValueWithOneUndo(bool color)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context, duration: new(8));
        var property = color ? AnimationProperty.FILL : AnimationProperty.POSITION;
        var original = color
            ? AnimationValue.FromColor(new(0.5, 0.5, 0.5, 0.7))
            : AnimationValue.FromVector(new(20, 20));
        context.Session.Editor.SetKeyframe(id, property, new(new(1), original));
        UiTestActions.SelectAnimationProperty(context.Window, property);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var location = timeline.GetKeyframePoint(id, property, new(1), original, 0)!.Value;
        var point = timeline.TranslatePoint(location, context.Window)!.Value;
        var before = context.Session.DocumentSnapshot;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        var destination = point + new Vector(timeline.PixelsPerSecond, -8);
        context.Window.MouseMove(destination);
        context.Window.MouseUp(destination, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var frame = Assert.Single(Assert.Single(context.Session.SelectedLayer!.Tracks).Keyframes);
        Assert.Equal(new MediaTime(2), frame.Time);
        Assert.Equal(frame.Value.GetComponent(0), frame.Value.GetComponent(1));
        Assert.Equal(original, frame.Value);
        if (color)
        {
            Assert.Equal(frame.Value.Color.Red, frame.Value.Color.Blue);
            Assert.Equal(0.7, frame.Value.Color.Alpha);
        }
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualColorInputCommitsCompleteColorBeforeChangingKeyframeWithOneUndo(bool rgba)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.SetKeyframe(id, AnimationProperty.FILL, new(new(1), new SceneColor(1, 0, 0)));
        context.Session.Editor.SetKeyframe(id, AnimationProperty.FILL, new(new(2), new SceneColor(0, 0, 1)));
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(1), new(1))));
        var before = context.Session.DocumentSnapshot;
        var color = UiTestActions.Find<ColorDraftInput>(context.Window, "KeyframeColorInput");
        if (rgba)
        {
            Assert.True(color.Draft!.TryToggleInputMode());
        }
        var text = color.FindControl<TextBox>("ColorInput")!;
        text.BringIntoView();
        context.Window.UpdateLayout();
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput(rgba ? "32,64,128,128" : "#20408080");
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(2), new(2))));
        Dispatcher.UIThread.RunJobs();

        Assert.True(ColorHexCodec.TryParse("#20408080", 1, true, out var expected));
        var track = Assert.Single(context.Session.SelectedLayer!.Tracks);
        Assert.Equal(expected, track.Keyframes[0].Value.Color);
        Assert.Equal(new SceneColor(0, 0, 1), track.Keyframes[1].Value.Color);
        Assert.Equal(new MediaTime(2), context.Session.SelectedKeyTime);
        Assert.Equal(new SceneColor(0, 0, 1), context.ViewModel.Effects.KeyframeColorDraft.Value);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(context.Session.DocumentSnapshot));
        Assert.Equal(expected, reloaded.Layers[0].Tracks[0].Keyframes[0].Value.Color);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidColorKeepsTargetAndActualEscapeRestoresItWithoutBlockingNavigation(bool rgba)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.SetKeyframe(id, AnimationProperty.FILL, new(new(1), SceneColor.White));
        context.Session.Editor.SetKeyframe(id, AnimationProperty.FILL, new(new(2), SceneColor.Black));
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(1), new(1))));
        var before = context.Session.DocumentSnapshot;
        var color = UiTestActions.Find<ColorDraftInput>(context.Window, "KeyframeColorInput");
        if (rgba)
        {
            Assert.True(color.Draft!.TryToggleInputMode());
        }
        var text = color.FindControl<TextBox>("ColorInput")!;
        text.BringIntoView();
        context.Window.UpdateLayout();
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        var invalid = rgba ? "256,0,0," : "#broken";
        context.Window.KeyTextInput(invalid);
        Assert.False(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(2), new(2))));
        Assert.Equal(invalid, text.Text);
        Assert.Equal(new MediaTime(1), context.Session.SelectedKeyTime);
        await context.Controller.SeekAsync(new(3));
        context.Session.Tick();
        Assert.Equal(invalid, text.Text);
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(context.ViewModel.Effects.KeyframeColorDraft.Error);
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(2), new(2))));
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task UneditedHdrColorSurvivesInspectorRefreshLanguageAndUnrelatedDraftCommit()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context);
        var hdr = new SceneColor(2.5, -0.1, 0.123456789, 0.7);
        context.Session.Editor.UpdateSubtitle(id, cue => cue with { Style = cue.Style with { Fill = hdr } });
        Assert.Equal(hdr, context.ViewModel.Styles.Subtitle.FillDraft.Value);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "en-US" });
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        context.ViewModel.Effects.RotationText = "12";
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal(hdr, context.Session.SelectedCue!.Style.Fill);
        Assert.Equal(hdr, context.ViewModel.Styles.Subtitle.FillDraft.Value);
        UiTestActions.SelectAnimationProperty(context.Window, AnimationProperty.FILL);
        Assert.Equal(hdr, context.ViewModel.Effects.KeyframeColorDraft.Value);
        Assert.DoesNotContain(context.ViewModel.Effects.Properties, choice => choice.Property == AnimationProperty.FILL_RED);
    }

    [AvaloniaFact]
    public async Task StyleColorPickerEditsTheSelectedTimeAsOneColorTrackAndPreservesOtherComponentsCurves()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context);
        var curves = new AnimationCurve?[] { new(KeyframeInterpolation.EASE_IN), null, new(KeyframeInterpolation.HOLD) };
        var first = new Keyframe(new(1), new SceneColor(1, 0.5, 0.2, 0.7)) { ComponentCurves = [.. curves] };
        context.Session.Editor.SetKeyframe(id, AnimationProperty.FILL, first);
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.FILL, new(1), new(1))));
        var before = context.Session.DocumentSnapshot;
        var expected = new SceneColor(0.1, 0.2, 2.4, 0.6);
        context.ViewModel.Styles.Subtitle.FillDraft.SetValue(expected);
        Assert.True(context.ViewModel.Styles.Subtitle.FillDraft.TryCommit());
        var frame = Assert.Single(Assert.Single(context.Session.SelectedLayer!.Tracks).Keyframes);
        Assert.Equal(expected, frame.Value.Color);
        Assert.Equal(first.ComponentCurves, frame.ComponentCurves);
        Assert.Equal(before.Subtitles[0].Style.Fill, context.Session.SelectedCue!.Style.Fill);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }
}
