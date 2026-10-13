using System.Globalization;
using AegiNext.Core.Projects;
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

public sealed class AssTransformAnimationImportUiTests
{
    [AvaloniaTheory]
    [InlineData("\\frz90", AnimationProperty.ROTATION_3D, "RotationInput", -90, "-45")]
    [InlineData("\\frz72000", AnimationProperty.ROTATION_3D, "RotationInput", -72000, "-144000")]
    [InlineData("\\bord6", AnimationProperty.STROKE_WIDTH, "StrokeWidthInput", 6, "4")]
    [InlineData("\\fsp4", AnimationProperty.LETTER_SPACING, "LetterSpacingInput", 4, "6")]
    [InlineData("\\fscx150\\fscy200", AnimationProperty.SCALE, "ScaleXInput", 1.5, "1.75")]
    public async Task SingleTransformImportsAsVisibleEditablePowerKeyframes(string tag, AnimationProperty property,
        string field, double expected, string replacement)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\t(0,2000,2," + tag + ")",
            property is AnimationProperty.SCALE or AnimationProperty.ROTATION_3D ? ["Ass.TransformAppearanceAnimation"] : []);
        var track = Assert.Single(layer.Tracks, value => value.Property == property);
        Assert.Empty(track.Transforms);
        Assert.Contains(track.Keyframes, keyframe => keyframe.GetCurve(property == AnimationProperty.ROTATION_3D ? 2 : 0) is
            { Interpolation: KeyframeInterpolation.POWER, Exponent: 2 });
        var frame = track.Keyframes[^1];
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Contains(property, timeline.GetAnimationProperties(layer.Id));
        var point = timeline.GetKeyframePoint(layer.Id, property, frame.Time, frame.Value);
        Assert.NotNull(point);
        ClickTimeline(context, timeline, point.Value);
        await context.Session.SeekForEditingAsync(frame.Time);
        Assert.Equal(frame.Time, context.Session.SelectedKeyTime);
        Assert.Contains(context.ViewModel.Effects.Properties, choice => choice.Property == property);
        var ordinary = FindOrdinary<NumericDraftInput>(context, field);
        Assert.True(ordinary.IsEffectivelyEnabled);
        Assert.Equal((decimal)AnimationPropertyMetadata.GetMinimum(property), ordinary.Minimum);
        Assert.Equal((decimal)AnimationPropertyMetadata.GetMaximum(property), ordinary.Maximum);
        Assert.Equal(expected, (double)ordinary.Value!.Value, 10);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window,
            property == AnimationProperty.ROTATION_3D ? "KeyframeValueZInput" : property == AnimationProperty.SCALE ? "KeyframeValueXInput" : "KeyframeValueInput");
        Assert.True(input.IsEffectivelyEnabled);
        Assert.Equal(expected, (double)input.Value!.Value, 10);
        var original = context.Session.DocumentSnapshot;
        Type(context.Window, input, replacement);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var requested = double.Parse(replacement, CultureInfo.InvariantCulture);
        var preview = context.Session.PreviewDocument.Layers.Single(value => value.Id == layer.Id).Tracks.Single(value => value.Property == property);
        Assert.Equal(requested, preview.Keyframes[^1].Value.GetComponent(property == AnimationProperty.ROTATION_3D ? 2 : 0));
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var changed = context.Session.SelectedLayer!.Tracks.Single(value => value.Property == property);
        Assert.Equal(requested, changed.Keyframes[^1].Value.GetComponent(property == AnimationProperty.ROTATION_3D ? 2 : 0));
        Assert.Equal(original.Subtitles[0].Style, context.Session.SelectedCue!.Style);
        if (property == AnimationProperty.SCALE)
        {
            Assert.Equal(2, changed.Keyframes[^1].Value.Vector.Y);
        }
        UiTestCapture.CaptureExportPanel(context.Window, "ass-transform-keyframe-" + property);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(expected, (double)input.Value!.Value, 10);
    }

    [AvaloniaTheory]
    [InlineData("\\frz90", AnimationProperty.ROTATION_3D, "RotationInput", -90, "-45")]
    [InlineData("\\bord6", AnimationProperty.STROKE_WIDTH, "StrokeWidthInput", 6, "4")]
    [InlineData("\\fsp4", AnimationProperty.LETTER_SPACING, "LetterSpacingInput", 4, "6")]
    [InlineData("\\fscx150\\fscy200", AnimationProperty.SCALE, "ScaleInput", 1.5, "1.75")]
    public async Task InstantTransformKeepsOrdinaryFieldsReadOnlyAndOperationValuesEditable(string tag, AnimationProperty property,
        string field, double expected, string replacement)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\t(1000,1000," + tag + ")",
            property is AnimationProperty.SCALE or AnimationProperty.ROTATION_3D ? ["Ass.TransformAppearanceAnimation"] : []);
        var track = Assert.Single(layer.Tracks, value => value.Property == property);
        Assert.Empty(track.Keyframes);
        var operation = Assert.Single(track.Transforms);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Contains(property, timeline.GetAnimationProperties(layer.Id));
        var marker = timeline.KeyframeMarkers.First(value => value.Identity.OperationId == operation.Id && !value.Identity.IsOperationStart);
        ClickTimeline(context, timeline, marker.Position);
        await context.Session.SeekForEditingAsync(operation.End);
        Assert.Equal(operation.Id, context.ViewModel.Effects.SelectedOperation?.Id);
        var ordinary = FindOrdinary<Control>(context, field);
        Assert.False(ordinary.IsEffectivelyEnabled);
        var original = context.Session.DocumentSnapshot;
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(ordinary.GetVisualAncestors().OfType<Control>(), parent =>
            Equals(ToolTip.GetTip(parent), Localization.Get("Workbench.StyleOrderedTransformHint")));
        Assert.Same(original, context.Session.DocumentSnapshot);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window,
            property == AnimationProperty.ROTATION_3D ? "OperationValueZInput" : property == AnimationProperty.SCALE ? "OperationValueXInput" : "OperationValueInput");
        Assert.True(input.IsEffectivelyEnabled);
        Assert.Equal(expected, (double)input.Value!.Value, 10);
        Type(context.Window, input, replacement);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var requested = double.Parse(replacement, CultureInfo.InvariantCulture);
        var preview = context.Session.PreviewDocument.Layers.Single(value => value.Id == layer.Id).Tracks.Single(value => value.Property == property);
        Assert.Equal(requested, preview.Transforms.Single(value => value.Id == operation.Id).Value.GetComponent(property == AnimationProperty.ROTATION_3D ? 2 : 0));
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var changed = context.Session.SelectedLayer!.Tracks.Single(value => value.Property == property);
        Assert.Equal(requested, changed.Transforms.Single(value => value.Id == operation.Id).Value.GetComponent(property == AnimationProperty.ROTATION_3D ? 2 : 0));
        if (property == AnimationProperty.SCALE)
        {
            Assert.Equal(2, changed.Transforms.Single(value => value.Id == operation.Id).Value.Vector.Y);
        }
        Assert.Equal(original.Subtitles[0].Style, context.Session.SelectedCue!.Style);
        UiTestCapture.CaptureExportPanel(context.Window, "ass-transform-operation-" + property);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(expected, (double)input.Value!.Value, 10);
        Assert.False(ordinary.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task NativeMirroredScaleRemainsEditableInTheOrdinaryVectorInput()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        Import(context, string.Empty);
        context.Session.Editor.Apply("Native mirror fixture", document => document with
        {
            Layers = [document.Layers[0] with { Transform = document.Layers[0].Transform with { Scale = new(-1.5, 2) } }]
        });
        var original = context.Session.DocumentSnapshot;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "ScaleXInput");
        Assert.Equal(-1.5m, input.Value);
        Assert.Equal((decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.SCALE), input.Minimum);
        Assert.Equal((decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.SCALE), input.Maximum);
        Type(context.Window, input, "-2");
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(new ScenePoint(-2, 2), context.Session.PreviewDocument.Layers[0].Transform.Scale);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new ScenePoint(-2, 2), context.Session.SelectedLayer!.Transform.Scale);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(-1.5m, input.Value);
    }

    [AvaloniaFact]
    public async Task ZeroScaleEntranceCanBeEditedAsANativeMirroredScaleWithoutClamping()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\fscx0\\fscy0\\t(0,2000,\\fscx150\\fscy200)",
            "Ass.TransformAppearanceAnimation", "Ass.TransformLayout", "Ass.TransformAppearance");
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.SCALE);
        var frame = track.Keyframes[0];
        Assert.Equal(new ScenePoint(0, 0), frame.Value.Vector);
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, track.Property, frame.Time, frame.Time)));
        await context.Session.SeekForEditingAsync(frame.Time);
        var original = context.Session.DocumentSnapshot;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "ScaleXInput");
        Assert.Equal(0m, input.Value);
        Assert.True(input.IsEffectivelyEnabled);
        Type(context.Window, input, "-1.5");
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new ScenePoint(-1.5, 0), context.Session.SelectedLayer!.Tracks.Single(value =>
            value.Property == AnimationProperty.SCALE).Keyframes[0].Value.Vector);
        Assert.Null(context.Session.LastError);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(0m, input.Value);
    }

    [AvaloniaFact]
    public async Task OrderedScaleAndRotationStillAllowCanvasPositionDragWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = Import(context, "\\t(1000,1000,\\frz30\\fscx150\\fscy120)", "Ass.TransformAppearanceAnimation");
        Assert.All(layer.Tracks, track => Assert.True(track.IsOrdered));
        await context.Session.SeekForEditingAsync(new(2));
        var original = context.Session.DocumentSnapshot;
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        canvas.BringIntoView();
        context.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await canvas.PreviewCompletion;
        Dispatcher.UIThread.RunJobs();
        var board = canvas.ProjectRectangle;
        var center = canvas.TranslatePoint(board.Center, context.Window)!.Value;
        var delta = new Vector(40 * board.Width / 640, 20 * board.Height / 360);
        Assert.Same(canvas, context.Window.InputHitTest(center));
        context.Window.MouseDown(center, MouseButton.Left);
        Assert.True(canvas.HasActiveDrag);
        context.Window.MouseMove(center + delta);
        Assert.Same(original, context.Session.DocumentSnapshot);
        context.Window.MouseUp(center + delta, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(canvas.HasActiveDrag);
        var changed = context.Session.SelectedLayer!;
        Assert.Equal(40, changed.Transform.X, 5);
        Assert.Equal(20, changed.Transform.Y, 5);
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.Null(context.Session.LastError);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestCapture.CaptureExportPanel(context.Window, "ass-transform-ordered-canvas-position");
    }

    private static ProjectLayer Import(MainWindowTestContext context, string tags, params string[] expectedDiagnosticCodes)
    {
        context.Session.Editor.Apply("ASS animated transform canvas", document => document with { Width = 640, Height = 360 });
        var source = "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Outline, Shadow, Alignment, MarginL, MarginR, MarginV\n" +
            "Style: Default,sans-serif,28,&H00FFFFFF,&H00808080,&H00000000,&H00000000,2,0,5,20,20,20\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            $"Dialogue: 0,0:00:00.00,0:00:04.00,Default,,0,0,0,,{{\\pos(320,180){tags}}}Transform 中文\n";
        var imported = AssSubtitleFormat.Parse(source, 640, 360);
        Assert.Equal(expectedDiagnosticCodes.Append("Ass.ShadowComposition").Order(StringComparer.Ordinal),
            imported.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct().Order(StringComparer.Ordinal));
        context.Session.Editor.ImportSubtitleLines(imported, "ASS numeric transform");
        context.Session.SelectCue(imported.Lines[0].Id);
        context.ViewModel.Effects.TransformExpanded = true;
        context.ViewModel.Effects.AnimationExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return context.Session.SelectedLayer!;
    }

    private static T FindOrdinary<T>(MainWindowTestContext context, string field) where T : Control
    {
        if (field == "RotationInput")
        {
            context.Window.Layouts.Activate("effects");
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        return UiTestActions.Find<T>(context.Window, field);
    }

    private static void ClickTimeline(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local)
    {
        timeline.BringIntoView();
        context.Window.UpdateLayout();
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
}
