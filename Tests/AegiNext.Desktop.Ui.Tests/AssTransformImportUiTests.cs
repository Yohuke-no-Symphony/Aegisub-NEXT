using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.SubtitleFormats;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssTransformImportUiTests
{
    [AvaloniaFact]
    public async Task ImportedStaticTransformAppearsInRealInputsAndEditsPreviewWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\pos(200,150)\\fscx150\\fscy200\\frz30");
        var original = context.Session.DocumentSnapshot;

        Assert.Equal(200m, UiTestActions.Find<NumericDraftInput>(context.Window, "PositionXInput").Value);
        Assert.Equal(150m, UiTestActions.Find<NumericDraftInput>(context.Window, "PositionYInput").Value);
        Assert.Equal(1.5m, UiTestActions.Find<NumericDraftInput>(context.Window, "ScaleXInput").Value);
        Assert.Equal(2m, UiTestActions.Find<NumericDraftInput>(context.Window, "ScaleYInput").Value);
        Assert.Equal(-30m, UiTestActions.Find<NumericDraftInput>(context.Window, "RotationInput").Value);
        Assert.Equal(layer.Transform, context.ViewModel.Preview.Scene.Document.Layers.Single(value => value.Id == layer.Id).Transform);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").PreviewCompletion;
        Dispatcher.UIThread.RunJobs();
        UiTestCapture.CaptureExportPanel(context.Window, "ass-static-transform-import");

        EnterNumber(context.Window, "RotationInput", "-45");
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();

        var changed = context.Session.SelectedLayer!;
        Assert.Equal(-45, changed.Transform.Rotation);
        Assert.Equal(new ScenePoint(1.5, 2), changed.Transform.Scale);
        Assert.Equal(changed.Transform,
            context.ViewModel.Preview.Scene.Document.Layers.Single(value => value.Id == layer.Id).Transform);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(-30m, UiTestActions.Find<NumericDraftInput>(context.Window, "RotationInput").Value);
    }

    [AvaloniaFact]
    public async Task ImportedMoveDrawsAnEditableTimelineRowAndKeepsAbsolutePositionAndUndoInSync()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\move(100,90,300,190,500,1500)");
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.POSITION);
        var end = track.Keyframes[^1];
        timeline.BringIntoView();
        context.Window.UpdateLayout();
        using var frame = context.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Contains(AnimationProperty.POSITION, timeline.GetAnimationProperties(layer.Id));
        var localPoint = timeline.GetKeyframePoint(layer.Id, AnimationProperty.POSITION, end.Time, end.Value);
        Assert.NotNull(localPoint);
        var point = timeline.TranslatePoint(localPoint.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));

        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new MediaTime(3, 2), context.Session.SelectedKeyTime);
        Assert.Equal(300m, UiTestActions.Find<NumericDraftInput>(context.Window, "PositionXInput").Value);
        Assert.Equal(190m, UiTestActions.Find<NumericDraftInput>(context.Window, "PositionYInput").Value);
        Assert.True(UiTestActions.Find<VectorDraftInput>(context.Window, "PositionInput").IsEffectivelyVisible);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").PreviewCompletion;
        Dispatcher.UIThread.RunJobs();
        UiTestCapture.CaptureExportPanel(context.Window, "ass-move-end-keyframe-import");
        var original = context.Session.DocumentSnapshot;
        EnterNumber(context.Window, "PositionXInput", "340");
        Assert.True(timeline.Focus());
        Dispatcher.UIThread.RunJobs();

        var changed = context.Session.SelectedLayer!;
        Assert.Equal(new ScenePoint(240, 100), changed.Tracks.Single(value => value.Property == AnimationProperty.POSITION).Keyframes[^1].Value.Vector);
        Assert.Equal(340m, context.ViewModel.Effects.PositionX);
        Assert.Equal(190m, context.ViewModel.Effects.PositionY);
        Assert.Same(context.Session.DocumentSnapshot, context.ViewModel.Preview.Scene.Document);
        Assert.Equal(default, changed.Transform.Position);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(300m, context.ViewModel.Effects.PositionX);
        Assert.Equal(190m, context.ViewModel.Effects.PositionY);

        EnterNumber(context.Window, "PositionYInput", "250");
        Assert.True(timeline.Focus());
        Dispatcher.UIThread.RunJobs();

        changed = context.Session.SelectedLayer!;
        Assert.Equal(new ScenePoint(200, 160), changed.Tracks.Single(value => value.Property == AnimationProperty.POSITION).Keyframes[^1].Value.Vector);
        Assert.Equal(300m, context.ViewModel.Effects.PositionX);
        Assert.Equal(250m, context.ViewModel.Effects.PositionY);
        Assert.Same(context.Session.DocumentSnapshot, context.ViewModel.Preview.Scene.Document);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(300m, context.ViewModel.Effects.PositionX);
        Assert.Equal(190m, context.ViewModel.Effects.PositionY);
    }

    private static ProjectLayer Import(MainWindowTestContext context, string tags)
    {
        context.Session.Editor.Apply("ASS transform canvas fixture", value => value with { Width = 640, Height = 360 });
        var document = context.Session.DocumentSnapshot;
        var source = $"[Script Info]\nPlayResX: {document.Width}\nPlayResY: {document.Height}\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,{{{tags}}}ASS 中文\n";
        var imported = AssSubtitleFormat.Parse(source, document.Width, document.Height);
        context.Session.Editor.ImportSubtitleLines(imported, "ASS");
        context.Session.SelectCue(imported.Lines[0].Id);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return context.Session.SelectedLayer!;
    }

    private static void EnterNumber(Window window, string name, string text)
    {
        var input = UiTestActions.Find<NumericDraftInput>(window, name);
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
}
