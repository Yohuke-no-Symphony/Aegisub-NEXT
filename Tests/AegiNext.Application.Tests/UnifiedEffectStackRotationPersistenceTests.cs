using System.Security.Cryptography;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class UnifiedEffectStackRotationPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarRotationAfterAScriptPreservesTheOtherAxesAcrossSerialization(bool rangeScoped)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            RotationX = 19, RotationY = 23, Rotation = 29
        };
        var line = rangeScoped ? new SubtitleLine { Text = "AB", End = new(4), AnimationRanges = [range] } : null;
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION, TextRangeId: line is null ? null : range.Id);
        var native = new AnimationTrack(target, [new(new(0), 30d)]);
        var instance = Rotation("rotation-x", "offset(7)") with { TargetContext = target };
        var layer = Shape() with
        {
            Kind = line is null ? LayerKind.SHAPE : LayerKind.SUBTITLE,
            Shape = line is null ? new(ShapeKind.RECTANGLE, 20, 10) : null,
            SubtitleId = line?.Id,
            Tracks = [native], Effects = [instance],
            EffectRowOrder = [new(target), new(InstanceId: instance.Id)]
        };
        var document = new ProjectDocument { Layers = [layer], Subtitles = line is null ? [] : [line] };
        var expected = line is null ? new SceneVector3(8, 2, 30) : new SceneVector3(26, 23, 30);
        Assert.Equal(expected, RotationAt(document, layer.Id, rangeScoped ? range.Id : null));

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(expected, RotationAt(loaded, layer.Id, rangeScoped ? range.Id : null));
        Assert.Equal(target, Assert.Single(loaded.Layers[0].Tracks).Target);
        Assert.Equal(layer.EffectRowOrder.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
        Assert.Equal(AnimationProperty.ROTATION, Assert.Single(layer.Tracks).Property);
    }

    [Fact]
    public void EditorReorderPreservesScalarAxesOnBothSidesOfAFullRotationScriptAndOneUndo()
    {
        var x = new AnimationTrack(AnimationProperty.ROTATION_X, [new(new(0), 10d)]);
        var z = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 30d)]);
        var instance = Rotation("rotation", "vec3(40, 50, 60)");
        var layer = Shape() with { Tracks = [x, z], Effects = [instance] };
        var document = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(document);
        Assert.Equal(new SceneVector3(40, 50, 60), RotationAt(document, layer.Id));

        editor.MoveTimelineEffectRow(layer.Id, new(z.Target), new(InstanceId: instance.Id));
        var changed = editor.Snapshot;
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(changed));

        Assert.Equal(new SceneVector3(40, 50, 30), RotationAt(changed, layer.Id));
        Assert.Equal(new SceneVector3(40, 50, 30), RotationAt(loaded, layer.Id));
        Assert.Equal(changed.Layers[0].EffectRowOrder.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
        Assert.Equal(new[] { x.Target, z.Target }, loaded.Layers[0].Tracks.Select(track => track.Target).ToArray());
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(new SceneVector3(40, 50, 60), RotationAt(editor.Snapshot, layer.Id));
    }

    [Fact]
    public void UnorderedLayersAndPresetsStillNormalizeInAProjectWithAnOrderedScalarLayer()
    {
        var instance = Rotation("rotation-x", "offset(7)");
        var native = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 30d)]);
        var ordered = Shape() with
        {
            Tracks = [native], Effects = [instance],
            EffectRowOrder = [new(native.Target), new(InstanceId: instance.Id)]
        };
        var unordered = Shape() with { Start = new(4), End = new(8), Tracks = [native] };
        var document = new ProjectDocument
        {
            Layers = [ordered, unordered], Presets = [new(Guid.NewGuid(), "Legacy turn", [native])],
            TimelineViewState = new()
            {
                ExpandedAnimationClips = [new(ordered.Id, native.Target), new(unordered.Id, native.Target)]
            }
        };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(AnimationProperty.ROTATION, Assert.Single(loaded.Layers[0].Tracks).Property);
        Assert.Equal(ordered.EffectRowOrder.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(loaded.Layers[1].Tracks).Property);
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(loaded.Presets[0].Tracks).Property);
        Assert.Equal(new TimelineEffectClipId[]
        {
            new(ordered.Id, native.Target), new(unordered.Id, new(AnimationProperty.ROTATION_3D))
        }, loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
        Assert.Equal(RotationAt(document, unordered.Id), RotationAt(loaded, unordered.Id));
    }

    [Fact]
    public void RetainedExpandedScalarTargetsMatchTheExactLayerAndTextRange()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [first, second] };
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION, TextRangeId: first.Id);
        var otherTarget = new AnimationTrackTarget(AnimationProperty.ROTATION_X, TextRangeId: second.Id);
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Tracks = [new(target, [new(new(0), 30d)]), new(otherTarget, [new(new(0), 10d)])],
            EffectRowOrder = [new(target), new(otherTarget)]
        };
        var secondZ = target with { TextRangeId = second.Id };
        var whole = new AnimationTrackTarget(AnimationProperty.ROTATION);
        var missingLayer = Guid.NewGuid();
        var document = new ProjectDocument
        {
            Subtitles = [line], Layers = [layer], TimelineViewState = new()
            {
                ExpandedAnimationClips =
                [
                    new(layer.Id, target), new(layer.Id, otherTarget), new(layer.Id, secondZ),
                    new(layer.Id, whole), new(missingLayer, target)
                ]
            }
        };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(new TimelineEffectClipId[]
        {
            new(layer.Id, target), new(layer.Id, otherTarget),
            new(layer.Id, secondZ with { Property = AnimationProperty.ROTATION_3D }),
            new(layer.Id, whole with { Property = AnimationProperty.ROTATION_3D }),
            new(missingLayer, target with { Property = AnimationProperty.ROTATION_3D })
        }, loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
        Assert.Equal(layer.EffectRowOrder.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
    }

    [Fact]
    public void LegacyExpandedHintsWithoutAnyActualTrackStillNormalizeAndDeduplicate()
    {
        var layer = Shape();
        var document = new ProjectDocument
        {
            Layers = [layer], TimelineViewState = new()
            {
                ExpandedAnimationClips =
                [
                    new(layer.Id, new(AnimationProperty.ROTATION)),
                    new(layer.Id, new(AnimationProperty.ROTATION_X)),
                    new(layer.Id, new(AnimationProperty.ROTATION_Y))
                ]
            }
        };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Empty(loaded.Layers[0].Tracks);
        Assert.Equal(new TimelineEffectClipId(layer.Id, new(AnimationProperty.ROTATION_3D)),
            Assert.Single(loaded.TimelineViewState.ExpandedAnimationClips));
        Assert.Equal(3, document.TimelineViewState.ExpandedAnimationClips.Length);
    }

    [Fact]
    public void OrderedScalarSerializationAndFingerprintRemainStableAcrossRepeatedRoundTrips()
    {
        var native = new AnimationTrack(AnimationProperty.ROTATION_X,
            [new(new(1, 3), 10d), new(new(7, 3), 70d)]);
        var instance = Rotation("rotation", "vec3(40, 50, 60)");
        var layer = Shape() with
        {
            Tracks = [native], Effects = [instance],
            EffectRowOrder = [new(native.Target), new(InstanceId: instance.Id)]
        };
        var document = new ProjectDocument { Layers = [layer] };

        var first = ProjectStore.Serialize(document);
        var loaded = ProjectStore.Deserialize(first);
        var second = ProjectStore.Serialize(loaded);
        var reopened = ProjectStore.Deserialize(second);

        Assert.Equal(first, second);
        Assert.Equal(second, ProjectStore.Serialize(reopened));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(first)), ProjectStore.ComputeFingerprint(document));
        Assert.Equal(ProjectStore.ComputeFingerprint(document), ProjectStore.ComputeFingerprint(reopened));
        ProjectStore.ValidateSerialization(document);
        foreach (var time in new MediaTime[] { new(0), new(1, 3), new(4, 3), new(7, 3), new(3) })
        {
            Assert.Equal(RotationAt(document, layer.Id, time: time), RotationAt(reopened, layer.Id, time: time));
        }
    }

    private static SceneVector3 RotationAt(ProjectDocument document, Guid layerId, Guid? rangeId = null, MediaTime? time = null)
    {
        var evaluated = SceneEvaluator.EvaluateLayer(new PreparedProjectScene(document), layerId, time ?? new(1));
        if (rangeId is { } id)
        {
            var range = evaluated.AnimationRanges.Single(value => value.Id == id);
            return new(range.RotationX, range.RotationY, range.Rotation);
        }
        return new(evaluated.Transform.RotationX, evaluated.Transform.RotationY, evaluated.Transform.Rotation);
    }

    private static ProjectLayer Shape() => new()
    {
        Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10), End = new(4),
        Transform = new() { RotationX = 1, RotationY = 2, Rotation = 3 }
    };

    private static EffectInstance Rotation(string property, string value) => new()
    {
        Name = "Turn", End = new(4), Source =
            "effect \"turn\" version 3\nshort-clip compress\nscope whole current\nsegment all flex 1\n" +
            "at 0 " + property + " " + value + "\nat 1 " + property + " " + value + "\nend\nend"
    };
}
