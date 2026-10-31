using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class LrcSubtitleFormatTests
{
    private static MediaTime Ms(long value) => new(value, 1000);

    [Fact]
    public void StandardLrcSupportsTimeVariantsUnsortedRepeatedTagsAndEndMarkers()
    {
        var text = "\uFEFF[ti:Song]\r\n[ar:Artist]\r\n[00:05.5]third\r\n[00:01.00][00:03.000]repeat\r\n[00:02]second\r\n[00:07.25]\r\n[00:08.00]last\r\n";
        var result = LrcSubtitleFormat.Parse(text);
        Assert.Equal(["repeat", "second", "repeat", "third", "last"], result.Lines.Select(line => line.Text));
        Assert.Equal([Ms(1000), Ms(2000), Ms(3000), Ms(5500), Ms(8000)], result.Lines.Select(line => line.Start));
        Assert.Equal([Ms(2000), Ms(3000), Ms(5500), Ms(7250), Ms(13000)], result.Lines.Select(line => line.End));
        Assert.Equal("Song", result.Metadata["ti"]);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Lrc.Metadata");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Lrc.LastLineDuration");
    }

    [Theory]
    [InlineData("+500", 500)]
    [InlineData("-250", 1250)]
    public void PositiveOffsetShowsLyricsEarlier(string offset, long expectedMilliseconds)
    {
        var result = LrcSubtitleFormat.Parse($"[offset:{offset}]\n[00:01.00]a\n[00:04.00]\n");
        var line = Assert.Single(result.Lines);
        Assert.Equal(Ms(expectedMilliseconds), line.Start);
    }

    [Fact]
    public void LengthTagEndsTheLastLine()
    {
        var line = Assert.Single(LrcSubtitleFormat.Parse("[length:00:20]\n[00:10.00]a").Lines);
        Assert.Equal(Ms(20000), line.End);
    }

    [Fact]
    public void EnhancedWordTimesBecomeKaraokeSegmentsRelativeToLineStart()
    {
        var result = LrcSubtitleFormat.Parse("[00:01.00]<00:01.00>Hello <00:01.50>world<00:02.20>\n[00:03.00]next");
        var line = result.Lines[0];
        Assert.Equal("Hello world", line.Text);
        Assert.Equal(2, line.Karaoke.Length);
        Assert.Equal((0, 6, MediaTime.Zero, Ms(500)), (line.Karaoke[0].Utf16Start, line.Karaoke[0].Utf16Length, line.Karaoke[0].Start, line.Karaoke[0].End));
        Assert.Equal((6, 5, Ms(500), Ms(1200)), (line.Karaoke[1].Utf16Start, line.Karaoke[1].Utf16Length, line.Karaoke[1].Start, line.Karaoke[1].End));
        Assert.All(line.Karaoke, segment => Assert.Equal(KaraokeHighlightKind.SWEEP, segment.HighlightKind));
        ProjectValidator.ValidateSubtitleKaraoke(line);
    }

    [Fact]
    public void RepeatedTagsShiftWordTimesAndOutOfRangeWordsAreClamped()
    {
        var result = LrcSubtitleFormat.Parse("[00:01.00][00:10.00]<00:01.00>a<00:01.50>b<00:09.00>\n[00:02.00]x\n[00:11.00]\n");
        var second = result.Lines.Single(line => line.Start == Ms(10000));
        Assert.Equal(Ms(500), second.Karaoke[1].Start);
        var first = result.Lines.Single(line => line.Start == Ms(1000));
        Assert.Equal(Ms(1000), first.Karaoke[^1].End);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Lrc.KaraokeClamped");
    }

    [Fact]
    public void StandardExportWritesGapEndMarkersAndJoinsLineBreaks()
    {
        SubtitleLine[] lines =
        [
            new() { Start = Ms(1000), End = Ms(2000), Text = "a\nb" },
            new() { Start = Ms(2000), End = Ms(3000), Text = "c" },
            new() { Start = Ms(4000), End = Ms(5005), Text = "d" }
        ];
        var diagnostics = System.Collections.Immutable.ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var text = LrcSubtitleFormat.Write(lines, MediaTime.Zero, false, diagnostics);
        Assert.Equal("[00:01.00]a b\n[00:02.00]c\n[00:03.00]\n[00:04.00]d\n[00:05.00]\n", text);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "Lrc.LineBreaks");
        var again = LrcSubtitleFormat.Parse(text).Lines;
        Assert.Equal([Ms(2000), Ms(3000), Ms(5000)], again.Select(line => line.End));
    }

    [Fact]
    public void OverlapsAreReportedAndTruncatedAtTheNextLine()
    {
        SubtitleLine[] lines =
        [
            new() { Start = Ms(0), End = Ms(3000), Text = "a" },
            new() { Start = Ms(1000), End = Ms(2000), Text = "b" }
        ];
        var diagnostics = System.Collections.Immutable.ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var text = LrcSubtitleFormat.Write(lines, MediaTime.Zero, false, diagnostics);
        Assert.Equal("[00:00.00]a\n[00:01.00]b\n[00:02.00]\n", text);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "Lrc.Overlap");
    }

    [Fact]
    public void EnhancedExportRoundTripsKaraokeIncludingGaps()
    {
        var source = "[00:01.00]<00:01.00>Hel<00:01.30>lo <00:01.80><00:02.00>world<00:02.50>\n[00:03.00]\n";
        var imported = LrcSubtitleFormat.Parse(source).Lines;
        var text = LrcSubtitleFormat.Write(imported, MediaTime.Zero, true);
        Assert.Equal("[00:01.00]<00:01.00>Hel<00:01.30>lo <00:01.80><00:02.00>world<00:02.50>\n[00:03.00]\n", text);
        var again = LrcSubtitleFormat.Parse(text).Lines;
        Assert.Equal(imported[0].Karaoke.Select(segment => (segment.Utf16Start, segment.Utf16Length, segment.Start, segment.End)),
            again[0].Karaoke.Select(segment => (segment.Utf16Start, segment.Utf16Length, segment.Start, segment.End)));
    }

    [Fact]
    public void ExportAppliesOffsetAndRejectsNegativeTimes()
    {
        SubtitleLine[] lines = [new() { Start = Ms(1000), End = Ms(2000), Text = "a" }];
        Assert.StartsWith("[01:01.00]a", LrcSubtitleFormat.Write(lines, Ms(60000), false));
        Assert.Throws<InvalidDataException>(() => LrcSubtitleFormat.Write(lines, Ms(-2000), false));
    }

    [Fact]
    public void LossAnalysisReportsKaraokeOnlyForStandardLrc()
    {
        var line = LrcSubtitleFormat.Parse("[00:01.00]<00:01.00>a<00:01.50>b\n[00:02.00]\n").Lines[0];
        var document = new ProjectDocument { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        Assert.Contains(SubtitleFormatLossAnalysis.ForLrc(document, false), diagnostic => diagnostic.Code == "Lrc.Karaoke");
        Assert.DoesNotContain(SubtitleFormatLossAnalysis.ForLrc(document, true), diagnostic => diagnostic.Code == "Lrc.Karaoke");
    }

    [Fact]
    public void UnrecognizedLinesAreReported()
    {
        var result = LrcSubtitleFormat.Parse("garbage\n[00:01.00]a");
        Assert.Single(result.Lines);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Lrc.UnrecognizedLines");
    }
}
