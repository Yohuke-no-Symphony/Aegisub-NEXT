using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssKaraokeVisualCompatibilityTests
{
    [Fact]
    public void StrokeOnlyOverrideRetainsSentenceHighlightFill()
    {
        var line = Line(KaraokeHighlightKind.STEP) with
        {
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "highlight", new() { Fill = new(1, 0, 0), ShadowBlur = 0 })
        };
        var result = AssSubtitleFormat.Parse(AssSubtitleFormat.Write(Document(line)).Text);
        Assert.Equal(new SceneColor(1, 0, 0), Assert.Single(result.Lines).KaraokeStyleSpans[0].ActiveStyle!.Fill);
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void StepVisualsRoundTripUsingQuantizedInstantTransformWithoutLeaking(KaraokeHighlightKind kind)
    {
        var line = Line(kind);
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains("\\t(250,250,", written.Text, StringComparison.Ordinal);
        Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Dialogue:", StringComparison.Ordinal));
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Ass.KaraokeVisual");
        var parsed = AssSubtitleFormat.Parse(written.Text);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
        var imported = Assert.Single(parsed.Lines);
        var active = imported.KaraokeStyleSpans[0].ActiveStyle!;
        Assert.Equal(6, active.StrokeWidth);
        Assert.Equal(new SceneColor(0, 1, 0), active.Stroke);
        Assert.Equal(new ScenePoint(8, 9), active.ShadowOffset);
        Assert.Equal(new SceneColor(1, 0, 0), active.ShadowColor);
        Assert.NotEqual(6, KaraokeVisualStyleResolver.RangeStyleAt(imported, 1, KaraokeVisualState.ACTIVE)?.StrokeWidth);
    }

    [Fact]
    public void ZeroStartUsesStaticActiveVisualInsteadOfZeroZeroTransform()
    {
        var line = Line(KaraokeHighlightKind.STEP);
        line = line with { Karaoke = [line.Karaoke[0] with { Start = MediaTime.Zero }, line.Karaoke[1]] };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.DoesNotContain("\\t(0,0,", written.Text, StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        var ordinary = imported.InlineSpans.First(span => span.Utf16Start == 0).Style.ApplyTo(imported.Style);
        Assert.Equal(6, ordinary.StrokeWidth);
    }

    [Theory]
    [InlineData("\\t(200,300,\\bord6)")]
    [InlineData("\\t(200,200,\\bord6)")]
    [InlineData("\\t(250,250,\\fs20)")]
    [InlineData("\\t(250,250,\\1c&H0000FF&)")]
    public void ContinuousAndInstantStylesConvertToNativeTracks(string transform)
    {
        var line = new SubtitleLine { Text = "a", End = new(1), Style = new() { ShadowBlur = 0 } };
        var parsed = AssTextProjection.Apply(line, "{\\k25}{" + transform + "\\k50}a");
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
        Assert.NotNull(parsed.TextAnimationTracks);
        Assert.NotEmpty(parsed.TextAnimationTracks.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SweepReportsOnlyActualVisualDifferences(bool different)
    {
        var line = Line(KaraokeHighlightKind.SWEEP);
        line = line with
        {
            Karaoke = [line.Karaoke[0]],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = different ? 6 : line.Style.StrokeWidth })]
        };
        var result = AssSubtitleFormat.Write(Document(line));
        Assert.Equal(different, result.Diagnostics.Any(item => item.Code == "Ass.KaraokeVisual"));
        Assert.Contains("\\kf50", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibleShadowBlurReportsDifferentAssBlurSemantics()
    {
        var line = new SubtitleLine { Text = "a", End = new(1), Style = new() { ShadowBlur = 2 } };
        Assert.Contains(AssSubtitleFormat.Write(Document(line)).Diagnostics, item => item.Code == "Ass.ShadowBlur");
        var transparent = line with { Style = line.Style with { Fill = SceneColor.Transparent, Stroke = SceneColor.Transparent, ShadowColor = SceneColor.Transparent } };
        Assert.DoesNotContain(AssSubtitleFormat.Write(Document(transparent)).Diagnostics, item => item.Code == "Ass.ShadowBlur");
    }

    [Fact]
    public void ProjectionChangingOnlyActiveStrokeKeepsFillPrecisionNullInheritanceAndTime()
    {
        var preciseFill = new SceneColor(3.123456789, -0.3, 0.2123456789, 0.73123456789);
        var line = Line(KaraokeHighlightKind.STEP);
        line = line with
        {
            Karaoke = [line.Karaoke[0] with
            {
                Start = new(1, 7), End = new(6, 7)
            }],
            KaraokeStyleSpans = [new(0, 1, new() { Fill = preciseFill, StrokeWidth = 6 })]
        };
        var source = AssTextProjection.Create(line).Source.Replace("\\bord6)", "\\bord7)", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source).Line;
        var clip = Assert.Single(edited.Karaoke);
        var style = Assert.Single(edited.KaraokeStyleSpans);
        Assert.Equal(7, style.ActiveStyle!.StrokeWidth);
        Assert.Equal(preciseFill, style.ActiveStyle.Fill);
        Assert.Null(style.ActiveStyle.ShadowOffset);
        Assert.Null(style.ActiveStyle.Stroke);
        Assert.Null(style.InactiveStyle);
        Assert.Equal(line.Karaoke[0].Id, clip.Id);
        Assert.Equal(line.Karaoke[0].Start, clip.Start);
        Assert.Equal(line.Karaoke[0].End, clip.End);
    }

    [Fact]
    public void ProjectionChangingDurationDoesNotQuantizeUntouchedStart()
    {
        var line = Line(KaraokeHighlightKind.STEP);
        line = line with { Karaoke = [line.Karaoke[0] with { Start = new(1, 7), End = new(6, 7) }] };
        var projection = AssTextProjection.Create(line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace("\\k72}", "\\k82}", StringComparison.Ordinal)).Line;
        Assert.Equal(new MediaTime(1, 7), edited.Karaoke[0].Start);
        Assert.Equal(new MediaTime(96, 100), edited.Karaoke[0].End);
    }

    [Fact]
    public void ProjectionChangingActiveAlphaKeepsNativeRgbAndUntouchedShadowAxis()
    {
        var precise = new SceneColor(4.123456789, -0.3, 0.2123456789, 0.73123456789);
        var line = Line(KaraokeHighlightKind.STEP);
        line = line with
        {
            Karaoke = [line.Karaoke[0]],
            KaraokeStyleSpans = [new(0, 1, new() { Fill = precise, ShadowOffset = new(8.123456789123, 9.123456789123) })]
        };
        var source = AssTextProjection.Create(line).Source.Replace("\\1a&H45&", "\\1a&H80&", StringComparison.Ordinal)
            .Replace("\\yshad9.123456789", "\\yshad10", StringComparison.Ordinal);
        var active = AssTextProjection.Apply(line, source).Line.KaraokeStyleSpans[0].ActiveStyle!;
        Assert.Equal(precise.Red, active.Fill!.Value.Red);
        Assert.Equal(precise.Green, active.Fill.Value.Green);
        Assert.Equal(precise.Blue, active.Fill.Value.Blue);
        Assert.Equal(1 - 128 / 255.0, active.Fill.Value.Alpha);
        Assert.Equal(8.123456789123, active.ShadowOffset!.Value.X);
        Assert.Equal(10, active.ShadowOffset.Value.Y);
    }

    [Fact]
    public void ProjectionPreservesSparseInlineInheritanceWhenEditingOneField()
    {
        var line = new SubtitleLine
        {
            Text = "a", End = new(1), Style = new() { Fill = new(4, -0.3, 0.123456789), ShadowBlur = 0 },
            InlineSpans = [new(0, 1, new() { Bold = true })]
        };
        var projection = AssTextProjection.Create(line);
        var span = Assert.Single(AssTextProjection.Apply(line, projection.Source.Replace("\\bord2", "\\bord3", StringComparison.Ordinal)).Line.InlineSpans);
        Assert.True(span.Style.Bold);
        Assert.Equal(3, span.Style.StrokeWidth);
        Assert.Null(span.Style.Fill);
        Assert.Null(span.Style.FontFamily);
        Assert.Null(span.Style.ShadowColor);
    }

    [Fact]
    public void ProjectionRestoredOrdinaryPrecisionDoesNotBecomeKaraokeOverrides()
    {
        var line = Line(KaraokeHighlightKind.STEP);
        line = line with
        {
            Style = line.Style with { StrokeWidth = 2.123456789123, ShadowOffset = new(3.123456789123, 4.123456789123) },
            Karaoke = [line.Karaoke[0]],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 6 })]
        };
        var projection = AssTextProjection.Create(line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal)).Line;
        Assert.Equal(line.KaraokeStyleSpans[0].ActiveStyle, edited.KaraokeStyleSpans[0].ActiveStyle);
        Assert.Null(edited.KaraokeStyleSpans[0].InactiveStyle);
    }

    private static SubtitleLine Line(KaraokeHighlightKind kind)
    {
        return new()
        {
            Text = "ab", End = new(2), Style = new() { Fill = new(0, 0, 1), ShadowBlur = 0 },
            Karaoke =
            [
                new(0, 1, new(1, 4), new(3, 4), SceneColor.White)
                {
                    HighlightKind = kind
                },
                new(1, 1, new(1), new(3, 2), SceneColor.White) { HighlightKind = kind }
            ],
            KaraokeStyleSpans = [new(0, 1, new() { Stroke = new(0, 1, 0), StrokeWidth = 6, ShadowOffset = new(8, 9), ShadowColor = new(1, 0, 0) })]
        };
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return SubtitleFormatTestDocument.Create(line);
    }
}
