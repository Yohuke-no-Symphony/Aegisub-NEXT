using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskPlacementPresetUiTests
{
    [AvaloniaTheory]
    [InlineData("effects")]
    [InlineData("masks")]
    public async Task AllNinePresetsPreserveTheMaskAndItsAnimationAndUndoInOneStep(string origin)
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var source = context.Session.DocumentSnapshot;
        var before = Matrix(context);
        var editor = UiTestActions.Find<LayerPositionEditor>(context.Window.Panels[origin], "MaskPositionEditor");
        var other = UiTestActions.Find<LayerPositionEditor>(context.Window.Panels[origin == "effects" ? "masks" : "effects"], "MaskPositionEditor");
        Assert.Same(editor.Draft, other.Draft);
        var names = new[]
        {
            "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
            "BottomLeft", "BottomCenter", "BottomRight"
        };
        for (var index = 0; index < names.Length; index++)
        {
            Click(context, origin, names[index]);

            var layer = context.Session.SelectedLayer!;
            Assert.Equal(new ScenePoint(index % 3 / 2d, index / 3 / 2d), layer.Mask!.Placement.Anchor);
            Assert.Equal(new ScenePoint(0.5, 0.5), layer.Mask.Placement.Pivot);
            Assert.Equal(source.Layers[0].Tracks, layer.Tracks);
            Assert.Equal(source.Layers[0].Mask!.ReferenceFrame, layer.Mask.ReferenceFrame);
            AssertSamePoints(before, Matrix(context));
            Assert.Equal("Commit workspace drafts", context.Session.Editor.UndoLabel);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.Same(editor.Draft, other.Draft);
        }
    }

    [AvaloniaTheory]
    [InlineData("effects", true, false)]
    [InlineData("effects", false, true)]
    [InlineData("effects", true, true)]
    [InlineData("masks", true, false)]
    [InlineData("masks", false, true)]
    [InlineData("masks", true, true)]
    public async Task ActualShiftAndAltModifiersUpdateTheSharedPlacementWithoutChangingAnimation(string origin, bool shift, bool alt)
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var source = context.Session.DocumentSnapshot;
        var before = Matrix(context);
        var modifiers = (shift ? RawInputModifiers.Shift : RawInputModifiers.None) |
            (alt ? RawInputModifiers.Alt : RawInputModifiers.None);

        Click(context, origin, "TopRight", modifiers);

        var mask = context.Session.SelectedLayer!.Mask!;
        Assert.Equal(new ScenePoint(1, 0), mask.Placement.Anchor);
        Assert.Equal(shift ? new ScenePoint(1, 0) : new ScenePoint(0.5, 0.5), mask.Placement.Pivot);
        Assert.Equal(source.Layers[0].Tracks, context.Session.SelectedLayer.Tracks);
        if (alt)
        {
            Assert.Equal(default(ScenePoint), mask.Placement.Offset);
        }
        else
        {
            AssertSamePoints(before, Matrix(context));
        }
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    private static async Task PrepareAsync(MainWindowTestContext context)
    {
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var document = context.Session.DocumentSnapshot;
        var layer = context.Session.SelectedLayer!;
        var mask = ClipMaskPlacementGeometry.CreatePlacement(new RectangleClipMask
        {
            TopLeft = new(100, 50), BottomRight = new(300, 150),
            Transform = new() { Scale = new(1.5, 0.75), Rotation = 37 }
        }, new(document.Width, document.Height));
        mask = mask with
        {
            Placement = mask.Placement with
            {
                Anchor = new(0.25, 0.75),
                Offset = new(mask.Placement.Offset.X + document.Width * 0.25, mask.Placement.Offset.Y - document.Height * 0.25)
            }
        };
        context.Session.Editor.UpdateLayer(layer.Id, current => current with
        {
            Mask = mask,
            Tracks = [new(AnimationProperty.MASK_POSITION,
                [new(new(0), new ScenePoint(13, -7)), new(new(2), new ScenePoint(90, 45))])]
        });
        context.ViewModel.Effects.MaskExpanded = true;
        context.Window.Layouts.Activate("masks");
        context.Window.Layouts.Float("masks");
        context.Window.Layouts.Activate("effects");
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        floating.Width = 650;
        floating.Height = 1000;
        Dispatcher.UIThread.RunJobs();
        floating.UpdateLayout();
        context.Window.UpdateLayout();
    }

    private static void Click(MainWindowTestContext context, string origin, string name,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var editor = UiTestActions.Find<LayerPositionEditor>(context.Window.Panels[origin], "MaskPositionEditor");
        var button = UiTestActions.Find<Button>(editor, "AnchorPreset" + name);
        Assert.Contains(Localization.Get("Workbench.AnchorPresetHint"), Assert.IsType<string>(ToolTip.GetTip(button)));
        button.BringIntoView();
        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(button));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static ProjectionMatrix3x3 Matrix(MainWindowTestContext context)
    {
        var document = context.Session.DocumentSnapshot;
        var mask = SceneEvaluator.EvaluateMask(context.Session.SelectedLayer!, context.Session.AnimationTarget!.LocalTime)!;
        return ClipMaskPlacementGeometry.CreateMatrix(mask, new(document.Width, document.Height));
    }

    private static void AssertSamePoints(ProjectionMatrix3x3 before, ProjectionMatrix3x3 after)
    {
        foreach (var point in new ScenePoint[] { new(100, 50), new(300, 50), new(300, 150), new(100, 150) })
        {
            Assert.Equal(before.Map(point).X, after.Map(point).X, 8);
            Assert.Equal(before.Map(point).Y, after.Map(point).Y, 8);
        }
    }
}
