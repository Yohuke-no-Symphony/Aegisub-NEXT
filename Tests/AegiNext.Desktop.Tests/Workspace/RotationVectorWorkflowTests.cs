using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class RotationVectorWorkflowTests
{
    [Fact]
    public async Task EditingAllRotationAxesUpdatesOneSharedKeyframeAndPreservesItsCurves()
    {
        var value = AnimationValue.FromVector3(new(10.123456789012345, 20.123456789012345, 30.123456789012345));
        var frame = new Keyframe(new(1), value, KeyframeInterpolation.POWER)
        {
            Exponent = 2, CurveStart = 0.2, CurveEnd = 0.8, Reverse = true,
            ComponentCurves = [new(KeyframeInterpolation.EASE_IN), new(KeyframeInterpolation.POWER) { Exponent = 4 }]
        };
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var other = new AnimationTrack(AnimationProperty.OPACITY, [new(new(1), 0.5)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [other, new(AnimationProperty.ROTATION_3D, [frame])] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION_3D, frame.Time, frame.Time)));
        var row = Assert.Single(session.ViewModel.Effects.RotationRows);
        row.X.RawText = "11.123456789012345";
        row.Y.RawText = "21.123456789012345";
        session.ViewModel.Effects.RotationText = "31.123456789012345";

        Assert.True(session.TryCommitDrafts(false));

        var rotation = Assert.Single(session.SelectedLayer!.Tracks, track => track.Property == AnimationProperty.ROTATION_3D);
        Assert.Equal(frame with { Value = AnimationValue.FromVector3(new(11.123456789012345, 21.123456789012345, 31.123456789012345)) }, Assert.Single(rotation.Keyframes));
        Assert.Same(other, session.SelectedLayer.Tracks[0]);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(AnimationValueKind.VECTOR)]
    [InlineData(AnimationValueKind.VECTOR3)]
    [InlineData(AnimationValueKind.COLOR)]
    public async Task CurvePickerEditsOnlyTheChosenComponentAndRetainsSharedTimeAndTrackMetadata(AnimationValueKind kind)
    {
        var property = kind == AnimationValueKind.VECTOR3 ? AnimationProperty.ROTATION_3D :
            kind == AnimationValueKind.COLOR ? AnimationProperty.FILL : AnimationProperty.POSITION;
        var value = kind == AnimationValueKind.VECTOR3 ? AnimationValue.FromVector3(new(10, 20, 30)) :
            kind == AnimationValueKind.COLOR ? AnimationValue.FromColor(new(0.1f, 0.2f, 0.3f, 0.5f)) : AnimationValue.FromVector(new(10, 20));
        var frame = new Keyframe(new(1), value, KeyframeInterpolation.POWER) { Exponent = 2, CurveStart = 0.2, CurveEnd = 0.8, Reverse = true };
        var track = new AnimationTrack(property, [frame]) { ColorSpace = kind == AnimationValueKind.COLOR ? AnimationColorSpace.SRGB : AnimationColorSpace.LINEAR_RGB };
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [track] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, property, frame.Time, frame.Time)));
        var effects = session.ViewModel.Effects;
        var component = value.ComponentCount - 1;
        effects.SelectedKeyframeCurveComponent = effects.KeyframeCurveComponents.Single(choice => choice.Mask == 1 << component);
        Assert.False(session.HasProjectDrafts);
        effects.PowerExponent.RawText = "7";

        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(track.ColorSpace, changed.ColorSpace);
        var key = Assert.Single(changed.Keyframes);
        Assert.Equal(frame.Time, key.Time);
        Assert.Equal(frame.Value, key.Value);
        Assert.Equal(new(KeyframeInterpolation.POWER) { Exponent = 7 }, key.GetCurve(component));
        for (var index = 0; index < component; index++)
        {
            Assert.Equal(frame.GetCurve(index), key.GetCurve(index));
        }
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }

    [Fact]
    public async Task LegacyScalarTargetSelectionMigratesOnceAndTheRotationTextProxyKeepsXY()
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End, Transform = new() { RotationX = 12, RotationY = 23 },
            Tracks = [new(AnimationProperty.ROTATION, [new(new(1), 34d, KeyframeInterpolation.POWER) { Exponent = 2 }])]
        };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION, new(1), new(1))));
        Assert.Equal(AnimationProperty.ROTATION_3D, session.ViewModel.Effects.Target.Property);
        session.ViewModel.Effects.RotationText = "45";

        Assert.True(session.TryCommitDrafts(false));

        var track = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(AnimationProperty.ROTATION_3D, track.Property);
        Assert.Equal(new SceneVector3(12, 23, 45), Assert.Single(track.Keyframes).Value.Vector3);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrderedOperationExponentEditsTheActualCurveOrLegacyAcceleration(bool hasCurve)
    {
        var curve = new AnimationCurve(KeyframeInterpolation.POWER, 0.2, 0.8) { Exponent = 3, Reverse = true };
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(1, 3), new(10, 3), AnimationValue.FromVector3(new(80, 90, 100)), 2)
        {
            ComponentMask = 4, Curve = hasCurve ? curve : null
        };
        var track = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(new(10, 20, 30)), Transforms = [operation]
        };
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [track] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        session.ViewModel.Effects.Target = track.Target;
        var effects = session.ViewModel.Effects;
        Assert.Equal(hasCurve, effects.HasOperationCurve);
        Assert.Equal(hasCurve ? "3" : "2", effects.OperationAcceleration.RawText);
        var before = SceneEvaluator.EvaluateTrack(track, new(2));
        effects.OperationAcceleration.RawText = "5";
        effects.OperationValueZ.RawText = "110.123456789012345";

        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks);
        var edited = Assert.Single(changed.Transforms);
        Assert.Equal(operation.Start, edited.Start);
        Assert.Equal(operation.End, edited.End);
        Assert.Equal(80, edited.Value.Vector3.X);
        Assert.Equal(90, edited.Value.Vector3.Y);
        Assert.Equal(110.123456789012345, edited.Value.Vector3.Z);
        Assert.Equal(hasCurve ? 2 : 5, edited.Acceleration);
        Assert.Equal(hasCurve ? curve with { Exponent = 5 } : null, edited.Curve);
        Assert.NotEqual(before.Vector3.Z, SceneEvaluator.EvaluateTrack(changed, new(2)).Vector3.Z);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }
}
