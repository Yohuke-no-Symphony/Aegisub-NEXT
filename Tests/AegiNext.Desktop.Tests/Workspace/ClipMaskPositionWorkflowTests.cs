using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ClipMaskPositionWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresetUsesMaskAnimationPosePreservesPlacementAndKeepsEveryTrackWithOneUndo(bool vector)
    {
        var document = Document(vector);
        var layer = document.Layers[0] with
        {
            Transform = new(100, 200, 9, 7, 120),
            Tracks =
            [
                new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(13, -7)), new(new(2), new ScenePoint(80, 50))]),
                new(AnimationProperty.MASK_SCALE, [new(new(0), new ScenePoint(2, 0.75)), new(new(2), new ScenePoint(3, 1))]),
                new(AnimationProperty.MASK_ROTATION, [new(new(0), 37d), new(new(2), 90d)])
            ]
        };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.MASK_SCALE, new(0), new(0))));
        var draft = session.MaskEditing.PositionDraft;
        Assert.Equal(new ScenePoint(2, 0.75), draft.Geometry!.Transform.Scale);
        Assert.Equal(37, draft.Geometry.Transform.Rotation);
        var original = SceneEvaluator.EvaluateMask(layer, new(0))!;
        var before = ClipMaskPlacementGeometry.CreateMatrix(original, new(document.Width, document.Height));

        Assert.True(draft.SelectPreset(new(1, 0), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot.Layers[0];
        Assert.Equal(new ScenePoint(1, 0), changed.Mask!.Placement.Anchor);
        Assert.Equal(new ScenePoint(1, 0), changed.Mask.Placement.Pivot);
        Assert.Equal(layer.Mask!.ReferenceFrame, changed.Mask.ReferenceFrame);
        Assert.Same(layer.Mask.Transform, changed.Mask.Transform);
        Assert.Equal(layer.Tracks, changed.Tracks);
        var after = ClipMaskPlacementGeometry.CreateMatrix(SceneEvaluator.EvaluateMask(changed, new(0))!, new(document.Width, document.Height));
        AssertSameProjection(before, after);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(layer.Mask.Placement.Pivot.X, (double)draft.PivotX.Parse()!);
    }

    [Fact]
    public async Task OutsidePivotPreviewsPreservesCurrentFrameAndInvalidTextKeepsItsOrigin()
    {
        var document = Document(false);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var source = session.DocumentSnapshot;
        var mask = source.Layers[0].Mask!;
        var before = ClipMaskPlacementGeometry.CreateMatrix(mask, new(source.Width, source.Height));
        var draft = session.MaskEditing.PositionDraft;
        session.MaskEditing.BeginPositionEdit("effects");
        draft.PivotX.RawText = "-2";
        draft.PivotY.RawText = "3";
        var preview = session.PreviewDocument.Layers[0].Mask!;
        Assert.Equal(new ScenePoint(-2, 3), preview.Placement.Pivot);
        AssertSameProjection(before, ClipMaskPlacementGeometry.CreateMatrix(preview, new(source.Width, source.Height)));
        Assert.Same(source, session.DocumentSnapshot);
        draft.AnchorX.RawText = "unfinished";
        session.MaskEditing.BeginPositionEdit("masks");

        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal("effects", session.ViewModel.InvalidPanelId);
        Assert.Equal(ClipMaskPositionEditingCoordinator.FieldKey("AnchorXInput"), session.ViewModel.InvalidFieldKey);
        Assert.Equal("unfinished", draft.AnchorX.RawText);
        Assert.Same(source, session.DocumentSnapshot);
        Assert.True(session.MaskEditing.RestorePositionField(ClipMaskPositionEditingCoordinator.FieldKey("AnchorXInput")));
        Assert.Equal("-2", draft.PivotX.RawText);
        Assert.Equal("3", draft.PivotY.RawText);
        Assert.True(session.TryCommitDrafts(false));
        Assert.True(context.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task OverflowingPivotCompensationShowsInvalidOffsetAndDoesNotCommitOrCreateUndo()
    {
        var document = Document(false);
        var mask = new RectangleClipMask
        {
            TopLeft = new(), BottomRight = new(100, 100),
            ReferenceFrame = new() { Size = new(100, 100) },
            Placement = new() { Pivot = new(0.5, 0.5) }
        };
        document = document with { Layers = [document.Layers[0] with { Mask = mask }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);

        session.MaskEditing.PositionDraft.PivotX.RawText = "20000000";

        Assert.Equal(1999999950m, session.MaskEditing.PositionDraft.OffsetX.Parse());
        Assert.Equal("OffsetXInput", session.MaskEditing.PositionDraft.Validate());
        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal(ClipMaskPositionEditingCoordinator.FieldKey("OffsetXInput"), session.ViewModel.InvalidFieldKey);
        Assert.Equal(mask.Placement, session.PreviewDocument.Layers[0].Mask!.Placement);
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(session.MaskEditing.RestorePositionField(ClipMaskPositionEditingCoordinator.FieldKey("PivotXInput")));
        Assert.Null(session.MaskEditing.PositionDraft.Validate());
        Assert.True(session.TryCommitDrafts(false));
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task NodeSelectionAndGeometryEditsKeepTheFixedFrameAndSharedPositionDraft()
    {
        var document = Document(true);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var layer = document.Layers[0];
        var original = Assert.IsType<VectorClipMask>(layer.Mask);
        var draft = session.MaskEditing.PositionDraft;
        var node = original.Contours[0].Nodes[1];
        session.MaskEditing.SelectNode(node.Id);
        Assert.Same(draft, session.MaskEditing.PositionDraft);
        Assert.True(session.MaskEditing.BeginGesture());
        var changed = original with
        {
            Contours = [original.Contours[0] with { Nodes = original.Contours[0].Nodes.SetItem(1, node with { Position = new(450, 250) }) }]
        };

        session.MaskEditing.CommitGesture(new(layer.Id, changed, node.Id));

        Assert.Equal(original.ReferenceFrame, context.Editor.Snapshot.Layers[0].Mask!.ReferenceFrame);
        Assert.Equal(original.Placement, context.Editor.Snapshot.Layers[0].Mask!.Placement);
        Assert.Same(draft, session.MaskEditing.PositionDraft);
        Assert.Equal(original.ReferenceFrame.Origin, draft.Geometry!.GlyphOrigin);
        Assert.Equal(original.ReferenceFrame.Size, draft.Geometry.GlyphSize);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document(bool vector)
    {
        var subtitle = new SubtitleLine { Text = "Mask", End = new(5) };
        ClipMask mask = vector ? new VectorClipMask
        {
            Contours = [new() { Nodes = [new() { Position = new(100, 50) }, new() { Position = new(300, 50) }, new() { Position = new(200, 150) }] }]
        } : new RectangleClipMask { TopLeft = new(100, 50), BottomRight = new(300, 150) };
        mask = ClipMaskPlacementGeometry.CreatePlacement(mask, new(800, 600)) with
        {
            Transform = new() { Position = new(13, -7), Scale = new(1.5, 0.75), Rotation = 37 }
        };
        return new()
        {
            Width = 800, Height = 600, Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End, Mask = mask }]
        };
    }

    private static void AssertSameProjection(ProjectionMatrix3x3 before, ProjectionMatrix3x3 after)
    {
        foreach (var point in new ScenePoint[] { new(100, 50), new(300, 50), new(200, 150), new(170, 90) })
        {
            Assert.Equal(before.Map(point).X, after.Map(point).X, 8);
            Assert.Equal(before.Map(point).Y, after.Map(point).Y, 8);
        }
    }
}
