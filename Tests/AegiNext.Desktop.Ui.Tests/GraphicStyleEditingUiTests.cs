using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Styles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class GraphicStyleEditingUiTests
{
    [AvaloniaFact]
    public async Task LinkedImageTitleDragRestoresBothOriginalTextsOnEscapeAndCommitsOneUndoOnRelease()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/ui-test.png");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100) };
        var source = new ProjectDocument { Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(source);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 390, Height = 900, Content = panel };
        try
        {
            window.Show();
            var model = context.ViewModel.Styles.Image;
            using (context.Session.BeginWorkbenchUpdate())
            {
                model.Width.RawText = "200.0000";
                model.Height.RawText = "100.0000";
            }
            var width = UiTestActions.Find<NumericDraftInput>(panel, "LayerWidthInput");
            var end = BeginDrag(window, width);
            Assert.True(width.IsTitleDragging);
            Assert.NotEqual("100.0000", model.Height.RawText);
            UiTestActions.Press(window, Key.Escape);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("200.0000", model.Width.RawText);
            Assert.Equal("100.0000", model.Height.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            end = BeginDrag(window, width);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(212, context.Session.SelectedLayer!.Image!.Width);
            Assert.Equal(106, context.Session.SelectedLayer.Image.Height);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
            panel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MountedImageSizeLocksItsRatioAndCropEscapePreservesTheOtherDraft()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/ui-test.png");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100) };
        context.Session.Editor.Reset(new() { Assets = [asset], Layers = [layer] });
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 390, Height = 900, Content = panel };
        try
        {
            window.Show();
            Type(window, UiTestActions.Find<NumericDraftInput>(panel, "LayerWidthInput"), "300");
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, context.Session.SelectedLayer!.Image!.Width);
            Assert.Equal(150, context.Session.SelectedLayer.Image.Height);
            var model = context.ViewModel.Styles.Image;
            model.CropWidth.RawText = "60";
            model.CropX.RawText = "invalid";
            Assert.False(context.Session.TryCommitDrafts(false));
            panel.FocusInvalidField("ImageCropXInput");
            window.UpdateLayout();
            var cropX = UiTestActions.Find<NumericDraftInput>(panel, "ImageCropXInput");
            Assert.True(cropX.IsEffectivelyVisible);
            Assert.True(cropX.FocusInput());
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0", model.CropX.RawText);
            Assert.Equal("60", model.CropWidth.RawText);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(0.6, context.Session.SelectedLayer.Image.Crop.Width);
            UiTestActions.Click(window, "ImageEditCropButton");
            Assert.Equal(CanvasEditMode.CROP, context.ViewModel.Preview.Scene.Mode);
        }
        finally
        {
            context.ViewModel.Styles.Image.RestoreField("ImageCropXInput");
            window.Close();
            panel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ImageFitAndShapeTypeChoicesKeepTheirModelsDuringLanguageChanges()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/ui-test.png");
        var image = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100, ImageFit.STRETCH) };
        var track = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer { TrackId = track.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ARROW, 100, 60) };
        context.Session.Editor.Reset(new() { Tracks = [ProjectTrack.Default, track], Assets = [asset], Layers = [image, shape] });
        context.Session.SelectLayer(image.Id, [image.Id]);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 390, Height = 900, Content = panel };
        try
        {
            window.Show();
            Assert.Equal((int)ImageFit.STRETCH, UiTestActions.Find<ComboBox>(panel, "ImageFitInput").SelectedIndex);
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = "en-US" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((int)ImageFit.STRETCH, UiTestActions.Find<ComboBox>(panel, "ImageFitInput").SelectedIndex);
            Assert.False(context.Session.HasProjectDrafts);
            context.Session.SelectLayer(shape.Id, [shape.Id]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, UiTestActions.Find<ComboBox>(panel, "ShapeTypeInput").SelectedIndex);
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, UiTestActions.Find<ComboBox>(panel, "ShapeTypeInput").SelectedIndex);
            Assert.Equal(ShapeKind.ARROW, context.Session.SelectedLayer!.Shape!.Kind);
            Assert.False(context.Session.HasProjectDrafts);
        }
        finally
        {
            window.Close();
            panel.Dispose();
        }
    }

    private static void Type(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        Assert.True(input.FocusInput());
        Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).SelectAll();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point BeginDrag(Window window, NumericDraftInput input)
    {
        var label = Assert.Single(window.GetVisualDescendants().OfType<NumericDragLabel>(), value => value.Input == input);
        label.BringIntoView();
        window.UpdateLayout();
        var start = label.TranslatePoint(new(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        var end = start + new Vector(12, 0);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        Dispatcher.UIThread.RunJobs();
        return end;
    }
}
