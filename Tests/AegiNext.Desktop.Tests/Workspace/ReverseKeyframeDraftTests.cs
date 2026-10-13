using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ReverseKeyframeDraftTests
{
    [Fact]
    public async Task EditingAKeyframeValuePreservesTheReversedCurveAndItsCroppedPhase()
    {
        var frame = Frame();
        await using var context = new WorkspaceSessionTestContext(Document(frame));
        await context.InitializeAsync();
        var session = context.Session;
        var layer = context.Editor.Snapshot.Layers[0];
        session.SelectCue(layer.SubtitleId!.Value);
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);
        Assert.True(session.SelectKeyframe(new(layer.Id, target, frame.Time, frame.Time)));

        var row = session.PropertyEditing.GetRow(layer.Id, target);
        row.Z.RawText = "15";
        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks[0].Keyframes);
        Assert.Equal(new SceneVector3(2, 3, 15), changed.Value.Vector3);
        Assert.Equal(frame.Time, changed.Time);
        for (var component = 0; component < 3; component++)
        {
            Assert.Equal(frame.GetCurve(component), changed.GetCurve(component));
        }
    }

    [Fact]
    public async Task SelectingANewInterpolationResetsTheReversedCurveAndItsCroppedPhase()
    {
        var frame = Frame();
        await using var context = new WorkspaceSessionTestContext(Document(frame));
        await context.InitializeAsync();
        var session = context.Session;
        var layer = context.Editor.Snapshot.Layers[0];
        session.SelectCue(layer.SubtitleId!.Value);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION_3D, frame.Time, frame.Time)));

        session.ViewModel.Effects.SelectedKeyframeCurveComponent = session.ViewModel.Effects.KeyframeCurveComponents.Single(choice => choice.Mask == 4);
        session.ViewModel.Effects.Interpolation = (int)KeyframeInterpolation.EASE_OUT;
        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks[0].Keyframes);
        var curve = changed.GetCurve(2);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, curve.Interpolation);
        Assert.False(curve.Reverse);
        Assert.Equal(0, curve.CurveStart);
        Assert.Equal(1, curve.CurveEnd);
        Assert.Equal(frame.Value, changed.Value);
        Assert.Equal(frame.GetCurve(0), changed.GetCurve(0));
        Assert.Equal(frame.GetCurve(1), changed.GetCurve(1));
    }

    private static Keyframe Frame()
    {
        return new(new(1), AnimationValue.FromVector3(new(2, 3, 10)), KeyframeInterpolation.POWER)
        {
            Exponent = 2.5,
            CurveStart = 0.2,
            CurveEnd = 0.8,
            Reverse = true
        };
    }

    private static ProjectDocument Document(Keyframe frame)
    {
        var line = new SubtitleLine { Text = "Curve", End = new(2) };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End, Tracks = [new(AnimationProperty.ROTATION_3D, [frame])] }]
        };
    }
}
