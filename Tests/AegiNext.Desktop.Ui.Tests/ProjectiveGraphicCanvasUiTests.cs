using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ProjectiveGraphicCanvasUiTests
{
    [AvaloniaFact]
    public void SpatialCropUsesTheFrozenInverseProjectionAndCommitsOnce()
    {
        using var environment = new UiTestEnvironment();
        using (var bitmap = new SKBitmap(2, 2))
        {
            bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(environment.DirectoryPath, "crop.png"), encoded.ToArray());
        }
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "crop.png");
        var layer = Layer() with { Kind = LayerKind.IMAGE, Image = new(asset.Id, 100, 80), Shape = null };
        var document = new ProjectDocument { Width = 400, Height = 300, Assets = [asset], Layers = [layer] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(environment.DirectoryPath));
        var geometry = renderer.GetLayerGeometry(document, new(), layer.Id)!;
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.CROP };
        canvas.SetScene(document, layer, new(), environment.DirectoryPath);
        var edits = new List<CanvasGraphicEditEventArgs>();
        canvas.GraphicEdited += (_, edit) => edits.Add(edit);
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, document, geometry, new(100, 80));
            var current = Project(canvas, document, geometry, new(65, 50));
            window.MouseDown(start, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(current, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            window.MouseUp(current, MouseButton.Left);

            var crop = Assert.Single(edits).Image!.Crop;
            Assert.Equal(0.65, crop.Width, 5);
            Assert.Equal(0.625, crop.Height, 5);
            Assert.Equal(new NormalizedImageCrop(), layer.Image!.Crop);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpatialRectangleResizePublishesCompensationOnReleaseAndEscapeDiscardsIt(bool cancel)
    {
        using var environment = new UiTestEnvironment();
        var layer = Layer();
        var document = new ProjectDocument { Width = 400, Height = 300, Layers = [layer] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(environment.DirectoryPath));
        var geometry = renderer.GetLayerGeometry(document, new(), layer.Id)!;
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, layer, new(), environment.DirectoryPath);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, edit) => edits.Add(edit);
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, document, geometry, new(60, 40));
            var current = Project(canvas, document, geometry, new(90, 60));
            window.MouseDown(start, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(current, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            if (cancel)
            {
                UiTestActions.Press(window, Key.Escape);
            }
            window.MouseUp(current, MouseButton.Left);
            Assert.False(canvas.HasActiveDrag);
            if (cancel)
            {
                Assert.Empty(edits);
            }
            else
            {
                var edit = Assert.Single(edits);
                Assert.Equal(90, edit.Shape!.Width, 4);
                Assert.Equal(60, edit.Shape.Height, 4);
                Assert.Equal(layer.Transform.Position, edit.Transform.Position);
                Assert.NotEqual(layer.Transform.ProjectionTranslation, edit.Transform.ProjectionTranslation);
                var changed = layer with { Shape = edit.Shape, Transform = edit.Transform };
                var after = renderer.GetLayerGeometry(document with { Layers = [changed] }, new(), layer.Id)!;
                var fixedBefore = Project(canvas, document, geometry, new());
                var fixedAfter = Project(canvas, document, after, new());
                Assert.Equal(fixedBefore.X, fixedAfter.X, 4);
                Assert.Equal(fixedBefore.Y, fixedAfter.Y, 4);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShiftClickSplitsTheActualPerspectiveCurveAtItsSourceParameter()
    {
        using var environment = new UiTestEnvironment();
        var path = new PathGeometry(new(0, 0), [new(new(0, 100), new(100, -50), new(100, 60))]);
        var layer = Layer() with { Shape = new(ShapeKind.PATH, 100, 100, path) };
        var document = new ProjectDocument { Width = 400, Height = 300, Layers = [layer] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(environment.DirectoryPath));
        var geometry = renderer.GetLayerGeometry(document, new(), layer.Id)!;
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SHAPE_PATH };
        canvas.SetScene(document, layer, new(), environment.DirectoryPath);
        var edits = new List<CanvasGraphicEditEventArgs>();
        canvas.GraphicEdited += (_, edit) => edits.Add(edit);
        var window = Open(canvas);
        try
        {
            const double PROGRESS = 0.37;
            var source = SceneEvaluator.EvaluatePath(path, PROGRESS);
            var point = Project(canvas, document, geometry, source);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(edits);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);

            var result = Assert.Single(edits).Shape!.Path!;
            Assert.Equal(2, result.Segments.Length);
            Assert.Equal(source.X, result.Segments[0].End.X, 4);
            Assert.Equal(source.Y, result.Segments[0].End.Y, 4);
            var expected = PathOperations.SplitSegment(path, 0, PROGRESS);
            Assert.Equal(expected.Segments[1].Control2.X, result.Segments[1].Control2.X, 4);
            Assert.Equal(expected.Segments[1].Control2.Y, result.Segments[1].Control2.Y, 4);
            Assert.Single(layer.Shape!.Path!.Segments);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer Layer() => new()
    {
        Kind = LayerKind.SHAPE,
        Shape = new(ShapeKind.RECTANGLE, 60, 40),
        Position = new() { Pivot = new(0.5, 0.5) },
        Transform = new(200, 150)
        {
            RotationX = 30, RotationY = -30, PerspectiveDistance = 180
        }
    };

    private static Window Open(EffectCanvasControl canvas)
    {
        var window = new Window { Width = 524, Height = 424, Content = canvas };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static Point Project(EffectCanvasControl canvas, ProjectDocument document, ProjectLayerGeometry geometry, ScenePoint local)
    {
        Assert.True(geometry.TryMapLocalPoint(new((float)local.X, (float)local.Y), out var world));
        var board = canvas.ProjectRectangle;
        return new(board.X + world.X * board.Width / document.Width, board.Y + world.Y * board.Height / document.Height);
    }
}
