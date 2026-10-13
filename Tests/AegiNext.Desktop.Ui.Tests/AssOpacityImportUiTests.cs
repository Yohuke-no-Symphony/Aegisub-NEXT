using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.SubtitleFormats;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssOpacityImportUiTests
{
    [AvaloniaTheory]
    [InlineData("\\fad(500,1000)", 1)]
    [InlineData("\\fade(255,64,255,0,500,2000,3000)", 191d / 255)]
    public async Task OrdinaryFadeImportsAsEditableOpacityKeyframesWithPreviewAndOneUndo(string fade, double peak)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, fade);
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.OPACITY);
        Assert.Empty(track.Transforms);
        var frame = Assert.Single(track.Keyframes, value => value.Time == new MediaTime(1, 2));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Contains(AnimationProperty.OPACITY, timeline.GetAnimationProperties(layer.Id));
        var point = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, frame.Time, frame.Value);
        Assert.NotNull(point);
        ClickMarker(context, timeline, point.Value);
        Assert.Equal(frame.Time, context.Session.SelectedKeyTime);
        var input = UiTestActions.PropertyInput(context.Window, AnimationProperty.OPACITY);
        Assert.True(input.IsEffectivelyEnabled);
        Assert.InRange(Math.Abs((double)input.Value!.Value - peak), 0, 0.0000001);
        var original = context.Session.DocumentSnapshot;

        EnterNumber(context.Window, input, "0.45");

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(0.45, SceneEvaluator.EvaluateScalarTrack(
            context.Session.PreviewDocument.Layers.Single(value => value.Id == layer.Id).Tracks
                .Single(value => value.Property == AnimationProperty.OPACITY), frame.Time), 10);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var changed = context.Session.SelectedLayer!;
        Assert.Equal(0.45, changed.Tracks.Single(value => value.Property == AnimationProperty.OPACITY)
            .Keyframes.Single(value => value.Time == frame.Time).Value.Scalar);
        Assert.Equal(1, changed.Opacity);
        Assert.Same(context.Session.DocumentSnapshot, context.ViewModel.Preview.Scene.Document);
        await CaptureAsync(context, fade.StartsWith("\\fad(", StringComparison.Ordinal) ? "ass-fad-opacity-keyframe" : "ass-fade-opacity-keyframe");
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.InRange(Math.Abs((double)input.Value!.Value - peak), 0, 0.0000001);
    }

    [AvaloniaFact]
    public async Task InstantFadeKeepsOperationEditingAndExplainsWhyTheEvaluatedOpacityIsReadOnly()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\fade(255,0,255,500,500,2000,2500)");
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.OPACITY);
        Assert.Empty(track.Keyframes);
        var operation = Assert.Single(track.Transforms, value => value.Start == value.End && value.Value.Scalar == 1);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Contains(AnimationProperty.OPACITY, timeline.GetAnimationProperties(layer.Id));
        var marker = timeline.KeyframeMarkers.First(value => value.Identity.OperationId == operation.Id && !value.Identity.IsOperationStart);
        ClickMarker(context, timeline, marker.Position);
        await context.Session.SeekForEditingAsync(new(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(operation.Id, context.ViewModel.Effects.SelectedOperation?.Id);
        Assert.True(context.ViewModel.Effects.IsOrderedTransform);
        var opacity = UiTestActions.PropertyInput(context.Window, AnimationProperty.OPACITY);
        Assert.Equal(1m, opacity.Value);
        Assert.False(opacity.IsEffectivelyEnabled);
        var original = context.Session.DocumentSnapshot;
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(opacity.GetVisualAncestors().OfType<Control>(), parent =>
            Equals(ToolTip.GetTip(parent), Localization.Get("Workbench.OpacityOrderedTransformHint")));
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.OPACITY);
        Assert.True(UiTestActions.Find<StackPanel>(context.Window, "OrderedTransformSection").IsEffectivelyVisible);
        Assert.False(UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo").IsEffectivelyVisible);
        var valueInput = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationValueInput");
        Assert.True(valueInput.IsEffectivelyEnabled);

        EnterNumber(context.Window, valueInput, "0.6");

        Assert.Same(original, context.Session.DocumentSnapshot);
        var preview = context.Session.PreviewDocument.Layers.Single(value => value.Id == layer.Id).Tracks
            .Single(value => value.Property == AnimationProperty.OPACITY);
        Assert.Equal(0.6, SceneEvaluator.EvaluateScalarTrack(preview, new(1)), 10);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var changed = context.Session.SelectedLayer!.Tracks.Single(value => value.Property == AnimationProperty.OPACITY);
        Assert.Equal(0.6, changed.Transforms.Single(value => value.Id == operation.Id).Value.Scalar);
        Assert.Same(context.Session.DocumentSnapshot, context.ViewModel.Preview.Scene.Document);
        await CaptureAsync(context, "ass-fade-instant-operation");
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.OPACITY);
        Assert.Equal(operation.Id, context.ViewModel.Effects.SelectedOperation?.Id);
        valueInput = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationValueInput");
        Assert.Equal(1m, valueInput.Value);
        Assert.False(opacity.IsEffectivelyEnabled);
    }

    private static ProjectLayer Import(MainWindowTestContext context, string fade)
    {
        context.Session.Editor.Apply("ASS opacity canvas fixture", value => value with { Width = 640, Height = 360 });
        var document = context.Session.DocumentSnapshot;
        var source = $"[Script Info]\nPlayResX: 640\nPlayResY: 360\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,{{\\an5\\pos(320,180)\\fs36{fade}}}Fade 中文\n";
        var imported = AssSubtitleFormat.Parse(source, document.Width, document.Height);
        context.Session.Editor.ImportSubtitleLines(imported, "ASS opacity");
        context.Session.SelectCue(imported.Lines[0].Id);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return context.Session.SelectedLayer!;
    }

    private static void ClickMarker(MainWindowTestContext context, SubtitleTimelineControl timeline, Point point)
    {
        timeline.BringIntoView();
        context.Window.UpdateLayout();
        var windowPoint = timeline.TranslatePoint(point, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(windowPoint));
        context.Window.MouseDown(windowPoint, MouseButton.Left);
        context.Window.MouseUp(windowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnterNumber(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task CaptureAsync(MainWindowTestContext context, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").PreviewCompletion;
        Dispatcher.UIThread.RunJobs();
        UiTestCapture.CaptureExportPanel(context.Window, name);
    }
}
