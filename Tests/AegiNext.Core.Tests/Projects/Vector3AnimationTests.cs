using System.Text.Json;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class Vector3AnimationTests
{
    private static readonly string[] componentNames = ["X", "Y", "Z"];

    [Fact]
    public void CompleteXyzValueKeepsDimensionAndTurnsWithoutAngleWrapping()
    {
        var value = AnimationValue.FromVector3(new(10, -20, 720));
        Assert.Equal(AnimationValueKind.VECTOR3, value.Kind);
        Assert.True(value.IsVector3);
        Assert.False(value.IsVector);
        Assert.False(value.IsColor);
        Assert.Equal(3, value.ComponentCount);
        Assert.Equal(new SceneVector3(10, -20, 720), value.Vector3);
        Assert.Equal(720, value.GetComponent(2));
        Assert.Equal(new SceneVector3(10, 25, 720), value.WithComponent(1, 25).Vector3);
        Assert.Equal(new SceneVector3(60, 80, 360), AnimationValue.Lerp(value, new SceneVector3(110, 180, 0), 0.5).Vector3);
        Assert.Throws<InvalidOperationException>(() => value.AsVector());
        Assert.Throws<InvalidOperationException>(() => value.AsScalar());
        Assert.Throws<InvalidOperationException>(() => value.AsColor());
        Assert.Throws<InvalidOperationException>(() => AnimationValue.FromVector(new(1, 2)).AsVector3());
        Assert.Throws<ArgumentOutOfRangeException>(() => value.GetComponent(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.WithComponent(-1, 0));
    }

    [Fact]
    public void CanonicalPropertyIsTheOnlyVisibleRotationAndNamesAllThreeComponents()
    {
        Assert.Equal(2, (int)AnimationProperty.ROTATION);
        Assert.Equal(36, (int)AnimationProperty.ROTATION_X);
        Assert.Equal(37, (int)AnimationProperty.ROTATION_Y);
        Assert.Equal(39, (int)AnimationProperty.ROTATION_3D);
        Assert.Contains(AnimationProperty.ROTATION_3D, AnimationPropertyMetadata.CurrentProperties);
        Assert.DoesNotContain(AnimationProperty.ROTATION, AnimationPropertyMetadata.CurrentProperties);
        Assert.DoesNotContain(AnimationProperty.ROTATION_X, AnimationPropertyMetadata.CurrentProperties);
        Assert.DoesNotContain(AnimationProperty.ROTATION_Y, AnimationPropertyMetadata.CurrentProperties);
        Assert.Equal(AnimationValueKind.VECTOR3, AnimationPropertyMetadata.GetValueKind(AnimationProperty.ROTATION_3D));
        Assert.Equal(3, AnimationPropertyMetadata.GetComponentCount(AnimationProperty.ROTATION_3D));
        Assert.Equal(30000, AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.ROTATION_3D));
        Assert.Equal(componentNames, Enumerable.Range(0, 3).Select(component =>
            AnimationPropertyMetadata.GetComponentName(AnimationProperty.ROTATION_3D, component)));
        Assert.Equal(-1e9, AnimationPropertyMetadata.GetMinimum(AnimationProperty.ROTATION_3D, 2));
        Assert.Equal(1e9, AnimationPropertyMetadata.GetMaximum(AnimationProperty.ROTATION_3D, 2));
        Assert.True(AnimationPropertyMetadata.IsTextRangeProperty(AnimationProperty.ROTATION_3D));
        Assert.False(AnimationPropertyMetadata.IsSubtitleVisualProperty(AnimationProperty.ROTATION_3D));
    }

    [Fact]
    public void SharedKeyTimesKeepIndependentYAndZCurvesAndJsonValues()
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION_3D,
        [
            new(new(0), new SceneVector3(10, 20, 30))
            {
                ComponentCurves = [new(KeyframeInterpolation.HOLD), new(KeyframeInterpolation.EASE_OUT)]
            },
            new(new(8), new SceneVector3(110, 220, 330))
        ]);
        var encoded = JsonSerializer.Serialize(track);
        var restored = Assert.IsType<AnimationTrack>(JsonSerializer.Deserialize<AnimationTrack>(encoded));
        Assert.Equal(track.Keyframes.Select(frame => frame.Value), restored.Keyframes.Select(frame => frame.Value));
        Assert.Equal(track.Keyframes[0].GetCurve(1), restored.Keyframes[0].GetCurve(1));
        Assert.Equal(track.Keyframes[0].GetCurve(2), restored.Keyframes[0].GetCurve(2));
        var sample = SceneEvaluator.EvaluateVector3Track(track, new(2));
        Assert.Equal(new SceneVector3(35, 20, 161.25), sample);
        Assert.Equal(new SceneVector3(10, 20, 30), SceneEvaluator.EvaluateVector3Track(track, new(-1)));
        Assert.Equal(new SceneVector3(110, 220, 330), SceneEvaluator.EvaluateVector3Track(track, new(9)));
        Assert.Throws<InvalidOperationException>(() => SceneEvaluator.EvaluateVectorTrack(track, new(2)));
        ProjectValidator.Validate(new() { Layers = [Shape(track)] });
    }

    [Fact]
    public void RepeatedCroppingPreservesEveryIndependentReversedCurve()
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION_3D,
        [
            new(new(-2), new SceneVector3(10, 20, 30), KeyframeInterpolation.POWER)
            {
                CurveStart = 0.1, CurveEnd = 0.9, Exponent = 2.5, Reverse = true,
                ComponentCurves =
                [
                    new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8),
                    new(KeyframeInterpolation.POWER, 0.15, 0.75) { Exponent = 0.7, Reverse = true }
                ]
            },
            new(new(8), new SceneVector3(110, 220, 750))
        ]);
        var once = LayerAnimationTiming.Clip(Shape(track) with { Start = new(1), End = new(7), AnimationOffset = new(1) });
        var twice = LayerAnimationTiming.Clip(once with { Start = new(2), End = new(6), AnimationOffset = new(2) });
        var clipped = Assert.Single(twice.Tracks);
        ProjectValidator.Validate(new() { Layers = [twice] });
        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(40 + index, 20);
            Equal(SceneEvaluator.EvaluateVector3Track(track, time), SceneEvaluator.EvaluateVector3Track(clipped, time));
        }
        Assert.True(clipped.Keyframes[0].GetCurve(0).Reverse);
        Assert.False(clipped.Keyframes[0].GetCurve(1).Reverse);
        Assert.True(clipped.Keyframes[0].GetCurve(2).Reverse);
    }

    [Fact]
    public void CanonicalBaseEditingUsesExistingWholeAndRangeRotationFields()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { RotationX = 1, RotationY = 2, Rotation = 3 };
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Transform = new() { RotationX = 10, RotationY = 20, Rotation = 30 } };
        var whole = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);
        var local = whole with { TextRangeId = range.Id };
        var document = new ProjectDocument { Layers = [layer], Subtitles = [line] };
        Assert.Equal(new SceneVector3(10, 20, 30), SubtitleAnimationEvaluation.GetBaseValue(layer, line, whole).Vector3);
        Assert.Equal(new SceneVector3(1, 2, 3), SubtitleAnimationEvaluation.GetBaseValue(layer, line, local).Vector3);
        var changed = SubtitleAnimationEditing.SetBaseValue(document, layer.Id, local, new SceneVector3(4, 5, 6));
        Assert.Equal(layer.Transform, changed.Layers[0].Transform);
        Assert.Equal(range.Id, changed.Subtitles[0].AnimationRanges[0].Id);
        Assert.Equal(new SceneVector3(4, 5, 6), SubtitleAnimationEvaluation.GetBaseValue(changed.Layers[0], changed.Subtitles[0], local).Vector3);
        changed = SubtitleAnimationEditing.SetBaseValue(changed, layer.Id, whole, new SceneVector3(40, 50, 60));
        Assert.Equal(40, changed.Layers[0].Transform.RotationX);
        Assert.Equal(50, changed.Layers[0].Transform.RotationY);
        Assert.Equal(60, changed.Layers[0].Transform.Rotation);
        Assert.Equal(6, changed.Subtitles[0].AnimationRanges[0].Rotation);
        Assert.Same(changed, SubtitleAnimationEditing.SetBaseValue(changed, layer.Id, whole, new SceneVector3(40, 50, 60)));
    }

    [Fact]
    public void CanonicalRotationOverridesCompatibilityAxesBeforeAddingPathZOrientation()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            Tracks =
            [
                new(AnimationProperty.ROTATION_X, [new(new(0), 100)]),
                new(AnimationProperty.ROTATION, [new(new(0), 200)]),
                new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(10, 20, 30))]),
                new(new AnimationTrackTarget(AnimationProperty.ROTATION, TextRangeId: range.Id), [new(new(0), 300)]),
                new(new AnimationTrackTarget(AnimationProperty.ROTATION_3D, TextRangeId: range.Id), [new(new(0), new SceneVector3(1, 2, 3))])
            ],
            MotionPath = new(new(new(0, 0), [new(new(0, 10), new(0, 20), new(0, 30))]), new(8), true)
        };
        var evaluated = SceneEvaluator.EvaluateLayer(layer, line, new(4));
        Assert.Equal(10, evaluated.Transform.RotationX);
        Assert.Equal(20, evaluated.Transform.RotationY);
        Assert.Equal(120, evaluated.Transform.Rotation, 10);
        Assert.Equal(1, evaluated.AnimationRanges[0].RotationX);
        Assert.Equal(2, evaluated.AnimationRanges[0].RotationY);
        Assert.Equal(3, evaluated.AnimationRanges[0].Rotation);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer], Subtitles = [line] }));
    }

    [Fact]
    public void Vector3ValidationRejectsIncompleteDimensionsAndNonNormalRotationTargets()
    {
        var invalid = new AnimationTrack[]
        {
            new(AnimationProperty.ROTATION_3D, [new(new(0), 5)]),
            new(AnimationProperty.ROTATION_3D, [new(new(0), new ScenePoint(5, 6))]),
            new(AnimationProperty.ROTATION_3D, [new(new(0), SceneColor.White)]),
            new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(0, double.NaN, 0))]),
            new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(0, 0, 1e9 + 1))]),
            new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(1, 2, 3)) { ComponentCurves = [new(KeyframeInterpolation.HOLD)] }]),
            new(AnimationProperty.ROTATION_3D, [])
            {
                InitialValue = new SceneVector3(1, 2, 3),
                Transforms = [new(Guid.NewGuid(), new(0), new(1), new SceneVector3(2, 3, 4)) { ComponentMask = 8 }]
            },
            new(AnimationProperty.ROTATION_3D, [])
            {
                InitialValue = new SceneVector3(1, 2, 3),
                Transforms = [new(Guid.NewGuid(), new(0), new(1), new SceneVector3(2, 3, 4)) { Curve = new(KeyframeInterpolation.POWER) { Exponent = 0 } }]
            }
        };
        foreach (var track in invalid)
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [Shape(track)] }));
        }
        Assert.Throws<InvalidDataException>(() => SubtitleAnimationTargetValidation.ValidateIdentity(
            new(AnimationProperty.ROTATION_3D, State: SubtitleAnimationState.ACTIVE)));
        Assert.Throws<InvalidDataException>(() => SubtitleAnimationTargetValidation.ValidateIdentity(
            new(AnimationProperty.ROTATION_3D, NodeId: Guid.NewGuid())));
    }

    [Fact]
    public void LegacyRotationRowsRemainReadableBeforeProjectNormalization()
    {
        var owner = Guid.NewGuid();
        var state = new TimelineViewState
        {
            CollapsedAnimationRows =
            [
                new(TimelineRowScope.TRACK, owner, AnimationProperty.ROTATION),
                new(TimelineRowScope.TRACK, owner, AnimationProperty.ROTATION_X),
                new(TimelineRowScope.TRACK, owner, AnimationProperty.ROTATION_Y)
            ]
        };
        state.Validate();
        Assert.Throws<InvalidDataException>(() => (state with
        {
            CollapsedAnimationRows = [state.CollapsedAnimationRows[0] with { State = SubtitleAnimationState.ACTIVE }]
        }).Validate());
    }

    private static ProjectLayer Shape(AnimationTrack track) => new()
    {
        Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(8), Tracks = [track]
    };

    private static void Equal(SceneVector3 expected, SceneVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }
}
