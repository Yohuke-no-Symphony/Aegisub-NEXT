using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ClipMaskTopologyEditingTests
{
    [Fact]
    public void RemoveNodePreservesOtherIdentitiesHandlesPlacementAndTracksInOneFlatTransaction()
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40), InHandle = new(-3, 4), OutHandle = new(5, -6) };
        var third = new MaskNode { Position = new(50, 60), InHandle = new(-7, -8), OutHandle = new(9, 10) };
        var other = new MaskContour { Nodes = [new() { Position = new(70, 80) }] };
        var contour = new MaskContour { Nodes = [first, second, third] };
        var mask = new VectorClipMask
        {
            Inverted = true, Contours = [contour, other],
            Placement = new() { Pivot = new(0.4, 0.5), Offset = new(40, 50) },
            ReferenceFrame = new() { Size = new(100, 100) },
            Transform = new() { Position = new(4, 5), Scale = new(2, 3), Rotation = 12 }
        };
        var editor = Editor(mask);
        var original = editor.Snapshot;
        var layer = original.Layers[0];
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        editor.RemoveClipMaskNode(layer.Id, first.Id);

        var resultLayer = editor.Snapshot.Layers[0];
        var result = Assert.IsType<VectorClipMask>(resultLayer.Mask);
        Assert.Equal(contour.Id, result.Contours[0].Id);
        Assert.Equal<MaskNode>([second, third], result.Contours[0].Nodes);
        Assert.Same(second, result.Contours[0].Nodes[0]);
        Assert.Same(third, result.Contours[0].Nodes[1]);
        Assert.Same(other, result.Contours[1]);
        Assert.Same(mask.Transform, result.Transform);
        Assert.Same(mask.Placement, result.Placement);
        Assert.Same(mask.ReferenceFrame, result.ReferenceFrame);
        Assert.True(result.Inverted);
        Assert.Equal(layer.Tracks, resultLayer.Tracks);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Equal(2, Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask).Contours[0].Nodes.Length);
    }

    [Fact]
    public void RemovingTheLastNodeRemovesOnlyItsContourAndPreservesTheOtherContour()
    {
        var first = new MaskContour { Nodes = [new()] };
        var other = new MaskContour { Nodes = [new(), new()] };
        var mask = new VectorClipMask
        {
            Contours = [first, other], Placement = new() { Pivot = new(0.15, 0.25), Offset = new(15, 25) },
            ReferenceFrame = new() { Size = new(100, 100) }
        };
        var editor = Editor(mask);
        var original = editor.Snapshot;
        var layer = original.Layers[0];
        editor.RemoveClipMaskNode(layer.Id, first.Nodes[0].Id);

        var result = Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask);
        Assert.Same(other, Assert.Single(result.Contours));
        Assert.Same(mask.Transform, result.Transform);
        Assert.Same(mask.Placement, result.Placement);
        Assert.Same(mask.ReferenceFrame, result.ReferenceFrame);
        Assert.Equal(layer.Tracks, editor.Snapshot.Layers[0].Tracks);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingFinalNodeOrContourClearsMaskAndAllMaskTracksInOneTransaction(bool removeContour)
    {
        var contour = new MaskContour { Nodes = [new()] };
        var editor = Editor(new VectorClipMask { Contours = [contour] });
        var original = editor.Snapshot;
        var layer = original.Layers[0];
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        if (removeContour)
        {
            editor.RemoveClipMaskContour(layer.Id, contour.Id);
        }
        else
        {
            editor.RemoveClipMaskNode(layer.Id, contour.Nodes[0].Id);
        }

        Assert.Null(editor.Snapshot.Layers[0].Mask);
        Assert.Same(layer.Tracks[0], Assert.Single(editor.Snapshot.Layers[0].Tracks));
        Assert.Equal(AnimationProperty.OPACITY, editor.Snapshot.Layers[0].Tracks[0].Property);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RemoveSelectedContourPreservesRemainingContourAndUsesOneUndoPerDeletion()
    {
        var first = new MaskContour { Nodes = [new(), new(), new()] };
        var second = new MaskContour { Nodes = [new(), new()] };
        var mask = new VectorClipMask
        {
            Contours = [first, second], Placement = new() { Pivot = new(0.22, 0.33), Offset = new(22, 33) },
            ReferenceFrame = new() { Size = new(100, 100) }
        };
        var editor = Editor(mask);
        var original = editor.Snapshot;
        var id = original.Layers[0].Id;
        editor.RemoveClipMaskContour(id, first.Id);
        var once = editor.Snapshot;
        var remaining = Assert.IsType<VectorClipMask>(once.Layers[0].Mask);
        Assert.Same(second, Assert.Single(remaining.Contours));
        Assert.Same(mask.Transform, remaining.Transform);
        Assert.Same(mask.Placement, remaining.Placement);
        Assert.Same(mask.ReferenceFrame, remaining.ReferenceFrame);
        Assert.Equal(original.Layers[0].Tracks, once.Layers[0].Tracks);
        editor.RemoveClipMaskContour(id, second.Id);
        Assert.Null(editor.Snapshot.Layers[0].Mask);
        Assert.True(editor.Undo());
        Assert.Same(once, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(AnimationProperty.MASK_NODE_POSITION)]
    [InlineData(AnimationProperty.MASK_NODE_IN_HANDLE)]
    [InlineData(AnimationProperty.MASK_NODE_OUT_HANDLE)]
    public void AnyNodeMorphLocksAllDeletionIncludingFinalGeometryUntilMorphTracksAreCleared(AnimationProperty property)
    {
        var contour = new MaskContour { Nodes = [new()] };
        var setup = Editor(new VectorClipMask { Contours = [contour] });
        var id = setup.Snapshot.Layers[0].Id;
        setup.SetKeyframe(id, new AnimationTrackTarget(property, contour.Nodes[0].Id), new(new(0), new ScenePoint(5, 6)));
        var original = setup.Snapshot;
        var editor = new ProjectEditor(original);

        Assert.Throws<InvalidOperationException>(() => editor.RemoveClipMaskNode(id, contour.Nodes[0].Id));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveClipMaskContour(id, contour.Id));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        editor.ClearMaskNodeAnimation(id);
        var unlocked = editor.Snapshot;
        Assert.Equal(3, unlocked.Layers[0].Tracks.Length);
        editor.RemoveClipMaskNode(id, contour.Nodes[0].Id);
        Assert.Null(editor.Snapshot.Layers[0].Mask);
        Assert.Single(editor.Snapshot.Layers[0].Tracks);
        Assert.True(editor.Undo());
        Assert.Same(unlocked, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void MorphOnAnotherContourLocksDeletionAndClearingItKeepsOverallTransformAnimation()
    {
        var first = new MaskContour { Nodes = [new(), new()] };
        var second = new MaskContour { Nodes = [new()] };
        var setup = Editor(new VectorClipMask { Contours = [first, second] });
        var id = setup.Snapshot.Layers[0].Id;
        setup.SetKeyframe(id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Nodes[0].Id), new(new(0), new ScenePoint(1, 2)));
        var editor = new ProjectEditor(setup.Snapshot);
        Assert.Throws<InvalidOperationException>(() => editor.RemoveClipMaskNode(id, first.Nodes[0].Id));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveClipMaskContour(id, first.Id));
        editor.ClearMaskNodeAnimation(id);
        var tracks = editor.Snapshot.Layers[0].Tracks;
        editor.RemoveClipMaskContour(id, first.Id);
        Assert.Equal(tracks, editor.Snapshot.Layers[0].Tracks);
        Assert.Same(second, Assert.Single(Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask).Contours));
    }

    [Fact]
    public void InvalidIdentityShapeAndLayerRejectAtomicallyWithoutClearingRedo()
    {
        var contour = new MaskContour { Nodes = [new(), new()] };
        var editor = Editor(new VectorClipMask { Contours = [contour] });
        var original = editor.Snapshot;
        var id = original.Layers[0].Id;
        editor.RemoveClipMaskNode(id, contour.Nodes[0].Id);
        Assert.True(editor.Undo());
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveClipMaskNode(id, Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveClipMaskContour(id, Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveClipMaskNode(Guid.NewGuid(), contour.Nodes[0].Id));
        Assert.Same(original, editor.Snapshot);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);

        var rectangle = Editor(new RectangleClipMask { BottomRight = new(100, 100) });
        Assert.Throws<InvalidOperationException>(() => rectangle.RemoveClipMaskNode(rectangle.Snapshot.Layers[0].Id, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => rectangle.RemoveClipMaskContour(rectangle.Snapshot.Layers[0].Id, Guid.NewGuid()));
        Assert.False(rectangle.CanUndo);
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10) };
        var nonSubtitle = new ProjectEditor(new() { Layers = [shape] });
        Assert.Throws<InvalidDataException>(() => nonSubtitle.RemoveClipMaskNode(shape.Id, Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => nonSubtitle.RemoveClipMaskContour(shape.Id, Guid.NewGuid()));
        Assert.False(nonSubtitle.CanUndo);
    }

    private static ProjectEditor Editor(ClipMask mask)
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(2), "mask");
        setup.SetClipMask(id, mask);
        setup.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.75));
        setup.SetKeyframe(id, AnimationProperty.MASK_POSITION, new(new(0), new ScenePoint(4, 5)));
        setup.SetAnimationTransform(id, new(AnimationProperty.MASK_ROTATION), 0,
            new(Guid.NewGuid(), new(0), new(2), 90, 2));
        var document = setup.Snapshot;
        return new(document);
    }
}
