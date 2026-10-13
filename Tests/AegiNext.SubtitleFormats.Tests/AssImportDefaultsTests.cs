using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssImportDefaultsTests
{
    [Theory]
    [InlineData("\\fnDifferent\\fn0", "Noto Sans")]
    [InlineData("\\rAlternate\\fnDifferent\\fn0", "Noto Serif")]
    public void ZeroFontNameRestoresTheCurrentResetFamily(string tags, string expected)
    {
        var source = AssBoundarySource.File("{" + tags + "}x")
            .Replace("Style: Alternate,Noto Sans,", "Style: Alternate,Noto Serif,", StringComparison.Ordinal);
        var line = Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Lines);

        Assert.Equal(expected, At(line, 0).FontFamily);
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360 });
        Assert.Equal(expected, At(Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines), 0).FontFamily);
    }

    [Theory]
    [InlineData("b", -1)]
    [InlineData("b", 2)]
    [InlineData("b", 99)]
    [InlineData("i", -1)]
    [InlineData("i", 2)]
    [InlineData("u", -1)]
    [InlineData("u", 2)]
    [InlineData("s", -1)]
    [InlineData("s", 2)]
    public void InvalidEmphasisRestoresOnlyThatFlagFromTheResetStyle(string tag, int value)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File(
            "{\\b1\\i1\\u1\\s1\\" + tag + value + "}x"), 640, 360).Lines);
        var actual = At(line, 0);

        Assert.Equal(tag != "b", actual.Bold);
        Assert.Equal(tag != "i", actual.Italic);
        Assert.Equal(tag != "u", actual.Underline);
        Assert.Equal(tag != "s", actual.Strikethrough);
    }

    [Theory]
    [InlineData("b")]
    [InlineData("i")]
    [InlineData("u")]
    [InlineData("s")]
    public void InvalidEmphasisUsesNamedResetStyleRatherThanTheOriginalStyle(string tag)
    {
        var source = AssBoundarySource.File("{\\rAlternate\\" + tag + "0\\" + tag + "2}x")
            .Replace("&H60FFFFFF,0,0,0,0,", "&H60FFFFFF,-1,-1,-1,-1,", StringComparison.Ordinal);
        var actual = At(Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Lines), 0);

        Assert.True(actual.Bold);
        Assert.True(actual.Italic);
        Assert.True(actual.Underline);
        Assert.True(actual.Strikethrough);
    }

    [Fact]
    public void NegativeStyleFieldsNormalizeBeforeValidationButSignedOverridesRemainLegal()
    {
        var source = AssBoundarySource.File("x{\\fsp-4\\xshad-6\\yshad-8}y")
            .Replace("0,0,0,0,100,100,0,0,1,2,2,2,", "0,0,0,0,100,100,-3,0,1,-2,-4,2,", StringComparison.Ordinal);
        var result = AssSubtitleFormat.Parse(source, 640, 360);
        var line = Assert.Single(result.Lines);

        Assert.Equal(0, At(line, 0).LetterSpacing);
        Assert.Equal(0, At(line, 0).StrokeWidth);
        Assert.Equal(default, At(line, 0).ShadowOffset);
        Assert.Equal(-4, At(line, 1).LetterSpacing);
        Assert.Equal(new ScenePoint(-6, -8), At(line, 1).ShadowOffset);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.LetterSpacingRange");
    }

    [Theory]
    [InlineData(-50, 75, 0, 0.75)]
    [InlineData(150, -50, 1.5, 0)]
    [InlineData(-50, -50, 0, 0)]
    public void NegativeStyleScaleClampsEachAxisToZero(double x, double y, double expectedX, double expectedY)
    {
        var source = AssBoundarySource.File("{\\fscx\\fscy}x")
            .Replace(",100,100,0,0,1,2,2,2,", $",{x},{y},0,0,1,2,2,2,", StringComparison.Ordinal);
        var result = AssSubtitleFormat.Parse(source, 640, 360);

        Assert.Equal(new ScenePoint(expectedX, expectedY), Assert.Single(result.Clips).Transform.Scale);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.UnsupportedTransform");
    }

    [Theory]
    [InlineData("an0", TextAlignment.TOP_LEFT)]
    [InlineData("an10", TextAlignment.TOP_LEFT)]
    [InlineData("a0", TextAlignment.TOP_LEFT)]
    [InlineData("a12", TextAlignment.TOP_LEFT)]
    [InlineData("a4", TextAlignment.TOP_LEFT)]
    [InlineData("a8", TextAlignment.TOP_LEFT)]
    public void ExternalAlignmentFallbackUsesTheResetStyleAndRetainsFirstTagPrecedence(string tag, TextAlignment expected)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\rAlternate\\" + tag + "\\an3}x"), 640, 360);

        Assert.Equal(expected, Assert.Single(result.Lines).Style.Alignment);
        Assert.Single(result.Diagnostics, item => item.Code == "Ass.DuplicatePlacement");
    }

    [Theory]
    [InlineData(1500, 500)]
    [InlineData(500, -500)]
    public void ReversedMoveTimesMatchTheEquivalentOrderedMovement(int start, int end)
    {
        var reversed = AssSubtitleFormat.Parse(AssBoundarySource.File($"{{\\move(10,20,110,220,{start},{end})}}x"), 640, 360);
        var ordered = AssSubtitleFormat.Parse(AssBoundarySource.File($"{{\\move(10,20,110,220,{end},{start})}}x"), 640, 360);
        var actual = Assert.Single(Assert.Single(reversed.Clips).Tracks);
        var expected = Assert.Single(Assert.Single(ordered.Clips).Tracks);

        foreach (var time in new[] { MediaTime.Zero, new(1, 2), new(1), new(3, 2), new(2) })
        {
            Assert.Equal(SceneEvaluator.EvaluateVectorTrack(expected, time), SceneEvaluator.EvaluateVectorTrack(actual, time));
        }
        Assert.DoesNotContain(reversed.Diagnostics, item => item.Code == "Ass.MoveTiming");
    }

    [Theory]
    [InlineData("k", KaraokeHighlightKind.STEP)]
    [InlineData("kf", KaraokeHighlightKind.SWEEP)]
    [InlineData("K", KaraokeHighlightKind.SWEEP)]
    [InlineData("ko", KaraokeHighlightKind.OUTLINE_STEP)]
    public void EmptyKaraokeDurationUsesOneSecondAndEmptyKtResetsItsIndependentClock(string tag, KaraokeHighlightKind kind)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\k25}a{\\kt\\" + tag + "}bc"), 640, 360);
        var line = Assert.Single(result.Lines);

        Assert.Equal(2, line.Karaoke.Length);
        Assert.Equal(MediaTime.Zero, line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(1), line.Karaoke[1].End);
        Assert.Equal(kind, line.Karaoke[1].HighlightKind);
        Assert.Equal(2, line.Karaoke[1].Utf16Length);
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360 });
        var roundTrip = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(line.Karaoke.Select(segment => (segment.Start, segment.End, segment.HighlightKind)),
            roundTrip.Karaoke.Select(segment => (segment.Start, segment.End, segment.HighlightKind)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonpositiveDrawingModePreservesOrdinaryTextWithoutAFalseDrawingLoss(int scale)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File($"{{\\p{scale}}}ordinary"), 640, 360);

        Assert.Equal("ordinary", Assert.Single(result.Lines).Text);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.Drawing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveInlineFontAssetsReportOneResourceLossWithoutChangingTheNativeProject(bool baseAsset)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "fonts/example.ttf");
        var line = new SubtitleLine
        {
            Text = "abc", End = new(2),
            Style = new() { FontAssetId = baseAsset ? asset.Id : null },
            InlineSpans = [new(1, 1, new() { FontFamily = "Embedded Family", FontAssetId = asset.Id })]
        };
        var document = SubtitleFormatTestDocument.Create(line) with { Assets = [asset] };
        var output = AssSubtitleFormat.Write(document);

        Assert.Single(output.Diagnostics, item => item.Code == "Ass.FontResource" && item.SubtitleId == line.Id);
        Assert.Equal(asset.Id, document.Subtitles[0].InlineSpans[0].Style.FontAssetId);
        Assert.Equal(baseAsset ? asset.Id : null, document.Subtitles[0].Style.FontAssetId);
    }

    private static SubtitleStyle At(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)
            ?.Style.ApplyTo(line.Style) ?? line.Style;
    }
}
