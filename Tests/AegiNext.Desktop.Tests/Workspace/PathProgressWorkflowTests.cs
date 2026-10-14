using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

/// <summary>验证路径进度的首帧启用、内容时钟、草稿和撤销语义。</summary>
[Collection("Workspace session")]
public sealed class PathProgressWorkflowTests
{
    private const int SAMPLE_INTERVALS = 32;

    /// <summary>启用首个进度关键帧保留完整自然运动，并在偏移和裁剪后仍使用内容时钟。</summary>
    [Theory]
    [InlineData(-2, false)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(-2, true)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public async Task FirstProgressKeyframePreservesNaturalMotionAcrossSignedOffsetsAndCroppedClips(int offset, bool crop)
    {
        var document = CreateDocument(offset);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        if (crop)
        {
            context.Editor.SetSubtitleTiming(document.Subtitles[0].Id, new(1), new(5), TimelineEditMode.CROP);
        }
        var source = context.Editor.Snapshot;
        var layer = source.Layers[0];
        var originalUndo = context.Editor.CanUndo;
        var currentTime = new MediaTime(offset + 3);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.OPACITY, currentTime, currentTime)));
        var target = new AnimationTrackTarget(AnimationProperty.PATH_PROGRESS);
        var row = session.PropertyEditing.GetRow(layer.Id, target);
        Assert.False(row.IsEnabled);
        Assert.True(row.CanAddKeyframe);
        Assert.False(row.CanReset);

        await session.AddAnimationPropertyKeyframeAsync(layer.Id, target);

        var changed = context.Editor.Snapshot;
        var animated = changed.Layers[0];
        var track = Assert.Single(animated.Tracks, track => track.Target == target);
        Assert.True(track.Keyframes.Length >= 2);
        var range = LayerAnimationTiming.GetRange(animated);
        Assert.All(track.Keyframes, frame => Assert.True(frame.Time >= range.Minimum && frame.Time <= range.Maximum));
        Assert.Equal(Math.Clamp((offset + 3) / 4d, 0, 1),
            Assert.Single(track.Keyframes, frame => frame.Time == currentTime).Value.Scalar, 10);
        Assert.Same(layer.MotionPath, animated.MotionPath);
        Assert.Same(layer.Tracks[0], animated.Tracks.Single(candidate => candidate.Property == AnimationProperty.OPACITY));
        Assert.True(session.PropertyEditing.GetRow(layer.Id, target).IsEnabled);
        AssertNaturalMotionPreserved(layer, animated, changed.Subtitles[0]);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.Equal(originalUndo, context.Editor.CanUndo);
    }

    /// <summary>进度重置只恢复选中帧的自然时刻值并保留曲线，禁用只删除进度轨且恢复自然运动。</summary>
    [Fact]
    public async Task ResetKeepsTheSelectedFramesCurveAndDisableRestoresNaturalMotionWithOneUndoEach()
    {
        var document = CreateDocument(0);
        var target = new AnimationTrackTarget(AnimationProperty.PATH_PROGRESS);
        var frame = new Keyframe(new(3), 0.2, KeyframeInterpolation.POWER)
        {
            Exponent = 2.5, Reverse = true, CurveStart = 0.2, CurveEnd = 0.8
        };
        var progress = new AnimationTrack(target, [new(new(0), 0), frame, new(new(4), 1)]);
        document = document with { Layers = [document.Layers[0] with { Tracks = [document.Layers[0].Tracks[0], progress] }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var layer = document.Layers[0];
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, target, frame.Time, frame.Time)));

        await session.ResetAnimationPropertyAsync(layer.Id, target);

        var reset = session.SelectedLayer!.Tracks.Single(track => track.Target == target);
        Assert.Equal(frame with { Value = 0.75 }, Assert.Single(reset.Keyframes, candidate => candidate.Time == frame.Time));
        Assert.Same(progress.Keyframes[0], reset.Keyframes[0]);
        Assert.Same(progress.Keyframes[^1], reset.Keyframes[^1]);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        await session.SetEffectPropertyAnimationAsync(target, false);

        var disabled = session.SelectedLayer!;
        Assert.Same(layer.Tracks[0], Assert.Single(disabled.Tracks));
        Assert.Same(layer.MotionPath, disabled.MotionPath);
        Assert.False(session.PropertyEditing.GetRow(layer.Id, target).IsEnabled);
        AssertNaturalMotionPreserved(layer with { Tracks = [layer.Tracks[0]] }, disabled, document.Subtitles[0]);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>路径缺失或未启用轨道时拒绝静态进度草稿，不进入不支持的基础值保存或写入工程。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingProgressTrackDisablesStaticValueDraftsWithoutThrowingOrChangingTheProject(bool hasPath)
    {
        var document = CreateDocument(0);
        if (!hasPath)
        {
            document = document with { Layers = [document.Layers[0] with { MotionPath = null }] };
        }
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var layer = document.Layers[0];
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.OPACITY, new(3), new(3))));
        var target = new AnimationTrackTarget(AnimationProperty.PATH_PROGRESS);
        var row = session.PropertyEditing.GetRow(layer.Id, target);
        Assert.False(row.IsEnabled);
        Assert.Equal(hasPath, row.CanAddKeyframe);
        Assert.False(row.CanReset);
        try
        {
            row.BeginEdit("effects");
            row.X.RawText = "0.6";
            Assert.False(session.TryCommitDrafts(false));
            Assert.Same(document, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            Assert.DoesNotContain(session.SelectedLayer!.Tracks, track => track.Property == AnimationProperty.PATH_PROGRESS);
        }
        finally
        {
            row.Restore(row.XFieldKey);
            session.TryCommitDrafts(false);
        }
        if (!hasPath)
        {
            await session.AddAnimationPropertyKeyframeAsync(layer.Id, target);
            Assert.Same(document, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
        }
    }

    private static ProjectDocument CreateDocument(int offset)
    {
        var cue = new SubtitleLine { End = new(6), Text = "Path progress 路径进度" };
        var layer = new ProjectLayer
        {
            Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End,
            AnimationOffset = new(offset),
            Transform = new() { Position = new(11, 13) },
            MotionPath = new(new(new(0, 0), [new(new(80, -40), new(160, 70), new(240, 20))]), new(4)),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(offset + 3), 0.5)])]
        };
        return new() { Subtitles = [cue], Layers = [layer] };
    }

    private static void AssertNaturalMotionPreserved(ProjectLayer natural, ProjectLayer animated, SubtitleLine subtitle)
    {
        var range = LayerAnimationTiming.GetRange(animated);
        var duration = range.Maximum - range.Minimum;
        for (var sample = 0; sample <= SAMPLE_INTERVALS; sample++)
        {
            var time = range.Minimum + new MediaTime(duration.Numerator * sample, duration.Denominator * SAMPLE_INTERVALS);
            var expected = SceneEvaluator.EvaluateLayer(natural, subtitle, time).Transform.Position;
            var actual = SceneEvaluator.EvaluateLayer(animated, subtitle, time).Transform.Position;
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
        }
    }
}
