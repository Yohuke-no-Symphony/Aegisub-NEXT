using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssKaraokeRangeStyleTests
{
    [Theory]
    [InlineData("k", KaraokeHighlightKind.STEP)]
    [InlineData("ko", KaraokeHighlightKind.OUTLINE_STEP)]
    [InlineData("kf", KaraokeHighlightKind.SWEEP)]
    public void ImportKeepsEveryRangeColorAndAlphaInsideOneCompleteGroup(string tag, KaraokeHighlightKind kind)
    {
        var result = AssSubtitleFormat.Parse(Source("{\\kt25\\" + tag + "100\\1c&H0000FF&\\1a&H80&\\2c&HFF0000&\\2a&H00&}a" +
            "{\\1c&H00FF00&\\1a&H00&\\2c&HFFFFFF&\\2a&H80&}😀"));
        var line = Assert.Single(result.Lines);
        var group = Assert.Single(line.Karaoke);

        Assert.Equal("a😀", line.Text);
        Assert.Equal((0, 3), (group.Utf16Start, group.Utf16Length));
        Assert.Equal(new MediaTime(1, 4), group.Start);
        Assert.Equal(new MediaTime(5, 4), group.End);
        Assert.Equal(kind, group.HighlightKind);
        Assert.Equal(2, line.KaraokeStyleSpans.Length);
        Assert.Equal((0, 1), (line.KaraokeStyleSpans[0].Utf16Start, line.KaraokeStyleSpans[0].Utf16Length));
        Assert.Equal((1, 2), (line.KaraokeStyleSpans[1].Utf16Start, line.KaraokeStyleSpans[1].Utf16Length));
        Assert.Equal(new SceneColor(1, 0, 0, 127d / 255), line.KaraokeStyleSpans[0].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(0, 0, 1), line.KaraokeStyleSpans[0].InactiveStyle!.Fill);
        Assert.Equal(new SceneColor(0, 1, 0), line.KaraokeStyleSpans[1].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(1, 1, 1, 127d / 255), line.KaraokeStyleSpans[1].InactiveStyle!.Fill);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns" && diagnostic.SourceLength > 0);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeActiveRuns");
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    public void ExportKeepsInlineAndHighlightRangeTagsWithoutSplittingGroupTime(KaraokeHighlightKind kind)
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2), Style = new() { Fill = SceneColor.White, ShadowBlur = 0 },
            InlineSpans = [new(1, 1, new() { Bold = true })],
            Karaoke = [new(0, 2, new(1, 4), new(5, 4), SceneColor.White) { HighlightKind = kind }],
            KaraokeStyleSpans =
            [
                new(0, 1, new() { Fill = new(1, 0, 0) }, new() { Fill = new(0, 0, 1) }),
                new(1, 1, new() { Fill = new(0, 1, 0), StrokeWidth = 6 }, new() { Fill = SceneColor.White })
            ]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        var group = Assert.Single(imported.Karaoke);

        Assert.Equal((0, 2), (group.Utf16Start, group.Utf16Length));
        Assert.Equal(line.Karaoke[0].Start, group.Start);
        Assert.Equal(line.Karaoke[0].End, group.End);
        Assert.Equal(kind, group.HighlightKind);
        Assert.Equal(new SceneColor(1, 0, 0), imported.KaraokeStyleSpans[0].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(0, 1, 0), imported.KaraokeStyleSpans[1].ActiveStyle!.Fill);
        Assert.Contains(imported.InlineSpans, span => span.Utf16Start == 1 && span.Style.Bold == true);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
        Assert.Equal(kind == KaraokeHighlightKind.SWEEP, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.KaraokeVisual"));
        if (kind != KaraokeHighlightKind.SWEEP)
        {
            Assert.Equal(6, imported.KaraokeStyleSpans[1].ActiveStyle!.StrokeWidth);
        }
        Assert.Single(line.Karaoke);
        Assert.Equal(2, line.Karaoke[0].Utf16Length);
    }

    [Fact]
    public void UniformStylesInsideMultiCharacterGroupDoNotReportStyleRunLoss()
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\kf100}a😀"));
        var line = Assert.Single(imported.Lines);
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Single(line.Karaoke);
        Assert.Single(line.KaraokeStyleSpans);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
    }

    [Fact]
    public void ProjectionStyleEditPreservesSingleGroupIdentityAndPreciseTime()
    {
        var line = new SubtitleLine
        {
            Text = "ab", Style = new() { ShadowBlur = 0 },
            Karaoke = [new(0, 2, new(1, 7), new(6, 7), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP }],
            KaraokeStyleSpans = [new(1, 1, new() { StrokeWidth = 6 })]
        };
        var source = AssTextProjection.Create(line).Source.Replace("\\bord6)", "\\bord7)", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source).Line;

        Assert.Equal(line.Karaoke[0], Assert.Single(edited.Karaoke));
        Assert.Equal(7, Assert.Single(edited.KaraokeStyleSpans).ActiveStyle!.StrokeWidth);
        Assert.Empty(edited.KaraokeStyleSpans.Where(span => span.Utf16Start == 0));
    }

    private static string Source(string text)
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nLayoutResX: 1920\nLayoutResY: 1080\nWrapStyle: 1\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,," + text;
    }
}
