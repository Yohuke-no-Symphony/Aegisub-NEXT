using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectScriptTextRangeEditingTests
{
    [Fact]
    public void ScopedScriptKeepsOtherTargetsAndCreatesOneUndoWithNoRangeIdentityInItsTemplate()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "AB");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        initial.SetSubtitleAnimationRange(id, range);
        initial.SetKeyframe(id, AnimationProperty.FILL, new(new(0), SceneColor.White));
        var snapshot = initial.Snapshot;
        var editor = new ProjectEditor(snapshot);
        var target = new AnimationTrackTarget(AnimationProperty.SHADOW_COLOR, TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE);
        var source = Source("shadow-color", "rgba(0, 0, 0, 0.5)");
        editor.ApplyEffectScript(id, EffectScriptParser.ParseLegacy(source), target);

        var layer = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(2, layer.Tracks.Length);
        Assert.Same(snapshot.Layers[0].Tracks[0], layer.Tracks.Single(track => track.Target.TextRangeId is null));
        Assert.Equal(target, layer.Tracks.Single(track => track.Target.TextRangeId.HasValue).Target);
        Assert.DoesNotContain(range.Id.ToString(), source);
        Assert.True(editor.Undo());
        Assert.Same(snapshot, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void WholeLineVisualStateCanBatchApplyWithEachSubtitlesOwnInheritedBase()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(0), new(2), "A");
        var second = initial.AddSubtitle(new(3), new(6), "B");
        initial.UpdateSubtitle(first, line => line with { KaraokeStyleSpans = [new(0, 1, new() { ShadowBlur = 4 })] });
        initial.UpdateSubtitle(second, line => line with { KaraokeStyleSpans = [new(0, 1, new() { ShadowBlur = 8 })] });
        var snapshot = initial.Snapshot;
        var editor = new ProjectEditor(snapshot);
        editor.ApplyEffectScript([first, second], EffectScriptParser.ParseLegacy(Source("shadow-blur", "base")),
            new(AnimationProperty.SHADOW_BLUR, State: SubtitleAnimationState.ACTIVE));

        Assert.Equal(4, editor.Snapshot.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.Equal(8, editor.Snapshot.Layers[1].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.All(editor.Snapshot.Layers, layer => Assert.Equal(SubtitleAnimationState.ACTIVE, Assert.Single(layer.Tracks).Target.State));
        Assert.True(editor.Undo());
        Assert.Same(snapshot, editor.Snapshot);
    }

    [Fact]
    public void InvalidScopeOrMixedBaseAtomicallyLeavesSnapshotAndHistoryUntouched()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(0), new(2), "AB");
        var second = initial.AddSubtitle(new(3), new(5), "CD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        initial.SetSubtitleAnimationRange(first, range);
        initial.UpdateSubtitle(first, line => line with { InlineSpans = [new(1, 1, new() { FontSize = 50 })] });
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var context = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id);
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript([first, second],
            EffectScriptParser.ParseLegacy(Source("fill", "base")), context));
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(first,
            EffectScriptParser.ParseLegacy(Source("font-size", "base")), context));
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(first,
            EffectScriptParser.ParseLegacy(Source("opacity", "1")), context));
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(second,
            EffectScriptParser.ParseLegacy(Source("fill", "base")), context));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void WholeLineStateMixedBaseRejectsTheEntireBatch()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(0), new(2), "A");
        var second = initial.AddSubtitle(new(3), new(5), "BC");
        initial.UpdateSubtitle(second, line => line with { KaraokeStyleSpans = [new(0, 1, new() { Fill = SceneColor.Black })] });
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);

        var error = Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript([first, second],
            EffectScriptParser.ParseLegacy(Source("fill", "base")), new(AnimationProperty.FILL, State: SubtitleAnimationState.ACTIVE)));

        Assert.Contains("混合", error.Message);
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void AStoredVersionThreeTemplateCanRetargetDifferentRangesAndInheritedSizes()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "AB");
        var second = editor.AddSubtitle(new(3), new(5), "CD");
        var firstRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var secondRange = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        editor.SetSubtitleAnimationRange(first, firstRange);
        editor.SetSubtitleAnimationRange(second, secondRange);
        editor.UpdateSubtitle(first, line => line with { InlineSpans = [new(0, 1, new() { FontSize = 20 })] });
        editor.UpdateSubtitle(second, line => line with { InlineSpans = [new(1, 1, new() { FontSize = 50 })] });
        const string SOURCE = """
            effect "scoped-template" version 3
            short-clip compress
            scope clip current
                segment stay flex 1
                    at 0 font-size factor(1.5)
                    at 1 font-size factor(1.5)
                end
            end
            """;
        var bytes = EffectScriptPresetStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Range size", SOURCE)] });
        var template = EffectScriptPresetService.Validate(Assert.Single(EffectScriptPresetStore.Deserialize(bytes).Presets));
        editor.ApplyEffectScript(first, template, new(AnimationProperty.FONT_SIZE, TextRangeId: firstRange.Id));
        editor.ApplyEffectScript(second, template, new(AnimationProperty.FONT_SIZE, TextRangeId: secondRange.Id));

        Assert.Equal(firstRange.Id, editor.Snapshot.Layers[0].Tracks[0].Target.TextRangeId);
        Assert.Equal(secondRange.Id, editor.Snapshot.Layers[1].Tracks[0].Target.TextRangeId);
        Assert.Equal(30, editor.Snapshot.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.Equal(75, editor.Snapshot.Layers[1].Tracks[0].Keyframes[0].Value.Scalar);
        ProjectValidator.Validate(ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot)));
    }

    private static string Source(string property, string value) => $"""
        effect "scope-edit" version 1
        short-clip compress
        segment stay flex 1
            at 0 {property} {value}
            at 1 {property} {value}
        end
        """;
}
