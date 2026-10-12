using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class InactiveKaraokeAssProjectionTests
{
    [Fact]
    public void DisabledClipsStayOutOfAssSourceAndUnchangedProjectionRetainsTheOriginalLine()
    {
        var line = Line();
        var ordinary = line with { InactiveKaraoke = [], KaraokeStyle = null };
        var source = AssTextProjection.Create(line);
        Assert.Equal(AssTextProjection.Create(ordinary).Source, source.Source);
        var unchanged = AssTextProjection.Apply(line, source.Source);
        Assert.Same(line, unchanged.Line);
        Assert.Equal(line.InactiveKaraoke, unchanged.Line.InactiveKaraoke);
    }

    [Fact]
    public void AssTextReplacementUpdatesSavedRangesWithoutChangingIdsTimesOrVisuals()
    {
        var line = Line();
        var source = AssTextProjection.Create(line).Source;
        var result = AssTextProjection.Apply(line, source[..^line.Text.Length] + "👩‍💻b");
        Assert.Empty(result.Diagnostics);
        Assert.Equal("👩‍💻b", result.Line.Text);
        Assert.Empty(result.Line.Karaoke);
        Assert.Equal(line.InactiveKaraoke[0] with { Utf16Length = 5 }, result.Line.InactiveKaraoke[0]);
        Assert.Equal(line.InactiveKaraoke[1] with { Utf16Start = 5 }, result.Line.InactiveKaraoke[1]);
        Assert.Equal(line.KaraokeStyle, result.Line.KaraokeStyle);
        ProjectValidator.Validate(Document(result.Line));
    }

    [Fact]
    public void InsertingAssTextAtGroupBoundaryRemapsSavedGroupsWithoutCreatingTiming()
    {
        var line = Line();
        var source = AssTextProjection.Create(line).Source;
        var result = AssTextProjection.Apply(line, source[..^line.Text.Length] + "aXYb");
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Line.Karaoke);
        Assert.Equal(2, result.Line.InactiveKaraoke.Length);
        Assert.Equal(line.InactiveKaraoke[0], result.Line.InactiveKaraoke[0]);
        Assert.Equal(line.InactiveKaraoke[1] with { Utf16Start = 3 }, result.Line.InactiveKaraoke[1]);
        ProjectValidator.Validate(Document(result.Line));
    }

    [Fact]
    public void ExplicitAssKaraokeReplacesOnlySavedClipsInTheSameTextRange()
    {
        var line = Line();
        var source = AssTextProjection.Create(line).Source;
        var result = AssTextProjection.Apply(line, source[..^line.Text.Length] + "{\\k50}a{\\k0}b");
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Line.Karaoke).Utf16Start);
        Assert.Equal(line.InactiveKaraoke[1], Assert.Single(result.Line.InactiveKaraoke));
        ProjectValidator.Validate(Document(result.Line));
    }

    [Fact]
    public void ChangingOnlyAssFormattingKeepsTheCompleteSavedHighlightData()
    {
        var line = Line();
        var source = AssTextProjection.Create(line).Source;
        var result = AssTextProjection.Apply(line, source[..^line.Text.Length] + "{\\b1}ab");
        Assert.Empty(result.Diagnostics);
        Assert.Equal(line.InactiveKaraoke, result.Line.InactiveKaraoke);
        Assert.Empty(result.Line.Karaoke);
        Assert.True(Assert.Single(result.Line.InlineSpans).Style.Bold);
        ProjectValidator.Validate(Document(result.Line));
    }

    [Fact]
    public void ExplicitAssReplacementCanReplaceSavedWaitingGroupsThatNoLongerFitTheText()
    {
        var line = Line();
        line = line with
        {
            InactiveKaraoke = [line.InactiveKaraoke[0] with { Start = MediaTime.Zero, End = new(1) },
                line.InactiveKaraoke[1] with { Start = new(2), End = new(3) }]
        };
        var result = AssTextProjection.Apply(line, "{\\k100}x");
        Assert.Empty(result.Diagnostics);
        Assert.Equal("x", result.Line.Text);
        Assert.Empty(result.Line.InactiveKaraoke);
        Assert.Equal(new MediaTime(1), Assert.Single(result.Line.Karaoke).End);
        ProjectValidator.Validate(Document(result.Line));
    }

    [Fact]
    public void PlainAssReplacementAcrossGroupsRemovesDeletedTimingWithoutInventingNewGroups()
    {
        var line = Line();
        line = line with
        {
            InactiveKaraoke = [line.InactiveKaraoke[0] with { Start = MediaTime.Zero, End = new(1) },
                line.InactiveKaraoke[1] with { Start = new(2), End = new(3) }]
        };
        var edited = AssTextProjection.Apply(line, "x").Line;
        Assert.Equal("x", edited.Text);
        Assert.Empty(edited.Karaoke);
        Assert.Empty(edited.InactiveKaraoke);
        ProjectValidator.Validate(Document(edited));
        Assert.Equal("ab", line.Text);
        Assert.Equal(2, line.InactiveKaraoke.Length);
    }

    private static SubtitleLine Line()
    {
        return new()
        {
            Text = "ab", End = new(4),
            Style = new() { ShadowBlur = 0 },
            InactiveKaraoke =
            [
                new(0, 1, MediaTime.Zero, new(1, 7), new(3.25, 0.125, 0.25, 0.731))
                {
                    HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
                },
                new(1, 1, new(1, 7), new(2, 7), new(2.75, 0.25, 0.5, 0.731))
                {
                    HighlightKind = KaraokeHighlightKind.STEP
                }
            ],
            KaraokeStyleSpans =
            [
                new(0, 1, new() { ShadowOffset = new(-2.25, 4.75) }, new() { StrokeWidth = 1.125 }),
                new(1, 1, new() { StrokeWidth = 3.875 }, new() { Fill = new(0.125, 0.25, 0.5) })
            ],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "saved highlight", new() { Fill = new(3.125, 0.25, 0.5), ShadowBlur = 0 })
        };
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
