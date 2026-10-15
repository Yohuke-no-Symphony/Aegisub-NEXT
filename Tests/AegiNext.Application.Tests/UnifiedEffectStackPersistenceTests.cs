using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class UnifiedEffectStackPersistenceTests
{
    [Fact]
    public void MovingANativeRowChangesCompositionInOneUndoAndEverySavedSampleUsesTheSameOrder()
    {
        var native = new AnimationTrack(AnimationProperty.POSITION,
            [new(new(0), new ScenePoint(5, 7)), new(new(1), new ScenePoint(9, 11))]);
        var instance = new EffectInstance { Name = "Offset", End = new(1), Source = """
            effect "saved-stack" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 position offset(10, 0)
                    at 1 position offset(10, 0)
                end
            end
            """ };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            End = new(1), Tracks = [native], Effects = [instance] };
        var document = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(document);
        Assert.Equal(16, SceneEvaluator.EvaluateLayer(layer, null, new(1, 4)).Transform.Position.X);

        editor.MoveTimelineEffectRow(layer.Id, new(native.Target), new(InstanceId: instance.Id));

        var changed = editor.Snapshot;
        Assert.Equal(6, SceneEvaluator.EvaluateLayer(changed.Layers[0], null, new(1, 4)).Transform.Position.X);
        Assert.Same(native, changed.Layers[0].Tracks[0]);
        Assert.Same(instance, changed.Layers[0].Effects[0]);
        Assert.Same(document.TimelineViewState, changed.TimelineViewState);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(changed));
        Assert.Equal<TimelineEffectRowIdentity>(changed.Layers[0].EffectRowOrder, restored.Layers[0].EffectRowOrder);
        var live = new PreparedProjectScene(changed);
        var loaded = new PreparedProjectScene(restored);
        foreach (var time in new[] { MediaTime.Zero, new(1, 4), new(1, 2), new(3, 4) })
        {
            var expected = SceneEvaluator.EvaluateLayer(live, layer.Id, time);
            var actual = SceneEvaluator.EvaluateLayer(loaded, layer.Id, time);
            Assert.Equal(expected.Transform, actual.Transform);
            Assert.Equal(expected.Opacity, actual.Opacity);
        }
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(16, SceneEvaluator.EvaluateLayer(editor.Snapshot.Layers[0], null, new(1, 4)).Transform.Position.X);
        Assert.True(editor.Redo());
        Assert.Same(changed, editor.Snapshot);
        Assert.Equal(6, SceneEvaluator.EvaluateLayer(editor.Snapshot.Layers[0], null, new(1, 4)).Transform.Position.X);
    }
}
