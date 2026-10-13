using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleEffectsWorkflowUiTests
{
    private static readonly string[] removedControls =
    [
        "RectangleButton", "EllipseButton", "ImageButton", "GroupButton", "UngroupButton", "DeleteLayerButton",
        "LayerUpButton", "LayerDownButton", "LayerList", "MaskButton", "ClearMaskButton", "InvertMaskCheck"
    ];

    [AvaloniaFact]
    public async Task RealPreviewDragAndResetButtonRestoreAutomaticPositionWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await PrepareSubtitleAsync(context);
        var original = context.Session.DocumentSnapshot;
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        await PresentAsync(context.Window, canvas);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        var geometry = Assert.IsType<ProjectLayerGeometry>(renderer.GetLayerGeometry(original, MediaTime.Zero, id));
        var board = canvas.ProjectRectangle;
        var local = new Point(board.X + geometry.WorldCorners.Average(point => point.X) * board.Width / original.Width,
            board.Y + geometry.WorldCorners.Average(point => point.Y) * board.Height / original.Height);
        var point = canvas.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(canvas, context.Window.InputHitTest(point));
        var delta = new Vector(40 * board.Width / original.Width, -20 * board.Height / original.Height);
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(canvas.HasActiveDrag);
        context.Window.MouseMove(point + delta);
        Assert.Same(original, context.Session.DocumentSnapshot);
        context.Window.MouseUp(point + delta, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var dragged = context.Session.DocumentSnapshot;
        Assert.Equal(40, dragged.Layers[0].Transform.Position.X, 5);
        Assert.Equal(-20, dragged.Layers[0].Transform.Position.Y, 5);
        Assert.False(canvas.HasActiveDrag);

        UiTestActions.Click(context.Window, "ResetPositionButton");

        var reset = context.Session.DocumentSnapshot;
        Assert.Null(reset.Subtitles[0].Style.Position);
        Assert.Equal(default, reset.Layers[0].Transform.Position);
        Assert.Null(reset.Layers[0].MotionPath);
        Assert.DoesNotContain(reset.Layers[0].Tracks, track => track.Property is AnimationProperty.POSITION or AnimationProperty.PATH_PROGRESS);
        Assert.Equal(dragged.Layers[0].Transform.Scale, reset.Layers[0].Transform.Scale);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(dragged, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task EffectPositionResetKeepsCustomAnchorPivotAndOffsetAndReturnsToTheirMeasuredPosition()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await PrepareSubtitleAsync(context);
        var position = new SubtitlePosition
        {
            Anchor = new(0.25, 0.75), Pivot = new(0.3, 0.4), Offset = new(18, -24)
        };
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { Position = position }
        });
        context.Session.Editor.UpdateLayer(id, layer => layer with
        {
            Transform = layer.Transform with { Position = new(30, 40), Scale = new(1.5, 0.75), Rotation = 12 },
            Opacity = 0.75,
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(5)),
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(30, 40))]),
                new(AnimationProperty.OPACITY, [new(new(0), 0.75)]),
                new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(new(5), 1)])
            ]
        });
        var before = context.Session.DocumentSnapshot;
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        await PresentAsync(context.Window, canvas);

        UiTestActions.Click(context.Window, "ResetPositionButton");

        var after = context.Session.DocumentSnapshot;
        Assert.Same(before.Subtitles[0], after.Subtitles[0]);
        Assert.Same(position, after.Subtitles[0].Style.Position);
        Assert.True(context.ViewModel.Styles.Subtitle.Position.IsExplicit);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        var measured = renderer.MeasureSubtitlePlacement(after, after.Subtitles[0]).Position;
        Assert.Equal(position.Anchor, measured.Anchor);
        Assert.Equal(position.Pivot, measured.Pivot);
        Assert.Equal((decimal)(measured.Anchor.X * after.Width + measured.Offset.X), context.ViewModel.Effects.PositionX);
        Assert.Equal((decimal)(measured.Anchor.Y * after.Height + measured.Offset.Y), context.ViewModel.Effects.PositionY);
        Assert.Equal(default, after.Layers[0].Transform.Position);
        Assert.Null(after.Layers[0].MotionPath);
        Assert.Equal(new ScenePoint(1.5, 0.75), after.Layers[0].Transform.Scale);
        Assert.Equal(12, after.Layers[0].Transform.Rotation);
        Assert.Equal(0.75, after.Layers[0].Opacity);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(after.Layers[0].Tracks).Property);
        Assert.Same(after, context.ViewModel.Preview.Scene.Document);
        await PresentAsync(context.Window, canvas);

        UiTestActions.Click(context.Window, "ResetPositionButton");
        Assert.Same(after, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(after, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task PositionAndScaleKeyboardEditsWriteSingleVectorsAtTheSelectedKeyframeTime()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await PrepareSubtitleAsync(context);
        context.Session.Editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(1), new ScenePoint(10, 20)));
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.POSITION, new(1), new(1))));
        var position = UiTestActions.Find<VectorDraftInput>(context.Window, "PositionInput");
        Assert.True(position.IsEffectivelyVisible);
        Assert.NotNull(context.ViewModel.Effects.PositionX);
        Assert.NotNull(context.ViewModel.Effects.PositionY);
        var baseX = context.ViewModel.Effects.PositionX.Value - 10m;
        var baseY = context.ViewModel.Effects.PositionY.Value - 20m;
        EnterNumber(context.Window, "PositionXInput", (baseX + 30m).ToString(CultureInfo.InvariantCulture));
        EnterNumber(context.Window, "PositionYInput", (baseY + 40m).ToString(CultureInfo.InvariantCulture));
        CommitByFocusingTimeline(context.Window);
        var layer = context.Session.SelectedLayer!;
        var key = Assert.Single(layer.Tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes);
        Assert.Equal(new MediaTime(1), key.Time);
        Assert.Equal(30, key.Value.Vector.X, 10);
        Assert.Equal(40, key.Value.Vector.Y, 10);
        Assert.Equal(baseX + 30m, context.ViewModel.Effects.PositionX);
        Assert.Equal(baseY + 40m, context.ViewModel.Effects.PositionY);
        Assert.Equal(default, layer.Transform.Position);
        EnterNumber(context.Window, "ScaleXInput", "2");
        EnterNumber(context.Window, "ScaleYInput", "3");
        CommitByFocusingTimeline(context.Window);
        layer = context.Session.SelectedLayer!;
        var scale = Assert.Single(layer.Tracks.Single(track => track.Property == AnimationProperty.SCALE).Keyframes);
        Assert.Equal(new MediaTime(1), scale.Time);
        Assert.Equal(new ScenePoint(2, 3), scale.Value.Vector);
        Assert.Equal(new ScenePoint(1, 1), layer.Transform.Scale);
        Assert.Equal(2, layer.Tracks.Length);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(context.Session.DocumentSnapshot));
        Assert.Equal(layer.Tracks.SelectMany(track => track.Keyframes), restored.Layers[0].Tracks.SelectMany(track => track.Keyframes));
    }

    [AvaloniaFact]
    public async Task PathButtonsAndPreviewDoubleClickAddPointsAndRemovedGraphicsToolsAreAbsent()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await PrepareSubtitleAsync(context);
        var panel = context.Window.Panels[WorkbenchPanelIds.EFFECTS];
        Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Control>(), control => removedControls.Contains(control.Name));
        UiTestActions.ExpandEffectsCategory(context.Window, "PathCategory");
        UiTestActions.Click(context.Window, "PathButton");
        var initial = context.Session.DocumentSnapshot;
        Assert.Single(initial.Layers[0].MotionPath!.Path.Segments);
        UiTestActions.Click(context.Window, "AddPathPointButton");
        Assert.Equal(2, context.Session.SelectedLayer!.MotionPath!.Path.Segments.Length);
        UiTestActions.Click(context.Window, "AddPathPointButton");
        Assert.Equal(3, context.Session.SelectedLayer!.MotionPath!.Path.Segments.Length);
        UiTestActions.Click(context.Window, "RemovePathPointButton");
        Assert.Equal(2, context.Session.SelectedLayer!.MotionPath!.Path.Segments.Length);
        var before = context.Session.DocumentSnapshot;
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        Assert.Equal(CanvasEditMode.PATH, canvas.EditMode);
        await PresentAsync(context.Window, canvas);
        var board = canvas.ProjectRectangle;
        var point = canvas.TranslatePoint(new(board.X + board.Width * 0.25, board.Y + board.Height * 0.25), context.Window)!.Value;
        Assert.Same(canvas, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, context.Session.SelectedLayer!.MotionPath!.Path.Segments.Length);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(before));
        Assert.Equal(before.Layers[0].MotionPath!.Path.Segments.ToArray(), restored.Layers[0].MotionPath!.Path.Segments.ToArray());
    }

    [AvaloniaTheory]
    [InlineData("fade-in-out")]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("pop-in")]
    [InlineData("pop-out")]
    [InlineData("slide-in")]
    [InlineData("slide-out")]
    public async Task EveryBuiltinPresetFitsShortAndLongClipsAndSurvivesSaveReload(string scriptId)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await PrepareSubtitleAsync(context);
        foreach (var duration in new MediaTime[] { new(1, 10), new(3, 5), new(5) })
        {
            context.Session.Editor.SetSubtitleTiming(id, MediaTime.Zero, duration, TimelineEditMode.STRETCH);
            context.Session.SelectCue(id);
            var before = context.Session.DocumentSnapshot;
            var expected = EffectScriptCompiler.Compile(BuiltinEffectScripts.Get(scriptId).Script, before.Layers[0]);
            UiTestActions.SelectBuiltinPreset(context.Window, scriptId);
            UiTestActions.Click(context.Window, "ApplyPresetButton");
            await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(context.ViewModel.EffectPresets.ApplyCommand).ExecutionTask!;
            var after = context.Session.DocumentSnapshot;
            Assert.Equal(expected.SelectMany(track => track.Keyframes), after.Layers[0].Tracks.SelectMany(track => track.Keyframes));
            Assert.All(after.Layers[0].Tracks, track => Assert.Equal(duration, track.Keyframes[^1].Time));
            var restored = ProjectStore.Deserialize(ProjectStore.Serialize(after));
            Assert.Equal(after.Layers[0].Tracks.SelectMany(track => track.Keyframes), restored.Layers[0].Tracks.SelectMany(track => track.Keyframes));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(before, context.Session.DocumentSnapshot);
        }
    }

    private static async Task<Guid> PrepareSubtitleAsync(MainWindowTestContext context)
    {
        var id = await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.Apply("Measured subtitle fixture", document => document with
        {
            Width = 640, Height = 360,
            Subtitles = [document.Subtitles[0] with
            {
                Text = "字幕 ABC 123", Style = document.Subtitles[0].Style with
                {
                    FontFamily = "sans-serif", FontSize = 28, FontAssetId = null, Position = null
                }
            }]
        });
        context.Session.SelectCue(id);
        Dispatcher.UIThread.RunJobs();
        return id;
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

    private static void CommitByFocusingTimeline(Window window)
    {
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task PresentAsync(Window window, EffectCanvasControl canvas)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            await canvas.PreviewCompletion;
            Dispatcher.UIThread.RunJobs();
            if (canvas.PresentedPreviewSequence == canvas.PreviewSequence)
            {
                return;
            }

            await Task.Yield();
        } while (DateTime.UtcNow < deadline);
        Assert.Fail("Preview did not present the latest scene before the gesture.");
    }
}
