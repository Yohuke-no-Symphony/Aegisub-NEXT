using AegiNext.Core.Effects;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectScriptMaskEditingTests
{
    [Fact]
    public void MaskScriptKeepsNativePlacementAndFrameWhileAnimatingOnlyIndependentTransformPosition()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "native mask");
        var first = new MaskNode { Position = new(10, 20) };
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [first, new() { Position = new(100, 80) }] }],
            Placement = new() { Anchor = new(0.2, 0.8), Pivot = new(1.25, -0.25), Offset = new(7, -9) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(90, 60) },
            Transform = new() { Position = new(13, -7), Scale = new(2, -3), Rotation = 31 }
        };
        initial.SetClipMask(id, mask);
        initial.UpdateLayer(id, value => value with { Transform = new(900, 800, 5, 7, 60) });
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var script = EffectScriptParser.ParseLegacy("""
            effect "native-mask-position" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-position base
                at 1 mask-position offset(30, -20)
                at 0 mask-node(1,1).position base
                at 1 mask-node(1,1).position offset(5, 10)
            end
            """);

        editor.ApplyEffectScript(id, script);

        var layer = editor.Snapshot.Layers[0];
        Assert.Same(mask, layer.Mask);
        var position = layer.Tracks.Single(track => track.Property == AnimationProperty.MASK_POSITION);
        Assert.Equal(mask.Transform.Position, position.Keyframes[0].Value.Vector);
        Assert.Equal(new ScenePoint(43, -27), position.Keyframes[^1].Value.Vector);
        var evaluated = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(1)));
        Assert.Same(mask.Placement, evaluated.Placement);
        Assert.Same(mask.ReferenceFrame, evaluated.ReferenceFrame);
        Assert.Equal(new ScenePoint(28, -17), evaluated.Transform.Position);
        Assert.Equal(new ScenePoint(12.5, 25), evaluated.Contours[0].Nodes[0].Position);
        var canvas = new ScenePoint(before.Width, before.Height);
        var point = new ScenePoint(20, 30);
        var originalPoint = ClipMaskPlacementGeometry.CreateMatrix(mask, canvas).Map(point);
        var animatedPoint = ClipMaskPlacementGeometry.CreateMatrix(evaluated, canvas).Map(point);
        Assert.Equal(15, animatedPoint.X - originalPoint.X, 10);
        Assert.Equal(-10, animatedPoint.Y - originalPoint.Y, 10);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(mask.Placement, restored.Layers[0].Mask!.Placement);
        Assert.Equal(mask.ReferenceFrame, restored.Layers[0].Mask!.ReferenceFrame);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ScriptCannotReplaceAnOrderedMaskTrackAndLeavesUndoUntouched()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "ordered");
        initial.SetClipMask(id, new RectangleClipMask { BottomRight = new(100, 100) });
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT);
        initial.SetAnimationTransform(id, target, new ScenePoint(100, 100),
            new(Guid.NewGuid(), new(0), new(2), new ScenePoint(200, 100), 2));
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var script = EffectScriptParser.ParseLegacy("""
            effect "ordered-target" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-rectangle-bottom-right base
                at 1 mask-rectangle-bottom-right offset(50, 0)
            end
            """);

        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, script));

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ScriptReplacesOnlyCompleteTargetsAndMissingNodesLeaveHistoryUntouched()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "morph");
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(100, 30) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] };
        initial.SetClipMask(id, mask);
        var other = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id),
            [new(new(0), second.Position), new(new(2), new ScenePoint(100, 80))]);
        initial.UpdateLayer(id, layer => layer with { Tracks = [other] });
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var script = EffectScriptParser.ParseLegacy("""
            effect "one-node" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-node(1,1).position base power(2)
                at 1 mask-node(1,1).position offset(50, 0)
            end
            """);

        editor.ApplyEffectScript(id, script);

        Assert.Same(mask, editor.Snapshot.Layers[0].Mask);
        Assert.Same(other, editor.Snapshot.Layers[0].Tracks.Single(track => track.Target == other.Target));
        Assert.Equal(2, editor.Snapshot.Layers[0].Tracks.Length);
        var applied = editor.Snapshot;
        var invalid = EffectScriptParser.ParseLegacy("""
            effect "missing-node" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-node(1,3).position base
                at 1 mask-node(1,3).position offset(50, 0)
            end
            """);
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, invalid));
        Assert.Same(applied, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
