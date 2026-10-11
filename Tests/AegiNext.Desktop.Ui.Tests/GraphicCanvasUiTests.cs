using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class GraphicCanvasUiTests
{
    [AvaloniaFact]
    public void CropCornerCommitsNormalizedSourceCoordinatesOnlyOnRelease()
    {
        using var environment = new UiTestEnvironment();
        using (var bitmap = new SKBitmap(2, 2))
        {
            bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(environment.DirectoryPath, "test.png"), encoded.ToArray());
        }
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "test.png");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 80, 80), Transform = new(20, 20) };
        var document = new ProjectDocument { Width = 200, Height = 200, Assets = [asset], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.CROP };
        canvas.SetScene(document, layer, MediaTime.Zero, environment.DirectoryPath);
        var edits = new List<CanvasGraphicEditEventArgs>();
        canvas.GraphicEdited += (_, e) => edits.Add(e);
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, new(100, 100));
            var end = Project(canvas, new(60, 80));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            window.MouseUp(end, MouseButton.Left);
            var crop = Assert.Single(edits).Image!.Crop;
            Assert.Equal(0.5, crop.Width, 5);
            Assert.Equal(0.75, crop.Height, 5);
            Assert.Equal(1, layer.Image.Crop.Width);
            Assert.Equal(1, layer.Image.Crop.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleResizePublishesDimensionsOnlyOnReleaseAndEscapeCancels(bool cancel)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 80, 60) { CornerRadii = new(4, 8, 12, 16) },
            Transform = new(20, 20)
        };
        var document = new ProjectDocument { Width = 200, Height = 200, Layers = [layer] };
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, new(100, 80));
            var end = Project(canvas, new(140, 100));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            if (cancel)
            {
                UiTestActions.Press(window, Key.Escape);
            }
            window.MouseUp(end, MouseButton.Left);
            if (cancel)
            {
                Assert.Empty(edits);
            }
            else
            {
                var result = Assert.Single(edits);
                Assert.Equal(layer.Transform, result.Transform);
                Assert.Equal(120, result.Shape!.Width, 5);
                Assert.Equal(80, result.Shape.Height, 5);
                Assert.Equal(layer.Shape.CornerRadii, result.Shape.CornerRadii);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickSelectsTheFrontmostVisibleContour()
    {
        var back = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 80), Transform = new(20, 20) };
        var frontTrack = new ProjectTrack();
        var front = back with { Id = Guid.NewGuid(), TrackId = frontTrack.Id, Shape = new(ShapeKind.ELLIPSE, 80, 80) };
        var document = new ProjectDocument { Width = 200, Height = 200, Tracks = [frontTrack, ProjectTrack.Default], Layers = [back, front] };
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, null, MediaTime.Zero);
        var selected = new List<Guid>();
        canvas.LayerSelectionRequested += (_, e) => selected.Add(e.LayerId);
        var window = Open(canvas);
        try
        {
            Click(window, Project(canvas, new(60, 60)));
            Click(window, Project(canvas, new(21, 21)));
            Assert.Equal(new[] { front.Id, back.Id }, selected.ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShapeCreationIsOneReleaseEventAndEscapeDiscardsItsDraft(bool cancel)
    {
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.CREATE_SHAPE, CreationShapeKind = ShapeKind.ROUNDED_RECTANGLE };
        canvas.SetScene(new() { Width = 200, Height = 200 }, null, MediaTime.Zero);
        var created = new List<CanvasShapeCreatedEventArgs>();
        canvas.ShapeCreated += (_, e) => created.Add(e);
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, new(20, 30));
            var end = Project(canvas, new(100, 90));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(created);
            if (cancel)
            {
                UiTestActions.Press(window, Key.Escape);
            }
            window.MouseUp(end, MouseButton.Left);
            Assert.False(canvas.HasActiveDrag);
            if (cancel)
            {
                Assert.Empty(created);
            }
            else
            {
                var result = Assert.Single(created);
                Assert.Equal(ShapeKind.ROUNDED_RECTANGLE, result.Kind);
                Assert.Equal(80, result.Width, 5);
                Assert.Equal(60, result.Height, 5);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShapeNodeMovementPublishesOneUndoAndSelectionChangeCancels()
    {
        var path = ShapeGeometry.CreatePath(new(ShapeKind.RECTANGLE, 80, 80));
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.PATH, 80, 80, path), Transform = new(20, 20) };
        var document = new ProjectDocument { Width = 200, Height = 200, Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SHAPE_PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var commits = 0;
        canvas.GraphicEdited += (_, e) =>
        {
            commits++;
            editor.UpdateLayer(e.LayerId, item => item with { Shape = e.Shape });
        };
        var window = Open(canvas);
        try
        {
            var start = Project(canvas, new(20, 20));
            var end = Project(canvas, new(30, 40));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Equal(0, commits);
            window.MouseUp(end, MouseButton.Left);
            Assert.Equal(1, commits);
            Assert.Equal(10, editor.Snapshot.Layers[0].Shape!.Path!.Start.X, 5);
            Assert.Equal(20, editor.Snapshot.Layers[0].Shape!.Path!.Start.Y, 5);
            Assert.True(editor.Undo());
            Assert.False(editor.CanUndo);
            canvas.SetScene(document, layer, MediaTime.Zero);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            canvas.SetScene(document, null, MediaTime.Zero);
            window.MouseUp(end, MouseButton.Left);
            Assert.Equal(1, commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FreePathKeepsClicksAsDraftUntilExplicitCompletion()
    {
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.CREATE_PATH };
        canvas.SetScene(new() { Width = 200, Height = 200 }, null, MediaTime.Zero);
        var created = new List<CanvasShapeCreatedEventArgs>();
        canvas.ShapeCreated += (_, e) => created.Add(e);
        var window = Open(canvas);
        try
        {
            Click(window, Project(canvas, new(20, 20)));
            Click(window, Project(canvas, new(100, 20)));
            Assert.Empty(created);
            UiTestActions.Press(window, Key.Enter);
            var result = Assert.Single(created);
            Assert.NotNull(result.Path);
            Assert.False(result.Path.Closed);
            Assert.Single(result.Path.Segments);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Open(EffectCanvasControl canvas)
    {
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Point Project(EffectCanvasControl canvas, Point value)
    {
        var board = canvas.ProjectRectangle;
        return new(board.X + value.X * board.Width / 200, board.Y + value.Y * board.Height / 200);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
