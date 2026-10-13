using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ThreeDimensionalEffectWorkflowTests
{
    [Theory]
    [InlineData(AnimationProperty.ROTATION_X, "35", 35)]
    [InlineData(AnimationProperty.ROTATION_Y, "-48", -48)]
    [InlineData(AnimationProperty.PERSPECTIVE_DISTANCE, "540", 540)]
    public async Task NormalRangeScalarDraftChangesOnlyTheExactRangeAndUndoesOnce(AnimationProperty property, string text, double expected)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        var other = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [range, other] };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        var target = new AnimationTrackTarget(property, TextRangeId: range.Id);
        session.ViewModel.Effects.Target = target;
        var row = session.ViewModel.Effects.RotationRows.Concat(session.ViewModel.Effects.PerspectiveRows)
            .Single(row => row.Target == target);

        row.X.RawText = text;
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(session.TryCommitDrafts(false));

        var changed = session.SelectedCue!.AnimationRanges[0];
        var value = property switch
        {
            AnimationProperty.ROTATION_X => changed.RotationX,
            AnimationProperty.ROTATION_Y => changed.RotationY,
            _ => changed.PerspectiveDistance
        };
        Assert.Equal(expected, value);
        Assert.Same(other, session.SelectedCue.AnimationRanges[1]);
        Assert.Equal(layer.Transform, session.SelectedLayer!.Transform);
        Assert.Empty(session.SelectedLayer.Tracks);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(AnimationProperty.ROTATION_X)]
    [InlineData(AnimationProperty.ROTATION_Y)]
    [InlineData(AnimationProperty.PERSPECTIVE_DISTANCE)]
    public async Task ScopedKeyframeEditingPreservesOtherTracksCurveAndStaticRange(AnimationProperty property)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [range] };
        var target = new AnimationTrackTarget(property, TextRangeId: range.Id);
        var frame = new Keyframe(new(1), 300d, KeyframeInterpolation.POWER) { Reverse = true, Exponent = 2.5 };
        var whole = new AnimationTrack(property, [new(MediaTime.Zero, 240d)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [whole, new(target, [frame])] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, target, frame.Time, frame.Time)));
        var row = session.ViewModel.Effects.RotationRows.Concat(session.ViewModel.Effects.PerspectiveRows)
            .Single(row => row.Target == target);

        row.X.RawText = "450";
        Assert.True(session.TryCommitDrafts(false));

        Assert.Equal(frame with { Value = 450d }, Assert.Single(session.SelectedLayer!.Tracks.Single(track => track.Target == target).Keyframes));
        Assert.Same(whole, session.SelectedLayer.Tracks.Single(track => track.Target == whole.Target));
        Assert.Same(line, session.SelectedCue);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task AppearanceStatesExcludeSpatialRowsAndInvalidDistanceKeepsTheFrozenTarget()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", End = new(4), AnimationRanges = [range] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End }] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        var effects = session.ViewModel.Effects;
        effects.Target = new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        Assert.Empty(effects.RotationRows);
        Assert.Empty(effects.PerspectiveRows);
        var target = new AnimationTrackTarget(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id);
        effects.Target = target;
        var row = Assert.Single(effects.PerspectiveRows);
        row.X.RawText = "0";

        effects.SelectedScope = effects.Scopes.Single(scope => scope.Id is null);

        Assert.Equal(target, effects.Target);
        Assert.Equal("0", row.X.RawText);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(effects.RestorePropertyField(row.XFieldKey));
        Assert.False(session.HasProjectDrafts);
    }
}
