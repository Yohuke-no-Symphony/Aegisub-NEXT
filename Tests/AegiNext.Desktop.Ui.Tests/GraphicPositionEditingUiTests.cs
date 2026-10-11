using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class GraphicPositionEditingUiTests
{
    [AvaloniaTheory]
    [InlineData(LayerKind.IMAGE, false)]
    [InlineData(LayerKind.SHAPE, false)]
    [InlineData(LayerKind.SHAPE, true)]
    public async Task ShiftPresetPreservesGraphicCornersAndCommitsOneUndo(LayerKind kind, bool path)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/anchor.png");
        var layer = CreateLayer(kind, path, asset.Id);
        var original = new ProjectDocument { Width = 640, Height = 360, Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(original);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 380, Height = 760, Content = panel };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        try
        {
            window.Show();
            var editor = Assert.Single(panel.GetVisualDescendants().OfType<LayerPositionEditor>());
            Assert.False(editor.FindControl<Grid>("PositionModeRow")!.IsVisible);
            Assert.True(editor.Draft!.CanSelectPreset);
            var before = renderer.GetLayerGeometry(original, MediaTime.Zero, layer.Id)!;
            var button = UiTestActions.Find<Button>(panel, "AnchorPresetMiddleCenter");
            button.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            var changed = context.Session.SelectedLayer!;
            Assert.Equal(new ScenePoint(0.5, 0.5), changed.Position!.Anchor);
            Assert.Equal(changed.Position.Anchor, changed.Position.Pivot);
            Assert.Same(layer.Transform, changed.Transform);
            var after = renderer.GetLayerGeometry(context.Session.DocumentSnapshot, MediaTime.Zero, layer.Id)!;
            for (var index = 0; index < before.WorldCorners.Count; index++)
            {
                Assert.InRange(Math.Abs(before.WorldCorners[index].X - after.WorldCorners[index].X), 0, 0.001);
                Assert.InRange(Math.Abs(before.WorldCorners[index].Y - after.WorldCorners[index].Y), 0, 0.001);
            }
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
            panel.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task InvalidPivotKeepsRawInputAndEscapeRestoresOnlyThatField(LayerKind kind)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/anchor.png");
        var layer = CreateLayer(kind, false, asset.Id);
        var original = new ProjectDocument { Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(original);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 380, Height = 760, Content = panel };
        try
        {
            window.Show();
            var position = Assert.Single(panel.GetVisualDescendants().OfType<LayerPositionEditor>()).Draft!;
            var input = UiTestActions.Find<NumericDraftInput>(panel, "PivotXInput");
            input.BringIntoView();
            window.UpdateLayout();
            Assert.True(input.FocusInput());
            Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).SelectAll();
            window.KeyTextInput("0.5e-");
            Dispatcher.UIThread.RunJobs();
            position.OffsetY.RawText = "27";
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Equal("0.5e-", position.PivotX.RawText);
            Assert.Same(original, context.Session.DocumentSnapshot);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("0", position.PivotX.RawText);
            Assert.Equal("27", position.OffsetY.RawText);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(27, context.Session.SelectedLayer!.Position!.Offset.Y);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            window.Close();
            panel.Dispose();
        }
    }

    private static ProjectLayer CreateLayer(LayerKind kind, bool path, Guid assetId)
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 160, 80);
        if (path)
        {
            shape = shape with { Kind = ShapeKind.PATH, Path = ShapeGeometry.CreatePath(shape) };
        }
        return new()
        {
            Kind = kind,
            Image = kind == LayerKind.IMAGE ? new(assetId, 160, 80) : null,
            Shape = kind == LayerKind.SHAPE ? shape : null,
            Transform = new(X: 210, Y: 120, ScaleX: 1.3, ScaleY: 0.7, Rotation: 31, AnchorX: 3, AnchorY: -2)
        };
    }
}
