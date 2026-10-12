using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class EffectScopeAssCompatibilityTests
{
    [Fact]
    public void ExportReportsLocalRangeTranslationWithoutTurningItIntoWholeLineMovement()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1) { Offset = new(4, -12) };
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [range] };
        var position = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.POSITION, TextRangeId: range.Id),
            [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(0, -12))]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [position] };

        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer] });

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.RangeTranslation");
        Assert.DoesNotContain("\\move(", written.Text, StringComparison.Ordinal);
        Assert.Same(position, layer.Tracks[0]);
    }

    [Fact]
    public void EditingProjectedTypographyKeepsLocalTranslationAndItsOwnedRange()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Offset = new(4, -12), GeneratedOrigin = new("bounce", "letters", null, "grapheme")
        };
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [range] };
        var position = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.POSITION, TextRangeId: range.Id),
            [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(0, -12))]);
        var font = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id),
            [new(new(0), 64), new(new(2), 80)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [position, font] };
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var changed = source.Replace("\\fs64", "\\fs70", StringComparison.Ordinal);
        Assert.NotEqual(source, changed);

        var edited = AssTextProjection.Apply(line, changed, layer: layer);

        Assert.Equal(range.Offset, Assert.Single(edited.Line.AnimationRanges, candidate => candidate.Id == range.Id).Offset);
        Assert.Equal(range.GeneratedOrigin, edited.Line.AnimationRanges.Single(candidate => candidate.Id == range.Id).GeneratedOrigin);
        Assert.Equal(position, Assert.Single(edited.TextAnimationTracks!.Value, track => track.Property == AnimationProperty.POSITION));
    }

    [Fact]
    public void ReversePowerTypographyUsesSamplingAndSurvivesProjectedTagChanges()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "ab", End = new(2), AnimationRanges = [range] };
        var font = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id),
        [
            new(new(0), 64, KeyframeInterpolation.POWER) { Exponent = 2, Reverse = true },
            new(new(2), 96)
        ]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [font] };
        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer] });
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var changed = source.Replace("\\fs64", "\\fs70", StringComparison.Ordinal);
        Assert.NotEqual(source, changed);

        var edited = AssTextProjection.Apply(line, changed, layer: layer);

        Assert.Equal(font, Assert.Single(edited.TextAnimationTracks!.Value, track => track.Property == AnimationProperty.FONT_SIZE));
        Assert.Contains(edited.Line.AnimationRanges, candidate => candidate.Id == range.Id);
    }

    [Fact]
    public void WholeLayerReversePowerIsExplicitlyReportedAsAnAssCurveApproximation()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2), Style = new() { Position = new() { Offset = new(100, 80) } }
        };
        var rotation = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 0, KeyframeInterpolation.POWER) { Exponent = 3, Reverse = true },
            new(new(2), 20)
        ]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [rotation] };

        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer] });

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation");
    }

    [Fact]
    public void ReversePowerMaskUsesExpandedEventsInsteadOfForwardAssAcceleration()
    {
        var line = new SubtitleLine { Text = "ab", End = new(2) };
        var top = new AnimationTrack(AnimationProperty.MASK_RECTANGLE_TOP_LEFT,
        [
            new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.POWER) { Exponent = 2, Reverse = true },
            new(new(2), new ScenePoint(20, 0))
        ]);
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(100, 100) },
            Tracks = [top]
        };

        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer], FrameRate = new(4, 1) });

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
    }
}
