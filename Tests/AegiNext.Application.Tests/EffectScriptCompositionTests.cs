using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class EffectScriptCompositionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 5)]
    [InlineData(false, -3)]
    [InlineData(true, 0)]
    [InlineData(true, 5)]
    [InlineData(true, -3)]
    public void ApplyingBothFadePresetsKeepsEntranceAndExitInEitherOrder(bool reverse, int offset)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(10), new(14), "Composed fade");
        editor.UpdateLayer(id, layer => layer with { AnimationOffset = new(offset) });
        var first = reverse ? "fade-out" : "fade-in";
        var second = reverse ? "fade-in" : "fade-out";

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get(first).Script);
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get(second).Script);

        var track = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(new MediaTime(offset), track.Keyframes[0].Time);
        Assert.Equal(new MediaTime(offset + 4), track.Keyframes[^1].Time);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(offset)), 12);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(203, 20))).Opacity, 12);
        Assert.Equal(1, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(12))).Opacity, 12);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(277, 20))).Opacity, 12);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(offset + 4)), 12);
        Assert.Empty(SceneEvaluator.Evaluate(editor.Snapshot, new(14)));
    }

    [Theory]
    [InlineData(KeyframeInterpolation.HOLD)]
    [InlineData(KeyframeInterpolation.LINEAR)]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT)]
    [InlineData(KeyframeInterpolation.POWER)]
    public void EmptyStayPreservesEverySampleOfTheExistingChangingAnimation(KeyframeInterpolation interpolation)
    {
        var editor = CreateEditorWithChangingOpacity(interpolation);
        var id = Assert.Single(editor.Snapshot.Layers).Id;
        var original = OpacityTrack(editor.Snapshot);
        Assert.NotEqual(SceneEvaluator.EvaluateScalarTrack(original, new(1)), SceneEvaluator.EvaluateScalarTrack(original, new(2)));
        Assert.Equal(1, SceneEvaluator.EvaluateScalarTrack(original, new(37, 10)), 12);

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-out").Script);

        var combined = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        for (var sample = 0; sample <= 148; sample++)
        {
            var time = new MediaTime(sample, 40);
            var expected = SceneEvaluator.EvaluateScalarTrack(original, time);
            var actual = SceneEvaluator.EvaluateScalarTrack(combined, time);
            Assert.True(Math.Abs(expected - actual) <= 1e-9,
                $"The empty stay must preserve the original animation at {time}: expected {expected}, actual {actual}.");
        }

        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(combined, new(77, 20)), 12);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(combined, new(4)), 12);
    }

    [Fact]
    public void ExplicitHoldStillOverridesTheExistingChangingAnimation()
    {
        var editor = CreateEditorWithChangingOpacity(KeyframeInterpolation.EASE_IN);
        var id = Assert.Single(editor.Snapshot.Layers).Id;
        Assert.NotEqual(1, SceneEvaluator.EvaluateScalarTrack(OpacityTrack(editor.Snapshot), new(3, 2)));
        var script = EffectScriptParser.ParseLegacy("""
            effect "explicit-hold-fade-out" version 1
            short-clip compress
            segment stay flex 1
                at 0 opacity base hold
                at 1 opacity base
            end
            segment exit fixed 300ms
                at 0 opacity base ease-in
                at 1 opacity 0
            end
            """);

        editor.ApplyEffectScript(id, script);

        var track = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(1, SceneEvaluator.EvaluateScalarTrack(track, new(3, 2)), 12);
        Assert.Equal(1, SceneEvaluator.EvaluateScalarTrack(track, new(5, 2)), 12);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, new(77, 20)), 12);
    }

    [Fact]
    public void AStayAnimatingScalePreservesItsUnmentionedOpacity()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "Per-property composition");
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-in").Script);
        var script = EffectScriptParser.ParseLegacy("""
            effect "scale-and-fade-out" version 1
            short-clip compress
            segment stay flex 1
                at 0 scale base
                at 1 scale factor(2, 2)
            end
            segment exit fixed 300ms
                at 0 opacity base ease-in
                at 1 opacity 0
            end
            """);

        editor.ApplyEffectScript(id, script);

        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(3, 20))).Opacity, 12);
        var middle = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(37, 20)));
        Assert.Equal(1.5, middle.Transform.ScaleX, 12);
        Assert.Equal(1.5, middle.Transform.ScaleY, 12);
        var exit = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(77, 20)));
        Assert.Equal(0.75, exit.Opacity, 12);
        Assert.Equal(2, exit.Transform.ScaleX, 12);
        Assert.Equal(2, exit.Transform.ScaleY, 12);
    }

    [Fact]
    public void SecondApplicationCreatesOneUndoAndTheCompositionSurvivesPersistence()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "Persistent composition");
        initial.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-in").Script);
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-out").Script);

        var applied = editor.Snapshot;
        Assert.Equal(1, changes);
        ProjectValidator.Validate(applied);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(applied));
        Assert.Equal(OpacityTrack(applied).Keyframes.AsEnumerable(), OpacityTrack(reloaded).Keyframes.AsEnumerable());
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(reloaded, new(3, 20))).Opacity, 12);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(reloaded, new(77, 20))).Opacity, 12);
    }

    [Fact]
    public void BatchCompositionUsesEachClipsRangeAndCommitsWithOneUndo()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(1), new(5), "First");
        var second = initial.AddSubtitle(new(10), new(16), "Second");
        initial.UpdateLayer(first, layer => layer with { AnimationOffset = new(-2), Opacity = 0.8 });
        initial.UpdateLayer(second, layer => layer with { AnimationOffset = new(5), Opacity = 0.6 });
        initial.ApplyEffectScript([first, second], BuiltinEffectScripts.Get("fade-in").Script);
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplyEffectScript([first, second], BuiltinEffectScripts.Get("fade-out").Script);

        var applied = editor.Snapshot;
        Assert.Equal(1, changes);
        ProjectValidator.Validate(applied);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
        foreach (var layer in applied.Layers)
        {
            var track = layer.Tracks.Single(track => track.Property == AnimationProperty.OPACITY);
            Assert.Equal(layer.AnimationOffset, track.Keyframes[0].Time);
            Assert.Equal(layer.AnimationOffset + layer.End - layer.Start, track.Keyframes[^1].Time);
            Assert.Equal(layer.Opacity * 0.75,
                Assert.Single(SceneEvaluator.Evaluate(applied, layer.Start + new MediaTime(3, 20))).Opacity, 12);
            Assert.Equal(layer.Opacity * 0.75,
                Assert.Single(SceneEvaluator.Evaluate(applied, layer.End - new MediaTime(3, 20))).Opacity, 12);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentFixedEdgesComposeWithoutCompressingTheirDeclaredDurations(bool reverse)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(3, 5), "Adjacent fades");
        var first = reverse ? "fade-out" : "fade-in";
        var second = reverse ? "fade-in" : "fade-out";

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get(first).Script);
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get(second).Script);

        var track = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(new MediaTime[] { new(0), new(3, 10), new(3, 5) }, track.Keyframes.Select(frame => frame.Time));
        Assert.Equal(KeyframeInterpolation.EASE_OUT, track.Keyframes[0].Interpolation);
        Assert.Equal(KeyframeInterpolation.EASE_IN, track.Keyframes[1].Interpolation);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, new(3, 20)), 12);
        Assert.Equal(1, SceneEvaluator.EvaluateScalarTrack(track, new(3, 10)), 12);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, new(9, 20)), 12);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(3, 5)), 12);
    }

    [Fact]
    public void OverlappingEdgesWithDifferentJoinValuesRejectTheWholeBatchAndPreserveHistory()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(0), new(4), "Composable member");
        var second = initial.AddSubtitle(new(5), new(27, 5), "Conflicting member");
        initial.ApplyEffectScript([first, second], BuiltinEffectScripts.Get("fade-in").Script);
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        editor.UpdateLayer(first, layer => layer with { Name = "Redo fixture" });
        var redo = editor.Snapshot;
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<EffectScriptException>(() =>
            editor.ApplyEffectScript([first, second], BuiltinEffectScripts.Get("fade-out").Script));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, changes);
        Assert.True(editor.Redo());
        Assert.Same(redo, editor.Snapshot);
    }

    [Theory]
    [InlineData(false, 300)]
    [InlineData(false, 250)]
    [InlineData(false, 1)]
    [InlineData(true, 300)]
    [InlineData(true, 250)]
    [InlineData(true, 1)]
    public void ASecondPresetCoveringTheWholeShortClipReplacesTheExistingTrack(bool reverse, int milliseconds)
    {
        var editor = new ProjectEditor();
        var duration = new MediaTime(milliseconds, 1000);
        var id = editor.AddSubtitle(new(0), duration, "Whole-clip replacement");
        var first = reverse ? "fade-out" : "fade-in";
        var second = reverse ? "fade-in" : "fade-out";
        var script = BuiltinEffectScripts.Get(second).Script;
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get(first).Script);
        var expected = Assert.Single(EffectScriptCompiler.Compile(script, Assert.Single(editor.Snapshot.Layers)));

        editor.ApplyEffectScript(id, script);

        var track = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(expected.Keyframes.AsEnumerable(), track.Keyframes.AsEnumerable());
        Assert.Equal(reverse ? 0 : 1, SceneEvaluator.EvaluateScalarTrack(track, new(0)), 12);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, duration / 2), 12);
        Assert.Equal(reverse ? 1 : 0, SceneEvaluator.EvaluateScalarTrack(track, duration), 12);
    }

    [Theory]
    [InlineData("fade-in", false)]
    [InlineData("fade-in", true)]
    [InlineData("fade-out", false)]
    [InlineData("fade-out", true)]
    public void ReapplyingTheSamePresetKeepsTheSnapshotNotificationsAndUndoRedoHistory(string preset, bool composed)
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "Repeated preset");
        if (composed)
        {
            var other = preset == "fade-in" ? "fade-out" : "fade-in";
            initial.ApplyEffectScript(id, BuiltinEffectScripts.Get(other).Script);
        }

        var script = BuiltinEffectScripts.Get(preset).Script;
        initial.ApplyEffectScript(id, script);
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        editor.UpdateLayer(id, layer => layer with { Name = "Redo fixture" });
        var redo = editor.Snapshot;
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplyEffectScript(id, script);

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, changes);
        Assert.True(editor.Redo());
        Assert.Same(redo, editor.Snapshot);
    }

    [Fact]
    public void PersistedKeyframeTracksComposeWithoutTheOriginalPresetSource()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "Reloaded entrance");
        initial.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-in").Script);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(initial.Snapshot));
        var editor = new ProjectEditor(reloaded);

        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-out").Script);

        var track = OpacityTrack(editor.Snapshot);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, new(3, 20)), 12);
        Assert.Equal(1, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 12);
        Assert.Equal(0.75, SceneEvaluator.EvaluateScalarTrack(track, new(77, 20)), 12);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(4)), 12);
    }

    private static ProjectEditor CreateEditorWithChangingOpacity(KeyframeInterpolation interpolation)
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "Changing opacity");
        initial.UpdateLayer(id, layer => layer with
        {
            Tracks = [new(AnimationProperty.OPACITY,
            [
                new(new(0), 1, KeyframeInterpolation.HOLD),
                new(new(1), 0.4, interpolation) { Exponent = 2.5 },
                new(new(2), 0.8, KeyframeInterpolation.EASE_OUT),
                new(new(37, 10), 1, KeyframeInterpolation.HOLD),
                new(new(4), 1)
            ])]
        });
        return new(initial.Snapshot);
    }

    private static AnimationTrack OpacityTrack(ProjectDocument document)
    {
        return Assert.Single(document.Layers).Tracks.Single(track => track.Property == AnimationProperty.OPACITY);
    }
}
