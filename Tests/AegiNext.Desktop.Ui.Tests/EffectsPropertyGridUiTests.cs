using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Effects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using AegiNext.Desktop.I18n;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsPropertyGridUiTests
{
    [AvaloniaFact]
    public async Task CollapsingTypographyKeepsInvalidDraftAndReopensForError()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context);
        var row = context.ViewModel.Effects.TypographyRows.Single(row => row.Target.Property == AnimationProperty.FONT_SIZE);
        var category = UiTestActions.Find<Expander>(context.Window, "TypographyCategory");
        context.ViewModel.Effects.TypographyExpanded = true;
        context.Window.UpdateLayout();
        var view = context.Window.GetVisualDescendants().OfType<EffectPropertyRowView>().Single(view => view.DataContext == row);
        var input = view.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.IsVisible);
        input.BringIntoView();
        context.Window.UpdateLayout();
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        try
        {
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("unfinished");
            Assert.Equal("unfinished", input.RawText);
            Assert.Equal("unfinished", row.X.RawText);
            var source = context.Session.DocumentSnapshot;

            var header = category.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(button => button.Name == "PART_HeaderSite");
            header.BringIntoView();
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            context.Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = context.Window.CaptureRenderedFrame();
            var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), context.Window)!.Value;
            Assert.True(context.Window.InputHitTest(point) is Visual visual && (ReferenceEquals(visual, header) || visual.GetVisualAncestors().Contains(header)));
            context.Window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            context.Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var pressedFrame = context.Window.CaptureRenderedFrame();
            context.Window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(category.IsExpanded);
            Assert.Equal("unfinished", row.X.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.False(context.Session.TryCommitDrafts());
            Dispatcher.UIThread.RunJobs();
            Assert.True(category.IsExpanded, $"Panel={context.ViewModel.InvalidPanelId}; Field={context.ViewModel.InvalidFieldKey}; Error={context.Session.LastError}; Ancestors={string.Join(",", view.GetVisualAncestors().Select(parent => parent.GetType().Name))}");
            Assert.Equal("unfinished", row.X.RawText);
            row.X.RawText = "72";
            Assert.True(context.Session.TryCommitDrafts());
        }
        finally
        {
            row.Restore(row.XFieldKey);
            context.Session.TryCommitDrafts(false);
        }
    }

    [AvaloniaFact]
    public async Task RangeStateAnimationToggleOnlyClearsCompleteTargetAndUndoesOnce()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context);
        var session = context.Session;
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, session.SelectedCue!.Text.Length);
        session.Editor.SetSubtitleAnimationRange(session.SelectedCue.Id, range);
        var target = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE);
        var layerId = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(layerId, AnimationProperty.FILL, new(new(0), SceneColor.White));
        session.Editor.SetKeyframe(layerId, target, new(new(0), SceneColor.Black));
        session.ViewModel.Effects.Target = target;
        var row = context.ViewModel.Effects.FillRows.Single(row => row.Target == target);
        var source = session.DocumentSnapshot;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)row.ToggleAnimationCommand).ExecuteAsync(null);

        Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Null(session.SelectedLayer.Tracks[0].Target.TextRangeId);
        Assert.True(session.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
        Assert.Equal(target, session.SceneEditing.Target);
    }

    [AvaloniaFact]
    public async Task NumericTitleDragPreviewsAndCommitsOnceWhileCancelAndGlobalCommitRestoreOriginal()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context);
        var row = context.ViewModel.Effects.TypographyRows.Single(row => row.Target.Property == AnimationProperty.FONT_SIZE);
        var panel = new EffectsPanelView(context.ViewModel.Effects, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = panel };
        host.Show();
        try
        {
            context.ViewModel.Effects.TypographyExpanded = true;
            host.UpdateLayout();
            var rowView = host.GetVisualDescendants().OfType<EffectPropertyRowView>().Single(view => view.DataContext == row);
            var label = rowView.GetVisualDescendants().OfType<NumericDragLabel>().Single();
            label.BringIntoView();
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = label.TranslatePoint(new(label.Bounds.Width / 2, label.Bounds.Height / 2), host)!.Value;
            var source = context.Session.DocumentSnapshot;
            var originalText = row.X.RawText;
            var originalSize = source.Subtitles[0].Style.FontSize;
            Assert.True(ReferenceEquals(label, host.InputHitTest(point)), $"Hit={host.InputHitTest(point)}; Point={point}; Host={host.Bounds}; Label={label.Bounds}; Visible={label.IsEffectivelyVisible}; Enabled={label.IsEffectivelyEnabled}; Attached={label.IsAttachedToVisualTree()}");
            host.MouseDown(point, MouseButton.Left);
            Assert.True(label.Input!.IsTitleDragging);
            host.MouseMove(point + new Vector(20, 0));
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.Equal(originalSize + 20, double.Parse(row.X.RawText, System.Globalization.CultureInfo.CurrentCulture));
            host.MouseMove(point + new Vector(10, 0));
            host.MouseUp(point + new Vector(10, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalSize + 10, context.Session.SelectedCue!.Style.FontSize);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(source, context.Session.DocumentSnapshot);

            host.UpdateLayout();
            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(15, 0));
            UiTestActions.Press(host, Key.Escape);
            host.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            Assert.Equal(originalText, row.X.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);

            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(15, 0));
            Assert.True(context.Session.TryCommitDrafts());
            host.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalText, row.X.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.MouseUp(default, MouseButton.Left);
            panel.CancelGestures();
            host.Close();
            panel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ResetIsDisabledForStaticPropertyAndRestoresAnimatedFrameToBaseWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context);
        var session = context.Session;
        var target = new AnimationTrackTarget(AnimationProperty.FONT_SIZE);
        var row = context.ViewModel.Effects.TypographyRows.Single(row => row.Target == target);
        Assert.False(row.CanReset);
        var baseSize = session.SelectedCue!.Style.FontSize;
        session.Editor.SetKeyframe(session.SelectedLayer!.Id, target, new(MediaTime.Zero, baseSize + 20));
        Assert.True(row.CanReset);
        var source = session.DocumentSnapshot;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)row.ResetCommand).ExecuteAsync(null);

        Assert.Equal(baseSize, session.SelectedLayer!.Tracks.Single(track => track.Target == target).Keyframes[0].Value.Scalar);
        Assert.True(session.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task ScaleComponentTitleUsesOneHundredthStepAndOneUndo()
    {
        await using var context = new MainWindowTestContext();
        CreateSubtitle(context);
        var panel = new EffectsPanelView(context.ViewModel.Effects, context.Session);
        var host = new Window { Width = 650, Height = 620, Content = panel };
        host.Show();
        try
        {
            var input = UiTestActions.Find<VectorDraftInput>(host, "ScaleInput");
            input.BringIntoView();
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var title = input.GetVisualDescendants().OfType<NumericDragLabel>().Single(title => title.Text == "X");
            Assert.Equal(0.01m, title.Input!.Increment);
            var point = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
            var source = context.Session.DocumentSnapshot;
            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(8, 0));
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.Equal(1.08m, input.X);
            host.MouseUp(point + new Vector(8, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.08, context.Session.SelectedLayer!.Transform.Scale.X, 5);
            Assert.Equal(1, context.Session.SelectedLayer.Transform.Scale.Y);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.MouseUp(default, MouseButton.Left);
            panel.CancelGestures();
            host.Close();
            panel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task CanvasDragWithRangeStateSelectedMovesWholeSubtitleAndKeepsPanelTarget()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context, "字幕");
        var session = context.Session;
        session.Editor.Apply("Use scene-sized fixture", document => document with { Width = 1920, Height = 1080 });
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        session.Editor.SetSubtitleAnimationRange(session.SelectedCue!.Id, range);
        var target = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        context.ViewModel.Effects.Target = target;
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        await PresentAsync(context.Window, canvas);
        var source = session.DocumentSnapshot;
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(session.ProjectDirectory));
        var geometry = Assert.IsType<ProjectLayerGeometry>(renderer.GetLayerGeometry(source, MediaTime.Zero, session.SelectedLayer!.Id));
        var board = canvas.ProjectRectangle;
        var local = new Point(board.X + geometry.WorldPivot.X * board.Width / source.Width,
            board.Y + geometry.WorldPivot.Y * board.Height / source.Height);
        var point = canvas.TranslatePoint(local, context.Window)!.Value;
        Assert.True(ReferenceEquals(canvas, context.Window.InputHitTest(point)), $"Hit={context.Window.InputHitTest(point)}; Point={point}; Canvas={canvas.Bounds}; Board={board}; Pivot={geometry.WorldPivot}; Project={source.Width}x{source.Height}");
        var delta = new Vector(40 * board.Width / source.Width, -20 * board.Height / source.Height);
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(canvas.HasActiveDrag);
        context.Window.MouseMove(point + delta);
        context.Window.MouseUp(point + delta, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, session.SelectedLayer!.Transform.Position.X, 5);
        Assert.Equal(-20, session.SelectedLayer.Transform.Position.Y, 5);
        Assert.Equal(range, session.SelectedCue!.AnimationRanges.Single());
        Assert.Empty(session.SelectedLayer.Tracks);
        Assert.Equal(target, session.SceneEditing.Target);
        Assert.True(session.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
    }
    [AvaloniaFact]
    public void TimelineUsesSeparateRowsForRangeAndAppearanceState()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var subtitle = new SubtitleLine { Text = "字幕", End = new(4), AnimationRanges = [range] };
        var whole = new AnimationTrackTarget(AnimationProperty.FILL);
        var normal = whole with { TextRangeId = range.Id };
        var inactive = normal with { State = SubtitleAnimationState.INACTIVE };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End,
            Tracks = [new(whole, [new(new(1), SceneColor.White)]), new(normal, [new(new(1), SceneColor.White)]),
                new(inactive, [new(new(1), SceneColor.White)])]
        };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80 };
        timeline.SetDocument(new() { Subtitles = [subtitle], Layers = [layer] }, subtitle.Id, layer);
        var window = new Window { Width = 850, Height = 500, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            var points = new[] { whole, normal, inactive }.Select(target =>
                timeline.GetKeyframePoint(layer.Id, target, new(1), SceneColor.White)!.Value).ToArray();
            Assert.Equal(3, points.Select(point => point.Y).Distinct().Count());
            AnimationTrackTarget? selected = null;
            timeline.KeyframeSelected += (_, e) => selected = e.Target;
            window.MouseDown(points[2], MouseButton.Left);
            window.MouseUp(points[2], MouseButton.Left);
            Assert.Equal(inactive, selected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PropertyTableFitsNarrowAndNormalPanelsAndCapturesBothThemes()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        CreateSubtitle(context);
        var model = context.ViewModel.Effects;
        model.FillExpanded = true;
        model.StrokeExpanded = true;
        model.ShadowExpanded = true;
        Localization.SetLanguage("zh-CN");
        var view = new EffectsPanelView(model, context.Session);
        var window = new Window { Width = 384, Height = 920, Content = view };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                foreach (var width in new[] { 384, 260 })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.Null(view.FindControl<ComboBox>("PresetCombo"));
                    var settings = view.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.Classes.Contains("animation-options") && button.IsEffectivelyVisible).ToArray();
                    Assert.NotEmpty(settings);
                    foreach (var button in settings)
                    {
                        Assert.Equal(32, button.Bounds.Width);
                        Assert.Equal(32, button.Bounds.Height);
                        var point = button.TranslatePoint(default, view)!.Value;
                        Assert.InRange(point.X, 0, view.Bounds.Width - button.Bounds.Width);
                    }
                    var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Assert.True(Path.IsPathFullyQualified(directory));
                        Directory.CreateDirectory(directory);
                        using var frame = window.CaptureRenderedFrame();
                        Assert.NotNull(frame);
                        frame.Save(Path.Combine(directory, $"effects-property-table-{width}-{theme.Key}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
            }
        }
        finally
        {
            window.Close();
            view.Dispose();
            Localization.SetLanguage("en-US");
        }
    }

    private static void CreateSubtitle(MainWindowTestContext context, string text = "Subtitle ABC 中文 123")
    {
        var id = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(5), text);
        context.Session.SelectCue(id);
        context.Window.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
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
