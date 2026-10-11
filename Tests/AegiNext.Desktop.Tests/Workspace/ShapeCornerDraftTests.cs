using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ShapeCornerDraftTests
{
    [Fact]
    public async Task LegacyRoundedRectangleKeepsItsHiddenCornersAcrossOtherShapeKindsAndUndo()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ROUNDED_RECTANGLE, 100, 80) { CornerRadius = 12 }
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;
        model.ShapeType = 1;
        Assert.True(context.Session.TryCommitDrafts(false));
        var ellipse = context.Editor.Snapshot;
        Assert.Equal(ShapeKind.ELLIPSE, context.Session.SelectedLayer!.Shape!.Kind);
        Assert.Equal(ShapeCornerRadii.Uniform(12), context.Session.SelectedLayer.Shape.CornerRadii);
        model.ShapeType = 0;
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(ShapeKind.RECTANGLE, context.Session.SelectedLayer.Shape.Kind);
        Assert.Equal(ShapeCornerRadii.Uniform(12), context.Session.SelectedLayer.Shape.EffectiveCornerRadii);
        Assert.True(context.Editor.Undo());
        Assert.Same(ellipse, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.Null(context.Session.SelectedLayer!.Shape!.CornerRadii);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task LinkedTextEscapeRevertsPropagationButKeepsAnEarlierIndependentCornerDraft()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80) { CornerRadii = ShapeCornerRadii.Uniform(10) }
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;
        model.CornersLinked = false;
        model.CornerBottomRight.RawText = "13.00";
        model.CornersLinked = true;
        model.CornerTopLeft.RawText = "20";
        model.CornerTopLeft.RawText = "20e-";
        Assert.Equal("20", model.CornerBottomRight.RawText);
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.True(model.RestoreField("ShapeCornerTopLeftInput"));
        Assert.Equal("10", model.CornerTopLeft.RawText);
        Assert.Equal("10", model.CornerTopRight.RawText);
        Assert.Equal("13.00", model.CornerBottomRight.RawText);
        Assert.Equal("10", model.CornerBottomLeft.RawText);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new ShapeCornerRadii(10, 10, 13, 10), context.Session.SelectedLayer!.Shape!.CornerRadii);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRectangleLoadingLanguageAndUnrelatedEditsPreserveItsCornerRepresentation(bool independent)
    {
        var geometry = new LayerShape(ShapeKind.ROUNDED_RECTANGLE, 100, 80)
        {
            CornerRadius = 12,
            CornerRadii = independent ? new(1, 2, 3, 4) : null
        };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = geometry };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;

        Assert.Equal(4, model.ShapeChoices.Length);
        Assert.Equal(0, model.ShapeType);
        Assert.True(model.IsRectangle);
        Assert.Equal(!independent, model.CornersLinked);
        Assert.Equal(independent ? "1" : "12", model.CornerTopLeft.RawText);
        Assert.Equal(independent ? "4" : "12", model.CornerBottomLeft.RawText);
        model.RefreshLanguage();
        context.Session.SelectLayer(Guid.Empty, []);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        Assert.False(context.Session.HasProjectDrafts);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        model.StrokeWidth.RawText = "2";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(geometry, context.Session.SelectedLayer!.Shape);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CornersLinkByDefaultAndIndependentChangesUndoWithoutCollapsingOtherCorners()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80) };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;
        Assert.True(model.CornersLinked);
        model.CornerTopRight.RawText = "12";
        Assert.Equal("12", model.CornerTopLeft.RawText);
        Assert.Equal("12", model.CornerBottomRight.RawText);
        Assert.Equal("12", model.CornerBottomLeft.RawText);
        Assert.True(context.Session.TryCommitDrafts(false));
        var linked = context.Editor.Snapshot;
        Assert.Equal(ShapeCornerRadii.Uniform(12), context.Session.SelectedLayer!.Shape!.CornerRadii);

        model.CornersLinked = false;
        Assert.False(context.Session.HasProjectDrafts);
        model.CornerBottomLeft.RawText = "4";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new ShapeCornerRadii(12, 12, 12, 4), context.Session.SelectedLayer.Shape.CornerRadii);
        Assert.False(model.CornersLinked);
        Assert.True(context.Editor.Undo());
        Assert.Same(linked, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task LinkToggleAndInvalidInputDoNotOverwriteCornersAndIndependentEscapeRestoresOnlyItsField()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80) { CornerRadii = new(1, 2, 3, 4) }
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;
        Assert.False(model.CornersLinked);
        model.CornersLinked = true;
        Assert.False(context.Session.HasProjectDrafts);
        Assert.Equal("2", model.CornerTopRight.RawText);
        model.CornersLinked = false;
        model.CornerTopLeft.RawText = "7e-";
        model.CornerBottomRight.RawText = "9";
        model.RefreshLanguage();
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("ShapeCornerTopLeftInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("7e-", model.CornerTopLeft.RawText);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(model.RestoreField("ShapeCornerTopLeftInput"));
        Assert.Equal("1", model.CornerTopLeft.RawText);
        Assert.Equal("9", model.CornerBottomRight.RawText);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new ShapeCornerRadii(1, 2, 9, 4), context.Session.SelectedLayer!.Shape!.CornerRadii);
    }

    [Fact]
    public async Task EditingCornersAtASelectedKeyDoesNotChangeAnimatedPaintOrCreateGeometryKeys()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), End = new(3),
            Tracks =
            [
                new(AnimationProperty.FILL, [new(new(1), new SceneColor(0.2, 0.3, 0.4))]),
                new(AnimationProperty.STROKE_WIDTH, [new(new(1), 6)]),
                new(AnimationProperty.OPACITY, [new(new(1), 0.5)])
            ]
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, AnimationProperty.OPACITY, new(1), new(1))));
        context.Session.ViewModel.Styles.Shape.CornerTopLeft.RawText = "15";

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Session.SelectedLayer!;
        Assert.Equal(ShapeCornerRadii.Uniform(15), changed.Shape!.CornerRadii);
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.Equal(layer.Fill, changed.Fill);
        Assert.Equal(layer.StrokeWidth, changed.StrokeWidth);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
