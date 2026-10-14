using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipMaskAnimationEditingTests
{
    [Fact]
    public void EditingTwoNodeTargetsPreservesBothAndSupportsUndoAndPersistence()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "mask");
        var first = new MaskNode();
        var second = new MaskNode();
        editor.SetClipMask(id, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        var targetA = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var targetB = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id);
        editor.SetKeyframe(id, targetA, new(new(0), new ScenePoint(10, 20)));
        editor.SetKeyframe(id, targetB, new(new(0), new ScenePoint(30, 40)));
        var before = editor.Snapshot;
        editor.SetKeyframe(id, targetA, new(new(2), new ScenePoint(50, 60)));
        Assert.Equal(2, editor.Snapshot.Layers[0].Tracks.Length);
        Assert.Single(editor.Snapshot.Layers[0].Tracks.First(track => track.Target == targetB).Keyframes);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(editor.Snapshot.Layers[0].Tracks.Select(track => track.Target), restored.Layers[0].Tracks.Select(track => track.Target));
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void NodeMorphLocksTopologyUntilItsTracksAreClearedAndMaskTransformSurvives()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "mask");
        var node = new MaskNode();
        var contour = new MaskContour { Nodes = [node] };
        var mask = new VectorClipMask { Contours = [contour] };
        editor.SetClipMask(id, mask);
        editor.SetKeyframe(id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), new(new(0), new ScenePoint(10, 20)));
        editor.SetKeyframe(id, AnimationProperty.MASK_POSITION, new(new(0), new ScenePoint(1, 2)));
        var original = editor.Snapshot;
        var changed = mask with { Contours = [contour with { Nodes = [node, new()] }] };
        Assert.Throws<InvalidOperationException>(() => editor.SetClipMask(id, changed));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateLayer(id, layer => layer with { Mask = changed }));
        Assert.Same(original, editor.Snapshot);
        editor.ClearMaskNodeAnimation(id);
        Assert.Equal(AnimationProperty.MASK_POSITION, Assert.Single(editor.Snapshot.Layers[0].Tracks).Property);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.True(editor.Redo());
        editor.SetClipMask(id, changed);
        Assert.Equal(2, Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask).Contours[0].Nodes.Length);
    }

    [Fact]
    public void ClearMaskClearsAllMaskAnimationInOneTransactionAndPreservesOrdinaryTracks()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "mask");
        editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(100, 100) });
        editor.SetKeyframe(id, AnimationProperty.MASK_POSITION, new(new(0), new ScenePoint(1, 2)));
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.75));
        var original = editor.Snapshot;
        editor.ClearClipMask(id);
        Assert.Null(editor.Snapshot.Layers[0].Mask);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(editor.Snapshot.Layers[0].Tracks).Property);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void OrderedOperationIdentitySupportsUpdateReorderRemoveAndRejectsImplicitKeyframes()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "mask");
        editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(100, 100) });
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var first = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(4), new ScenePoint(10, 20), 1.75);
        var second = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), new ScenePoint(20, 30));
        editor.SetAnimationTransform(id, target, new ScenePoint(0, 0), first);
        editor.SetAnimationTransform(id, target, new ScenePoint(0, 0), second);
        var original = editor.Snapshot;
        Assert.Throws<InvalidOperationException>(() => editor.SetKeyframe(id, target, new(new(2), new ScenePoint(40, 50))));
        Assert.Same(original, editor.Snapshot);
        editor.SetAnimationTransform(id, target, new ScenePoint(0, 0), first with { Acceleration = 3 });
        editor.MoveAnimationTransform(id, target, second.Id, 0);
        Assert.Equal(second.Id, editor.Snapshot.Layers[0].Tracks[0].Transforms[0].Id);
        Assert.Equal(3, editor.Snapshot.Layers[0].Tracks[0].Transforms[1].Acceleration);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal<AnimationTransformOperation>(editor.Snapshot.Layers[0].Tracks[0].Transforms, restored.Layers[0].Tracks[0].Transforms);
        editor.RemoveAnimationTransform(id, target, first.Id);
        Assert.Equal(second.Id, Assert.Single(editor.Snapshot.Layers[0].Tracks[0].Transforms).Id);
        editor.RemoveAnimationTransform(id, target, second.Id);
        Assert.Empty(editor.Snapshot.Layers[0].Tracks);
    }

    [Fact]
    public void ExistingV5WithoutNewOptionalAnimationFieldsStillLoads()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "mask");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
        var json = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        var track = json["layers"]![0]!["tracks"]![0]!.AsObject();
        track.Remove("initialValue");
        track.Remove("transforms");
        track["keyframes"]![0]!.AsObject().Remove("exponent");
        var restored = ProjectStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Empty(restored.Layers[0].Tracks[0].Transforms);
        Assert.Null(restored.Layers[0].Tracks[0].InitialValue);
        Assert.Equal(1, restored.Layers[0].Tracks[0].Keyframes[0].Exponent);
    }

    [Fact]
    public void SplitCreatesIndependentOperationIdsAndStretchPreservesTheirContentClockAndNodeTargets()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "abcd");
        var first = new MaskNode();
        var second = new MaskNode();
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] };
        editor.SetClipMask(id, mask);
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(4), new ScenePoint(20, 40), 2);
        editor.SetAnimationTransform(id, firstTarget, new ScenePoint(0, 0), operation);
        editor.SetKeyframe(id, secondTarget, new(new(0), new ScenePoint(10, 20)));
        editor.SetKeyframe(id, secondTarget, new(new(4), new ScenePoint(30, 60)));
        var original = editor.Snapshot;
        editor.Apply("Split", document => ProjectEditingOperations.SplitSubtitle(document, id, new(2), 2));
        var right = editor.Snapshot.Layers[1];
        Assert.Equal(new MediaTime(2), right.AnimationOffset);
        var splitOperation = Assert.Single(right.Tracks[0].Transforms);
        Assert.NotEqual(operation.Id, splitOperation.Id);
        Assert.Equal(operation with { Id = splitOperation.Id }, splitOperation);
        Assert.Same(mask.Placement, right.Mask!.Placement);
        Assert.Same(mask.ReferenceFrame, right.Mask.ReferenceFrame);
        var rightMask = Assert.IsType<VectorClipMask>(AegiNext.Core.Editing.SceneEvaluator.EvaluateMask(right, new(3)));
        Assert.Equal(new ScenePoint(11.25, 22.5), rightMask.Contours[0].Nodes[0].Position);
        Assert.Equal(new ScenePoint(25, 50), rightMask.Contours[0].Nodes[1].Position);
        editor.SetLayerTiming(right.Id, right.Start, new(6), AegiNext.Core.Editing.TimelineEditMode.STRETCH);
        var stretched = editor.Snapshot.Layers[1];
        Assert.Equal(splitOperation.Id, stretched.Tracks[0].Transforms[0].Id);
        Assert.Equal(new MediaTime(8), stretched.Tracks[0].Transforms[0].End);
        Assert.Equal(new MediaTime(4), stretched.AnimationOffset);
        var stretchedMask = Assert.IsType<VectorClipMask>(AegiNext.Core.Editing.SceneEvaluator.EvaluateMask(stretched, new(6)));
        Assert.Equal<MaskNode>(rightMask.Contours[0].Nodes, stretchedMask.Contours[0].Nodes);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(firstTarget, restored.Layers[1].Tracks[0].Target);
        Assert.Equal(secondTarget, restored.Layers[1].Tracks[1].Target);
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }
}
