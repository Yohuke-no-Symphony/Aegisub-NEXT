using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class DrawingToolsPanelUiTests
{
    [AvaloniaFact]
    public async Task FloatingToolboxFinishesTheCanvasPathOnceWithoutDiscardingItOnFocusChange()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var source = new ProjectDocument { Width = 200, Height = 200 };
        context.Session.Editor.Reset(source);
        context.Window.Layouts.Float(WorkbenchPanelIds.DRAWING_TOOLS);
        Flush(context.Window);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        UiTestActions.Click(floating, "PathToolButton");
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        ClickCanvas(context.Window, canvas, new(20, 20));
        ClickCanvas(context.Window, canvas, new(140, 80));
        Assert.True(canvas.HasActiveDrag);
        Assert.Empty(context.Session.DocumentSnapshot.Layers);

        UiTestActions.Click(floating, "FinishShapePathButton");

        var path = Assert.Single(context.Session.DocumentSnapshot.Layers).Shape!.Path!;
        Assert.Single(path.Segments);
        Assert.False(path.Closed);
        Assert.False(canvas.HasActiveDrag);
        Assert.False(context.ViewModel.DrawingTools.FinishPathCommand.CanExecute(null));
        context.ViewModel.DrawingTools.FinishPathCommand.Execute(null);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task PathButtonsEditTheSelectedSegmentAndEachActionHasOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var layer = PathLayer();
        var source = new ProjectDocument { Width = 200, Height = 200, Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        UiTestActions.Click(context.Window, "EditShapePathToolButton");
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        ClickCanvas(context.Window, canvas, new(130, 90));

        UiTestActions.Click(context.Window, "CurveSegmentButton");

        var curved = context.Session.SelectedLayer!.Shape!.Path!;
        Assert.Equal(layer.Shape!.Path!.Segments[0], curved.Segments[0]);
        Assert.NotEqual(layer.Shape.Path.Segments[1], curved.Segments[1]);
        UiTestActions.Click(context.Window, "StraightSegmentButton");
        var straight = context.Session.SelectedLayer!.Shape!.Path!;
        Assert.Equal(layer.Shape.Path.Segments[1], straight.Segments[1]);
        UiTestActions.Click(context.Window, "CloseShapePathButton");
        Assert.True(context.Session.SelectedLayer!.Shape!.Path!.Closed);
        Assert.True(context.Session.Editor.Undo());
        Assert.Equal(straight, context.Session.SelectedLayer!.Shape!.Path);
        Assert.True(context.Session.Editor.Undo());
        Assert.Equal(curved, context.Session.SelectedLayer!.Shape!.Path);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvisiblePreviewCancelsPathDraftAndRejectsStaleCommands(bool hideWithLayout)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var layer = PathLayer();
        var source = new ProjectDocument { Width = 200, Height = 200, Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        UiTestActions.Click(context.Window, "PathToolButton");
        var preview = context.Window.Panels[WorkbenchPanelIds.PREVIEW];
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        ClickCanvas(context.Window, canvas, new(20, 150));
        ClickCanvas(context.Window, canvas, new(140, 180));
        Assert.True(canvas.HasActiveDrag);

        if (hideWithLayout)
        {
            context.Window.Layouts.Hide(WorkbenchPanelIds.PREVIEW);
        }
        else
        {
            preview.IsVisible = false;
        }
        Flush(context.Window);
        Assert.False(canvas.HasActiveDrag);
        var tools = context.ViewModel.DrawingTools;
        Assert.False(tools.FinishPathCommand.CanExecute(null));
        tools.FinishPathCommand.Execute(null);
        context.Session.SetGraphicCanvasMode(CanvasEditMode.SHAPE_PATH);
        Assert.False(tools.CurveSegmentCommand.CanExecute(null));
        tools.CurveSegmentCommand.Execute(null);
        tools.ToggleClosedPathCommand.Execute(null);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);

        if (hideWithLayout)
        {
            context.Window.Layouts.Show(WorkbenchPanelIds.PREVIEW);
        }
        else
        {
            preview.IsVisible = true;
        }
        Flush(context.Window);
        UiTestActions.Click(context.Window, "PathToolButton");
        ClickCanvas(context.Window, canvas, new(20, 150));
        ClickCanvas(context.Window, canvas, new(140, 180));
        UiTestActions.Click(context.Window, "FinishShapePathButton");
        Assert.Equal(2, context.Session.DocumentSnapshot.Layers.Length);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task HidingPreviewDuringCropCancelsTheDraftAndTheNextDragCommitsOnce()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "drawing-tools-crop.png");
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        using (var bitmap = new SKBitmap(2, 2))
        {
            bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(context.Session.ProjectDirectory, asset.RelativePath), encoded.ToArray());
        }
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 80, 80), Transform = new(20, 20) };
        var source = new ProjectDocument { Width = 200, Height = 200, Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        UiTestActions.Click(context.Window, "CropImageToolButton");
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        var start = CanvasPoint(context.Window, canvas, new(100, 100));
        var end = CanvasPoint(context.Window, canvas, new(60, 80));
        context.Window.MouseDown(start, MouseButton.Left);
        context.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.True(canvas.HasActiveDrag,
            $"Mode={canvas.EditMode}; Start={start}; End={end}; Project={canvas.ProjectRectangle}; Hit={context.Window.InputHitTest(start)?.GetType().Name}");

        context.Window.Layouts.Hide(WorkbenchPanelIds.PREVIEW);
        context.Window.MouseUp(end, MouseButton.Left);
        Flush(context.Window);
        Assert.False(canvas.HasActiveDrag);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);

        context.Window.Layouts.Show(WorkbenchPanelIds.PREVIEW);
        Flush(context.Window);
        UiTestActions.Click(context.Window, "CropImageToolButton");
        start = CanvasPoint(context.Window, canvas, new(100, 100));
        end = CanvasPoint(context.Window, canvas, new(60, 80));
        context.Window.MouseDown(start, MouseButton.Left);
        context.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        context.Window.MouseUp(end, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(0.5, context.Session.SelectedLayer!.Image!.Crop.Width, 5);
        Assert.Equal(0.75, context.Session.SelectedLayer.Image.Crop.Height, 5);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InvalidStyleDraftPreservesModeShapeTypeAndCheckedTool()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 60) };
        var source = new ProjectDocument { Width = 200, Height = 200, Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        UiTestActions.Click(context.Window, "RectangleToolButton");
        var style = context.ViewModel.Styles.Shape;
        style.Width.RawText = "7e-";

        UiTestActions.Click(context.Window, "EllipseToolButton");

        Assert.Equal(CanvasEditMode.CREATE_SHAPE, context.ViewModel.DrawingTools.ActiveMode);
        Assert.Equal(ShapeKind.RECTANGLE, context.Session.SceneEditing.CreationShapeKind);
        Assert.True(UiTestActions.Find<ToolbarToggleButton>(context.Window, "RectangleToolButton").IsChecked);
        Assert.False(UiTestActions.Find<ToolbarToggleButton>(context.Window, "EllipseToolButton").IsChecked);
        Assert.Equal("7e-", style.Width.RawText);
        Assert.Same(source, context.Session.DocumentSnapshot);
        style.RestoreField("LayerWidthInput");
        UiTestActions.Click(context.Window, "SelectGraphicButton");
        style.Width.RawText = "7e-";
        UiTestActions.Click(context.Window, "PathToolButton");
        Assert.Equal(CanvasEditMode.POSITION, context.ViewModel.DrawingTools.ActiveMode);
        Assert.Equal("7e-", style.Width.RawText);
        style.RestoreField("LayerWidthInput");
        UiTestActions.Click(context.Window, "EllipseToolButton");
        Assert.Equal(ShapeKind.ELLIPSE, context.ViewModel.DrawingTools.ActiveShapeKind);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public async Task SingleRowToolbarKeepsToolsAccessibleAndLanguageChangesPreserveSelection(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(context.Session.Preferences with { Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT });
        Localization.SetLanguage(language);
        var layer = PathLayer();
        var source = new ProjectDocument { Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        UiTestActions.Click(context.Window, "ArrowToolButton");
        var panel = context.Window.Panels[WorkbenchPanelIds.DRAWING_TOOLS];
        var toggles = panel.GetVisualDescendants().OfType<ToolbarToggleButton>().ToArray();
        Assert.Equal(8, toggles.Length);
        Assert.Null(panel.FindControl<ToolbarToggleButton>("RoundedRectangleToolButton"));
        AssertSingleButtonRow(panel, 9);
        foreach (var button in toggles)
        {
            Assert.True(button.IsEffectivelyVisible);
            var position = button.TranslatePoint(default, panel)!.Value;
            Assert.InRange(position.X, 0, panel.Bounds.Width - button.Bounds.Width);
            Assert.Equal(32, button.Bounds.Width);
            Assert.Equal(32, button.Bounds.Height);
        }
        Assert.Null(context.Window.Panels[WorkbenchPanelIds.PREVIEW].FindControl<ComboBox>("CanvasToolCombo"));
        Localization.SetLanguage(language == "en-US" ? "zh-CN" : "en-US");
        Flush(context.Window);
        Assert.True(UiTestActions.Find<ToolbarToggleButton>(panel, "ArrowToolButton").IsChecked);
        var arrow = UiTestActions.Find<ToolbarToggleButton>(panel, "ArrowToolButton");
        Assert.Equal(Localization.Get("Preview.CreateArrow"), ToolTip.GetTip(arrow));
        Assert.Equal(Localization.Get("Preview.CreateArrow"), AutomationProperties.GetName(arrow));
        Assert.Equal(ShapeKind.ARROW, context.ViewModel.DrawingTools.ActiveShapeKind);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Localization.SetLanguage(language);
        if (Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            Flush(context.Window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = context.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"drawing-tools-{language}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
        }

        UiTestActions.Click(context.Window, "PathToolButton");
        Flush(context.Window);
        AssertSingleButtonRow(panel, 10);
        Assert.Equal(Localization.Get("Preview.FinishPath"), ToolTip.GetTip(UiTestActions.Find<Button>(panel, "FinishShapePathButton")));
        UiTestActions.Click(context.Window, "EditShapePathToolButton");
        Flush(context.Window);
        AssertSingleButtonRow(panel, 12);

        context.Window.Layouts.Float(WorkbenchPanelIds.DRAWING_TOOLS);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        floating.Width = 260;
        Flush(floating);
        AssertSingleButtonRow(panel, 12);
        var scroller = UiTestActions.Find<ScrollViewer>(panel, "DrawingToolsScrollViewer");
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        UiTestActions.Click(floating, "ArrowToolButton");
        Flush(floating);
        Assert.True(scroller.Offset.X > 0);
        Assert.Equal(0, scroller.Offset.Y);
        Assert.True(arrow.IsChecked);
        var visiblePosition = arrow.TranslatePoint(default, scroller)!.Value;
        Assert.InRange(visiblePosition.X, -1, scroller.Viewport.Width - arrow.Bounds.Width + 1);
        AssertSingleButtonRow(panel, 9);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void AssertSingleButtonRow(Control panel, int count)
    {
        var buttons = panel.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible &&
            (button is ToolbarToggleButton || button.Classes.Contains("icon-button"))).ToArray();
        Assert.Equal(count, buttons.Length);
        var rowY = buttons[0].TranslatePoint(default, panel)!.Value.Y;
        foreach (var button in buttons)
        {
            Assert.InRange(Math.Abs(rowY - button.TranslatePoint(default, panel)!.Value.Y), 0, 1);
            Assert.Equal(32, button.Bounds.Width);
            Assert.Equal(32, button.Bounds.Height);
        }
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
    }

    private static ProjectLayer PathLayer()
    {
        var path = new PathGeometry(new(0, 0),
        [
            new(new(110d / 3, 0), new(220d / 3, 0), new(110, 0)),
            new(new(110, 100d / 3), new(110, 200d / 3), new(110, 100))
        ]);
        return new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.PATH, 110, 100, path), Transform = new(20, 20), StrokeWidth = 2 };
    }

    private static void ClickCanvas(Window window, EffectCanvasControl canvas, Point point)
    {
        var position = CanvasPoint(window, canvas, point);
        window.MouseDown(position, MouseButton.Left);
        window.MouseUp(position, MouseButton.Left);
        Flush(window);
    }

    private static Point CanvasPoint(Window window, EffectCanvasControl canvas, Point point)
    {
        Flush(window);
        var board = canvas.ProjectRectangle;
        var local = new Point(board.X + point.X * board.Width / 200, board.Y + point.Y * board.Height / 200);
        return canvas.TranslatePoint(local, window)!.Value;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
