using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class ExpandedClipAnimationEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedClipAcceptsSignedKeysAndRoundTripsWithSeparateUndoSteps(bool layerEntrypoint)
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(5), new(9), "Expanded");
        var originalKey = new Keyframe(new(0), 0.25, KeyframeInterpolation.EASE_OUT);
        initial.SetKeyframe(id, AnimationProperty.OPACITY, originalKey);
        var original = initial.Snapshot;
        var editor = new ProjectEditor(original);
        if (layerEntrypoint)
        {
            editor.SetLayerTiming(id, new(3), new(9), TimelineEditMode.CROP);
        }
        else
        {
            editor.SetSubtitleTiming(id, new(3), new(9), TimelineEditMode.CROP);
        }
        var expanded = editor.Snapshot;
        var layer = Assert.Single(expanded.Layers);
        Assert.Equal(new MediaTime(5), layer.Start + originalKey.Time - layer.AnimationOffset);

        var key = new Keyframe(new(-1001, 1000), 0.75, KeyframeInterpolation.POWER) { Exponent = 2.5 };
        editor.SetKeyframe(id, AnimationProperty.OPACITY, key);
        var edited = editor.Snapshot;
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(edited));
        Assert.Equal(key, reloaded.Layers[0].Tracks[0].Keyframes[0]);
        Assert.Equal(originalKey, reloaded.Layers[0].Tracks[0].Keyframes[1]);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(reloaded, new(3999, 1000))).Opacity);
        Assert.True(editor.Undo());
        Assert.Same(expanded, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.True(editor.Redo());
        Assert.Same(edited, editor.Snapshot);
    }

    [Fact]
    public void LeftExtensionPreservesPathAndBothKaraokeClocks()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(5), new(9), "ab");
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)]
        });
        editor.UpdateLayer(id, layer => layer with
        {
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(4)),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.2, KeyframeInterpolation.EASE_IN), new(new(4), 0.8)])]
        });
        var before = editor.Snapshot;
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(before, new(6)));

        editor.SetSubtitleTiming(id, new(3), new(9), TimelineEditMode.CROP);

        var after = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(6)));
        Assert.Equal(evaluated.Transform, after.Transform);
        Assert.Equal(evaluated.Opacity, after.Opacity);
        Assert.Same(before.Layers[0].MotionPath, editor.Snapshot.Layers[0].MotionPath);
        Assert.Equal(before.Subtitles[0].Karaoke, editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(before.Subtitles[0].InactiveKaraoke, editor.Snapshot.Subtitles[0].InactiveKaraoke);
    }

    [Theory]
    [InlineData(3, 9)]
    [InlineData(1, 2)]
    public void EffectScriptCoversTheEntireSignedContentWindowAndCanBeReloaded(int start, int end)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(5), new(9), "Script");
        editor.SetSubtitleTiming(id, new(start), new(end), TimelineEditMode.CROP);
        editor.ApplyEffectScript(id, BuiltinEffectScripts.Get("fade-in-out").Script);
        var layer = Assert.Single(editor.Snapshot.Layers);
        var track = Assert.Single(layer.Tracks);

        Assert.Equal(layer.AnimationOffset, track.Keyframes[0].Time);
        Assert.Equal(layer.AnimationOffset + layer.End - layer.Start, track.Keyframes[^1].Time);
        var reloaded = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(track.Keyframes.Select(frame => (frame.Time, frame.Value, frame.Interpolation,
            frame.CurveStart, frame.CurveEnd, frame.Exponent)),
            reloaded.Layers[0].Tracks[0].Keyframes.Select(frame => (frame.Time, frame.Value, frame.Interpolation,
                frame.CurveStart, frame.CurveEnd, frame.Exponent)));
        Assert.Equal(track.Keyframes.SelectMany(frame => frame.ComponentCurves),
            reloaded.Layers[0].Tracks[0].Keyframes.SelectMany(frame => frame.ComponentCurves));
        Assert.Equal(0, Assert.Single(SceneEvaluator.Evaluate(reloaded, new(start))).Opacity);
        Assert.Empty(SceneEvaluator.Evaluate(reloaded, new(end)));
    }

    [Fact]
    public void SignedRectangleMaskKeysExportAsClipRelativeAssTimes()
    {
        var line = new SubtitleLine { Start = new(3), End = new(5), Text = "Mask" };
        var layer = new ProjectLayer
        {
            Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
            Start = line.Start, End = line.End, AnimationOffset = new(-2),
            Mask = new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(100, 100) },
            Tracks = [new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT,
                [new(new(-2), new ScenePoint(0, 0)), new(new(0), new ScenePoint(20, 40))])]
        };
        var document = new ProjectDocument { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] };

        var written = AssSubtitleFormat.Write(document);
        Assert.Contains("\\t(0,2000,", written.Text, StringComparison.Ordinal);
        var reloaded = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var originalMask = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(-1)));
        var reloadedMask = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(Assert.Single(reloaded.Layers), new(1)));
        Assert.Equal(originalMask.TopLeft, reloadedMask.TopLeft);
        Assert.Equal(originalMask.BottomRight, reloadedMask.BottomRight);
    }
}
