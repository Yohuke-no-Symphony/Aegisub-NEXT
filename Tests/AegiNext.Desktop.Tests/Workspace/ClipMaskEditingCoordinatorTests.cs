using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ClipMaskEditingCoordinatorTests
{
    [Fact]
    public async Task ValidMaskNumericDraftPreviewsBeforeOneUndoableCommitAndInvalidTextSurvives()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument());
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(context.Editor.Snapshot.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var fields = session.MaskEditing.Fields;
        var x = Assert.Single(fields, field => field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT && field.Component == 0);
        var y = Assert.Single(fields, field => field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT && field.Component == 1);
        x.Draft.RawText = "25";
        var preview = Assert.IsType<RectangleClipMask>(session.PreviewDocument.Layers[0].Mask);
        Assert.Equal(25, preview.TopLeft.X);
        Assert.Same(original, context.Editor.Snapshot);
        y.Draft.RawText = "unfinished";
        Assert.False(session.TryCommitDrafts());
        Assert.Equal("unfinished", y.Draft.RawText);
        Assert.Same(original, context.Editor.Snapshot);
        session.MaskEditing.Restore(y);
        Assert.Equal("25", x.Draft.RawText);
        Assert.True(session.TryCommitDrafts());
        Assert.Equal(25, Assert.IsType<RectangleClipMask>(context.Editor.Snapshot.Layers[0].Mask).TopLeft.X);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task FrozenNodeGestureWritesOnlyItsNodeTrackAndClearNodeAnimationRetainsWholeTransform()
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(40, 50) };
        var document = CreateDocument();
        var layer = document.Layers[0] with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), [new(new(0), first.Position)]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id), [new(new(0), second.Position)]),
                new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(3, 4))])
            ]
        };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.IsTopologyLocked);
        Assert.True(session.MaskEditing.BeginGesture());
        var original = Assert.IsType<VectorClipMask>(AegiNext.Core.Editing.SceneEvaluator.EvaluateMask(layer, new(0)));
        var contour = original.Contours[0];
        var changed = original with { Contours = [contour with { Nodes = contour.Nodes.SetItem(0, first with { Position = new(70, 80) }) }] };
        session.MaskEditing.CommitGesture(new(layer.Id, changed, first.Id));
        Assert.Equal(new ScenePoint(70, 80), context.Editor.Snapshot.Layers[0].Tracks[0].Keyframes[0].Value.Vector);
        Assert.Same(layer.Tracks[1], context.Editor.Snapshot.Layers[0].Tracks[1]);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        session.MaskEditing.ClearNodeAnimation();
        Assert.False(session.MaskEditing.IsTopologyLocked);
        Assert.Equal(AnimationProperty.MASK_POSITION, Assert.Single(context.Editor.Snapshot.Layers[0].Tracks).Property);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task SelectionReplacementCancelsMaskGestureAndClearMaskClearsEveryMaskTrackInOneUndo()
    {
        var document = CreateDocument();
        var layer = document.Layers[0] with { Tracks = [new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(3, 4))])] };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        session.MaskEditing.CancelGesture();
        session.MaskEditing.CommitGesture(new(layer.Id, new RectangleClipMask { TopLeft = new(30, 40), BottomRight = new(100, 200) }));
        Assert.Same(document, context.Editor.Snapshot);
        session.MaskEditing.Clear();
        Assert.Null(context.Editor.Snapshot.Layers[0].Mask);
        Assert.Empty(context.Editor.Snapshot.Layers[0].Tracks);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SingleNodeClosedCubicSubdivisionPreservesCurveNodeIdentityAndFixedPivotWithOneUndo()
    {
        var node = new MaskNode { Position = new(50, 70), OutHandle = new(100, -80), InHandle = new(-90, -60) };
        var contour = new MaskContour { Nodes = [node] };
        var mask = new VectorClipMask { Contours = [contour], Placement = new() { Pivot = new(11, 13), Offset = new(11, 13) } };
        var document = CreateDocument();
        document = document with { Layers = [document.Layers[0] with { Mask = mask }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.MaskEditing.SelectNode(node.Id);
        session.MaskEditing.Subdivide();
        var changed = Assert.IsType<VectorClipMask>(context.Editor.Snapshot.Layers[0].Mask);
        Assert.Equal(mask.Placement, changed.Placement);
        Assert.Equal(mask.ReferenceFrame, changed.ReferenceFrame);
        var changedContour = Assert.Single(changed.Contours);
        Assert.Equal(contour.Id, changedContour.Id);
        Assert.Equal(2, changedContour.Nodes.Length);
        Assert.Equal(node.Id, changedContour.Nodes[0].Id);
        Assert.NotEqual(node.Id, changedContour.Nodes[1].Id);
        for (var sample = 0; sample <= 20; sample++)
        {
            var t = sample / 20d;
            var expected = EvaluateCubic(node, node, t);
            var actual = t <= 0.5
                ? EvaluateCubic(changedContour.Nodes[0], changedContour.Nodes[1], t * 2)
                : EvaluateCubic(changedContour.Nodes[1], changedContour.Nodes[0], t * 2 - 1);
            Assert.Equal(expected.X, actual.X, 8);
            Assert.Equal(expected.Y, actual.Y, 8);
        }
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ScenePoint EvaluateCubic(MaskNode first, MaskNode next, double t)
    {
        var u = 1 - t;
        return new(
            u * u * u * first.Position.X + 3 * u * u * t * (first.Position.X + first.OutHandle.X) +
            3 * u * t * t * (next.Position.X + next.InHandle.X) + t * t * t * next.Position.X,
            u * u * u * first.Position.Y + 3 * u * u * t * (first.Position.Y + first.OutHandle.Y) +
            3 * u * t * t * (next.Position.Y + next.InHandle.Y) + t * t * t * next.Position.Y);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public async Task FrozenSegmentInsertionPreservesTransformAnimationSelectsNewNodeAndCreatesOneUndo(double progress)
    {
        var contour = new MaskContour { Nodes = [new() { Position = new(20, 30), OutHandle = new(40, -30) }, new() { Position = new(200, 100), InHandle = new(-20, 50) }] };
        var mask = new VectorClipMask { Contours = [contour], Placement = new() { Pivot = new(11, 13), Offset = new(11, 13) } };
        var document = CreateDocument();
        var layer = document.Layers[0] with { Mask = mask, Tracks = [new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(90, 100))])] };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        session.MaskEditing.CommitSegmentInsertion(new(layer.Id, contour.Id, contour.Nodes[0].Id, progress));
        var changed = Assert.IsType<VectorClipMask>(context.Editor.Snapshot.Layers[0].Mask);
        Assert.Equal(3, changed.Contours[0].Nodes.Length);
        Assert.Equal(mask.Transform, changed.Transform);
        var expected = EvaluateCubic(contour.Nodes[0], contour.Nodes[1], progress);
        Assert.Equal(expected.X, changed.Contours[0].Nodes[1].Position.X, 8);
        Assert.Equal(expected.Y, changed.Contours[0].Nodes[1].Position.Y, 8);
        Assert.Equal(changed.Contours[0].Nodes[1].Id, session.SceneEditing.MaskNodeId);
        Assert.Same(layer.Tracks[0], Assert.Single(context.Editor.Snapshot.Layers[0].Tracks));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("snapshot")]
    [InlineData("identity")]
    [InlineData("topology")]
    [InlineData("progress")]
    public async Task InvalidatedSegmentInsertionDoesNotChangeTheProject(string invalidation)
    {
        var contour = new MaskContour { Nodes = [new() { Position = new(20, 30) }, new() { Position = new(100, 200) }] };
        var document = CreateDocument();
        var layer = document.Layers[0] with { Mask = new VectorClipMask { Contours = [contour] } };
        if (invalidation == "topology")
        {
            layer = layer with { Tracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, contour.Nodes[0].Id), [new(new(0), contour.Nodes[0].Position)])] };
        }
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        if (invalidation == "cancel")
        {
            session.MaskEditing.CancelGesture();
        }
        else if (invalidation == "snapshot")
        {
            context.Editor.UpdateSubtitle(document.Subtitles[0].Id, line => line with { Text = "changed" });
        }
        var current = context.Editor.Snapshot;
        session.MaskEditing.CommitSegmentInsertion(new(layer.Id, contour.Id, invalidation == "identity" ? Guid.NewGuid() : contour.Nodes[0].Id,
            invalidation == "progress" ? double.NaN : 0.5));
        Assert.Same(current, context.Editor.Snapshot);
    }

    [Fact]
    public async Task SubdivideActionDisablesAtTheTotalNodeBudgetAndResynchronizesAfterRemovalAndUndo()
    {
        var contour = new MaskContour { Nodes = System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Range(0, 10000).Select(_ => new MaskNode { Position = new(20, 30) })) };
        var document = CreateDocument();
        var layer = document.Layers[0] with { Mask = new VectorClipMask { Contours = [contour] } };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.MaskEditing.SelectNode(contour.Nodes[0].Id);
        Assert.False(session.MaskEditing.CanSubdivideSelectedNode);
        Assert.False(session.ViewModel.Masks.CanSubdivide);
        session.MaskEditing.Subdivide();
        Assert.Same(document, context.Editor.Snapshot);
        context.Editor.RemoveClipMaskNode(layer.Id, contour.Nodes[^1].Id);
        Assert.True(session.MaskEditing.CanSubdivideSelectedNode);
        Assert.True(session.ViewModel.Masks.CanSubdivide);
        Assert.True(context.Editor.Undo());
        Assert.False(session.MaskEditing.CanSubdivideSelectedNode);
        Assert.False(session.ViewModel.Masks.CanSubdivide);
    }

    [Fact]
    public async Task RectangleToolOnlyEntersEditingAndExitsWithoutWritingGeometry()
    {
        var document = CreateDocument();
        document = document with { Width = 1280, Height = 720, Layers = [document.Layers[0] with { Mask = null }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.MaskEditing.CreateRectangle();
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Null(context.Editor.Snapshot.Layers[0].Mask);
        Assert.Equal(CanvasEditMode.MASK_RECTANGLE, session.SceneEditing.Mode);
        session.MaskEditing.CreateRectangle();
        session.MaskEditing.ExitEditing();
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationCommandsRejectExistingMasksAndEditingTogglePreservesTheirType(bool vectorMask)
    {
        var document = CreateDocument();
        if (vectorMask)
        {
            document = document with
            {
                Layers =
                [
                    document.Layers[0] with
                    {
                        Mask = new VectorClipMask
                        {
                            Contours =
                            [
                                new()
                                {
                                    Nodes =
                                    [
                                        new()
                                        {
                                            Position = new(10, 20)
                                        },
                                        new()
                                        {
                                            Position = new(40, 50)
                                        }
                                    ]
                                }
                            ]
                        }
                    }
                ]
            };
        }
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var masks = session.ViewModel.Masks;
        Assert.False(masks.CanCreateMask);
        Assert.False(masks.CreateRectangleMaskCommand.CanExecute(null));
        Assert.False(masks.CreateVectorMaskCommand.CanExecute(null));
        Assert.True(masks.ToggleMaskEditingCommand.CanExecute(null));
        masks.CreateRectangleMaskCommand.Execute(null);
        masks.CreateVectorMaskCommand.Execute(null);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(document, context.Editor.Snapshot);
        masks.ToggleMaskEditingCommand.Execute(null);
        Assert.Equal(vectorMask ? CanvasEditMode.MASK_VECTOR : CanvasEditMode.MASK_RECTANGLE, session.SceneEditing.Mode);
        Assert.True(masks.IsMaskEditing);
        masks.ToggleMaskEditingCommand.Execute(null);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.False(masks.IsMaskEditing);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SelectedNodeAndContourRemovalRetainASurvivingSelectionAndOneUndoEach()
    {
        var first = new MaskNode { Position = new(20, 30) };
        var second = new MaskNode { Position = new(80, 90) };
        var third = new MaskNode { Position = new(110, 150) };
        var contour = new MaskContour { Nodes = [first, second] };
        var other = new MaskContour { Nodes = [third] };
        var document = CreateDocument();
        document = document with { Layers = [document.Layers[0] with { Mask = new VectorClipMask { Contours = [contour, other] } }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.MaskEditing.SelectNode(first.Id);
        Assert.True(session.MaskEditing.CanDeleteSelectedNode);
        session.MaskEditing.DeleteSelectedNode();
        Assert.Equal(second.Id, session.SceneEditing.MaskNodeId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        session.MaskEditing.SelectNode(first.Id);
        Assert.True(session.MaskEditing.CanDeleteSelectedContour);
        session.MaskEditing.DeleteSelectedContour();
        Assert.Equal(third.Id, session.SceneEditing.MaskNodeId);
        Assert.Equal(other.Id, session.SceneEditing.MaskContourId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DrawingAReplacementRectangleCommitsTheDisplayedPivotWithItsGeometry()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        var mask = new RectangleClipMask
        {
            TopLeft = new(600, 400),
            BottomRight = new(900, 700),
            Placement = new() { Pivot = new(0.5, 0.5), Anchor = new(0.5, 0.5), Offset = new(750 - document.Width / 2d, 550 - document.Height / 2d) },
            ReferenceFrame = new() { Origin = new(600, 400), Size = new(300, 300) }
        };
        session.MaskEditing.CommitGesture(new(document.Layers[0].Id, mask));
        Assert.Equal(mask, context.Editor.Snapshot.Layers[0].Mask);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task FrozenDeletionRequestRejectsAReplacedSnapshotAndLockedTopology()
    {
        var node = new MaskNode { Position = new(20, 30) };
        var document = CreateDocument();
        var layer = document.Layers[0] with { Mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] } };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        context.Editor.Apply("Change opacity", value => value with { Layers = [value.Layers[0] with { Opacity = 0.5 }] });
        var replacement = context.Editor.Snapshot;
        session.MaskEditing.CommitNodeDeletion(new(layer.Id, node.Id));
        Assert.Same(replacement, context.Editor.Snapshot);
        context.Editor.SetKeyframe(layer.Id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), new(new(0), node.Position));
        session.MaskEditing.SelectNode(node.Id);
        var locked = context.Editor.Snapshot;
        Assert.False(session.MaskEditing.CanDeleteSelectedNode);
        Assert.False(session.MaskEditing.CanDeleteSelectedContour);
        session.MaskEditing.DeleteSelectedNode();
        session.MaskEditing.DeleteSelectedContour();
        Assert.Same(locked, context.Editor.Snapshot);
    }

    private static ProjectDocument CreateDocument()
    {
        var line = new SubtitleLine { Text = "Mask", End = new(5) };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(500, 300) } }]
        };
    }
}
