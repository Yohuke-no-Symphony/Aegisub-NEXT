using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskEffectCanvasUiTests
{
    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void RectangleHandleChecksEveryAffectedCornerTarget(int corner, bool editable)
    {
        var document = Document();
        var layer = document.Layers[0] with { Effects = [Instance("mask-rectangle-top-left (100, 50)")] };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var starts = 0;
        var commits = 0;
        canvas.MaskGestureStarting += (_, _) => starts++;
        canvas.MaskEdited += (_, _) => commits++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = Screen(canvas, corner == 1 ? new(250, 50) : new(250, 150));
            window.MouseDown(point, MouseButton.Left);
            Assert.Equal(editable, canvas.HasActiveDrag);
            Assert.Equal(editable ? 1 : 0, starts);
            var last = point + new Vector(30, 20);
            window.MouseMove(last);
            window.MouseUp(last, MouseButton.Left);
            Assert.Equal(editable ? 1 : 0, commits);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, 0, false)]
    [InlineData(true, 3, true)]
    [InlineData(false, 0, true)]
    public void CoveredPositionRejectsTheGestureBeforeStartingButInactiveInstancesAllowIt(bool enabled, int time, bool editable)
    {
        var document = Document();
        var layer = document.Layers[0] with { Effects = [Instance("mask-position (30, 40)") with { End = new(2), IsEnabled = enabled }] };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(time));
        var visible = SceneEvaluator.EvaluateMask(layer, new(time), document.Subtitles[0])!;
        var starts = 0;
        canvas.MaskGestureStarting += (_, _) => starts++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var projection = ClipMaskPlacementGeometry.CreateMatrix(visible, new(document.Width, document.Height));
            var point = Screen(canvas, projection.Map(new(175, 100)));
            window.MouseDown(point, MouseButton.Left);
            Assert.Equal(editable, canvas.HasActiveDrag);
            Assert.Equal(editable ? 1 : 0, starts);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RealMaskDragBuildsAnInverseNativePreviewAndPublishesOneVisiblePose()
    {
        var document = Document();
        var track = new AnimationTrack(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(10, 20))]);
        var layer = document.Layers[0] with
        {
            Tracks = [track],
            Effects = [Instance("mask-position factor(2, 3)"), Instance("mask-position offset(10, -5)")]
        };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var visible = SceneEvaluator.EvaluateMask(layer, MediaTime.Zero, document.Subtitles[0])!;
        CanvasMaskEditEventArgs? committed = null;
        var commits = 0;
        canvas.MaskEdited += (_, e) => { committed = e; commits++; };
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var projection = ClipMaskPlacementGeometry.CreateMatrix(visible, new(document.Width, document.Height));
            var point = Screen(canvas, projection.Map(new(175, 100)));
            var last = point + new Vector(24, 18);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(last);
            Assert.True(canvas.HasActiveDrag);
            Assert.Null(committed);
            var delta = new ScenePoint(24 * document.Width / canvas.ProjectRectangle.Width, 18 * document.Height / canvas.ProjectRectangle.Height);
            var expected = visible with
            {
                Transform = visible.Transform with { Position = new(visible.Transform.Position.X + delta.X, visible.Transform.Position.Y + delta.Y) }
            };

            var preview = canvas.PrepareMaskDraftLayer(expected);

            Assert.Empty(preview.Tracks);
            Assert.Equal(layer.Effects, preview.Effects);
            var previewMask = SceneEvaluator.EvaluateMask(preview, MediaTime.Zero, document.Subtitles[0])!;
            Assert.Equal(expected.Transform.Position.X, previewMask.Transform.Position.X, 8);
            Assert.Equal(expected.Transform.Position.Y, previewMask.Transform.Position.Y, 8);
            Assert.Same(track, Assert.Single(layer.Tracks));
            window.MouseUp(last, MouseButton.Left);
            Assert.Equal(1, commits);
            Assert.NotNull(committed);
            Assert.Equal(expected.Transform.Position.X, committed.Mask.Transform.Position.X, 8);
            Assert.Equal(expected.Transform.Position.Y, committed.Mask.Transform.Position.Y, 8);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void NodeEffectTopologyLockAlsoControlsRealContourCreation(bool enabled)
    {
        var document = Document();
        var layer = document.Layers[0] with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(100, 50) }, new() { Position = new(250, 150) }] }] },
            Effects = [Instance("mask-node(1,1).position offset(10, 20)") with { IsEnabled = enabled }]
        };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_DRAW_VECTOR };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var starts = 0;
        canvas.MaskGestureStarting += (_, _) => starts++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = Screen(canvas, new(400, 250));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            Assert.Equal(!enabled, canvas.HasOpenMaskContour);
            Assert.Equal(enabled ? 0 : 1, starts);
        }
        finally
        {
            window.Close();
        }
    }

    private static Point Screen(EffectCanvasControl canvas, ScenePoint point)
    {
        var board = canvas.ProjectRectangle;
        return new(board.X + point.X * board.Width / 1000, board.Y + point.Y * board.Height / 500);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { Text = "Mask", End = new(5) };
        return new()
        {
            Width = 1000, Height = 500, Subtitles = [line], Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Mask = new RectangleClipMask { TopLeft = new(100, 50), BottomRight = new(250, 150) }
            }]
        };
    }

    private static EffectInstance Instance(string value) => new()
    {
        Name = "Mask gesture", End = new(5),
        Source = "effect \"mask-edit\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
            "at 0 " + value + "\nat 1 " + value + "\nend\nend"
    };
}
