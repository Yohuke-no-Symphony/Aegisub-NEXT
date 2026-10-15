using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class BatchEffectScriptEditingTests
{
    [Fact]
    public void BatchUsesEachClipsDurationOffsetAndStyleAndPreservesUnrelatedDataWithOneUndo()
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(1), new(3), "First");
        var second = initial.AddSubtitle(new(5), new(9), "Second");
        var untouched = initial.AddSubtitle(new(10), new(11), "Untouched");
        initial.UpdateSubtitle(first, line => line with { Style = line.Style with { StrokeWidth = 2 } });
        initial.UpdateSubtitle(second, line => line with { Style = line.Style with { StrokeWidth = 4 } });
        initial.UpdateLayer(first, layer => layer with
        {
            AnimationOffset = new(1), Opacity = 0.8,
            Tracks = [new(AnimationProperty.ROTATION_3D, [new(new(1), new SceneVector3(0, 0, 15))]), new(AnimationProperty.OPACITY, [new(new(1), 0.3)])],
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(7))
        });
        initial.UpdateLayer(second, layer => layer with { AnimationOffset = new(2), Opacity = 0.5 });
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        var script = EffectScriptParser.ParseLegacy("""
            effect "per-clip-values" version 1
            short-clip compress
            segment all flex 1
                at 0 opacity 0
                at 1 opacity base
                at 0 stroke-width factor(2)
                at 1 stroke-width base
            end
            """);

        editor.ApplyEffectScript([first, second], script);

        var applied = editor.Snapshot;
        foreach (var id in new[] { first, second })
        {
            var before = original.Layers.Single(layer => layer.Id == id);
            var after = applied.Layers.Single(layer => layer.Id == id);
            var style = original.Subtitles.Single(line => line.Id == id).Style;
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Start, after.Start);
            Assert.Equal(before.End, after.End);
            Assert.Equal(before.AnimationOffset, after.AnimationOffset);
            Assert.Same(before.MotionPath, after.MotionPath);
            Assert.Same(before.Transform, after.Transform);
            Assert.All(after.Tracks, track =>
            {
                Assert.Equal(before.AnimationOffset, track.Keyframes[0].Time);
                if (track.Property != AnimationProperty.ROTATION_3D)
                {
                    Assert.Equal(before.End - before.Start + before.AnimationOffset, track.Keyframes[^1].Time);
                }
            });
            var opacity = after.Tracks.Single(track => track.Property == AnimationProperty.OPACITY);
            Assert.Equal(before.Opacity, opacity.Keyframes[^1].Value.Scalar);
            var stroke = after.Tracks.Single(track => track.Property == AnimationProperty.STROKE_WIDTH);
            Assert.Equal(style.StrokeWidth * 2, stroke.Keyframes[0].Value.Scalar);
            Assert.Equal(style.StrokeWidth, stroke.Keyframes[^1].Value.Scalar);
        }
        Assert.Same(original.Layers[0].Tracks[0], applied.Layers[0].Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D));
        Assert.Same(original.Layers.Single(layer => layer.Id == untouched), applied.Layers.Single(layer => layer.Id == untouched));
        Assert.Equal(original.Subtitles, applied.Subtitles);
        Assert.Equal(1, changes);
        ProjectValidator.Validate(applied);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(applied));
        Assert.Equal(applied.Layers.SelectMany(layer => layer.Tracks).SelectMany(track => track.Keyframes),
            reloaded.Layers.SelectMany(layer => layer.Tracks).SelectMany(track => track.Keyframes));
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("ordered")]
    [InlineData("missing-mask")]
    [InlineData("missing-layer")]
    public void AnyRejectedMemberPreservesSnapshotUndoRedoAndChangeNotifications(string failure)
    {
        var initial = new ProjectEditor();
        var first = initial.AddSubtitle(new(0), new(2), "First");
        var second = initial.AddSubtitle(new(3), new(5), "Second");
        var script = BuiltinEffectScripts.Get("fade-in-out").Script;
        if (failure == "short")
        {
            initial.SetSubtitleTiming(second, new(3), new(31, 10), TimelineEditMode.CROP);
            script = script with { ShortClipPolicy = EffectScriptShortClipPolicy.REJECT };
        }
        else if (failure == "ordered")
        {
            initial.SetAnimationTransform(second, new(AnimationProperty.OPACITY), 1,
                new(Guid.NewGuid(), new(0), new(2), 0.5));
        }
        else if (failure == "missing-mask")
        {
            initial.SetClipMask(first, new VectorClipMask { Contours = [new() { Nodes = [new(), new() { Position = new(10, 20) }] }] });
            script = EffectScriptParser.ParseLegacy("""
                effect "mask-node" version 1
                short-clip compress
                segment all flex 1
                    at 0 mask-node(1,1).position base
                    at 1 mask-node(1,1).position offset(10, 0)
                end
                """);
        }
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        editor.UpdateLayer(first, layer => layer with { Name = "Redo fixture" });
        var redo = editor.Snapshot;
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        var ids = failure == "missing-layer" ? new[] { first, second, Guid.NewGuid() } : [first, second];

        var error = Record.Exception(() => editor.ApplyEffectScript(ids, script));

        if (failure == "missing-layer")
        {
            Assert.IsType<KeyNotFoundException>(error);
        }
        else
        {
            Assert.IsType<EffectScriptException>(error);
        }
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, changes);
        Assert.True(editor.Redo());
        Assert.Same(redo, editor.Snapshot);
    }

    [Fact]
    public void EmptySelectionIsNoOpAndDuplicateIdsApplyOnlyOnce()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "Only clip");
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        var script = BuiltinEffectScripts.Get("pop-in").Script;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ApplyEffectScript(Array.Empty<Guid>(), script);

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
        editor.ApplyEffectScript([id, id], script);
        Assert.Equal(1, changes);
        Assert.Equal(EffectScriptCompiler.Compile(script, original.Layers[0]).SelectMany(track => track.Keyframes),
            editor.Snapshot.Layers[0].Tracks.SelectMany(track => track.Keyframes));
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void BatchUpdatesFlatClipsAndRetainsTheirIdentity()
    {
        var first = new SubtitleLine { Start = new(0), End = new(2), Text = "Nested" };
        var second = new SubtitleLine { Start = new(3), End = new(5), Text = "Root" };
        var firstLayer = Layer(first);
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [firstLayer, Layer(second)] };
        var editor = new ProjectEditor(original);

        editor.ApplyEffectScript([first.Id, second.Id], BuiltinEffectScripts.Get("fade-in-out").Script);

        Assert.Equal(firstLayer.Id, editor.Snapshot.Layers[0].Id);
        Assert.Same(firstLayer.Transform, editor.Snapshot.Layers[0].Transform);
        Assert.Single(editor.Snapshot.Layers[0].Tracks);
        Assert.Single(editor.Snapshot.Layers[1].Tracks);
        ProjectValidator.Validate(editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };
}
