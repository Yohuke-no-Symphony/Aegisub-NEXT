using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class EffectScriptEditingTests
{
    [Fact]
    public void ApplyingScriptKeepsClipIdentityOtherTracksAndPathAndCreatesOneUndo()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(10), new(12), "字幕自然尺寸");
        initial.SetKeyframe(id, AnimationProperty.ROTATION_3D, new(new(0), new SceneVector3(0, 0, 15)));
        initial.UpdateLayer(id, layer => layer with
        {
            AnimationOffset = new(5),
            Tracks = [new(AnimationProperty.ROTATION_3D, [new(new(5), new SceneVector3(0, 0, 15))])],
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(7)),
            Transform = new(100, 200, 2, 3)
        });
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("pop-in").Script);

        var layer = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(id, layer.Id);
        Assert.Equal(id, Assert.Single(editor.Snapshot.Subtitles).Id);
        Assert.Same(original.Subtitles[0], editor.Snapshot.Subtitles[0]);
        Assert.Same(original.Layers[0].MotionPath, layer.MotionPath);
        Assert.Same(original.Layers[0].Tracks[0], layer.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D));
        var first = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(10)));
        Assert.Equal(0.4, first.Transform.ScaleX, 12);
        Assert.Equal(0.6, first.Transform.ScaleY, 12);
        Assert.Equal(1, changes);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(layer.Tracks.SelectMany(track => track.Keyframes), reloaded.Layers[0].Tracks.SelectMany(track => track.Keyframes));
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        ProjectValidator.Validate(editor.Snapshot);
    }

    [Fact]
    public void RejectPolicyAndConflictingBoundariesLeaveSnapshotAndHistoryUntouched()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2, 5), "short");
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var reject = BuiltinEffectScripts.Get("fade-in-out").Script with { ShortClipPolicy = EffectScriptShortClipPolicy.REJECT };

        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, reject));
        var conflict = EffectScriptParser.Parse(BuiltinEffectScripts.Get("fade-in-out").Source.Replace("at 0 opacity base ease-in", "at 0 opacity 0.5 ease-in", StringComparison.Ordinal));
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, conflict));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void SubtitleStrokeBaseComesFromItsStyleRatherThanTheLayerPlaceholder()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "stroke");
        editor.UpdateSubtitle(id, line => line with { Style = line.Style with { StrokeWidth = 4 } });
        var script = EffectScriptParser.ParseLegacy("""
            effect "stroke" version 1
            short-clip compress
            segment all flex 1
                at 0 stroke-width factor(2)
                at 1 stroke-width base
            end
            """);

        editor.ApplyEffectScript(id, script);

        var track = Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks);
        Assert.Equal(8, track.Keyframes[0].Value.Scalar);
        Assert.Equal(4, track.Keyframes[^1].Value.Scalar);
        Assert.Equal(8, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(0))).StrokeWidth);
    }

    [Fact]
    public void CompiledTracksRemainValidAfterMoveCropAndStretch()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "timing");
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-in-out").Script);
        editor.ShiftSubtitle(id, new(7));
        Assert.Equal(0, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(7))).Opacity);
        editor.SetSubtitleTiming(id, new(8), new(10), TimelineEditMode.CROP);
        editor.SetSubtitleTiming(id, new(8), new(13), TimelineEditMode.STRETCH);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.All(editor.Snapshot.Layers[0].Tracks.SelectMany(track => track.Keyframes), frame =>
            Assert.Equal(frame.Time, LayerAnimationTiming.ClampTime(editor.Snapshot.Layers[0], frame.Time)));
        Assert.Empty(SceneEvaluator.Evaluate(editor.Snapshot, new(13)));
    }
}
