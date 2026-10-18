using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssTypographyExportTests
{
    [Fact]
    public void SpacingUsesTheStyleTableAndOnlyWritesActualInlineDifferences()
    {
        var line = Line() with
        {
            Style = Line().Style with { LetterSpacing = 2.5 },
            InlineSpans = [new(1, 1, new() { LetterSpacing = -1.25 })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        var fields = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal)).Split(',');
        Assert.Equal("2.5", fields[13]);
        Assert.DoesNotContain("\\fsp2.5", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains("\\fsp-1.25", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(2.5, imported.Style.LetterSpacing);
        Assert.Equal(-1.25, Assert.Single(imported.InlineSpans, span => span.Utf16Start <= 1 &&
            1 < span.Utf16Start + span.Utf16Length).Style.ApplyTo(imported.Style).LetterSpacing);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Ass.NativeTypography" or "Ass.NumberPrecision");
        Assert.Equal(2.5, line.Style.LetterSpacing);
    }

    [Theory]
    [InlineData(SubtitleWrapMode.GRAPHEME, 1, true)]
    [InlineData(SubtitleWrapMode.NATURAL, 1, false)]
    [InlineData(SubtitleWrapMode.NO_WRAP, 2, false)]
    public void WrapModeIsExplicitAndGraphemeWrappingReportsItsDifferentBreakRules(SubtitleWrapMode mode, int assMode, bool loss)
    {
        var line = Line() with { Style = Line().Style with { WrapMode = mode } };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains("WrapStyle: 1", written.Text, StringComparison.Ordinal);
        if (assMode == 2)
        {
            Assert.Contains("\\q2", Body(written.Text), StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("\\q", Body(written.Text), StringComparison.Ordinal);
        }
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(assMode == 2 ? SubtitleWrapMode.NO_WRAP : SubtitleWrapMode.NATURAL, imported.Style.WrapMode);
        Assert.Equal(loss, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.WrapMode"));
        Assert.DoesNotContain("\\q", AssTextProjection.Create(line).Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 3, 0, 3)]
    [InlineData(2, 0, 4, 4)]
    public void ExternalBlurUsesTheSelectedPaintSigmaAndSharedShadowCanRoundTrip(double strokeWidth, double fill, double stroke, double expected)
    {
        var sigma = AssBlurConversion.SigmaPerUnit;
        var line = Line() with
        {
            Style = Line().Style with
            {
                StrokeWidth = strokeWidth, FillBlur = fill * sigma, StrokeBlur = stroke * sigma,
                ShadowColor = SceneColor.Black, ShadowBlur = expected * sigma
            }
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Equal(expected, Assert.Single(BlurValues(Body(written.Text))));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Ass.FillBlur" or "Ass.StrokeBlur" or "Ass.ShadowBlur");
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        var style = Effective(imported);
        Assert.Equal(fill * sigma, style.FillBlur, 8);
        Assert.Equal(stroke * sigma, style.StrokeBlur, 8);
        Assert.Equal(expected * sigma, style.ShadowBlur, 8);
    }

    [Fact]
    public void TransparentStrokeStillSelectsStrokeBlurAndReportsIndependentFillAndShadowLoss()
    {
        var line = Line() with
        {
            Style = Line().Style with
            {
                StrokeWidth = 2, Stroke = SceneColor.Transparent, FillBlur = 4,
                StrokeBlur = 3 * AssBlurConversion.SigmaPerUnit, ShadowColor = SceneColor.Black, ShadowBlur = 7
            }
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Equal(3, Assert.Single(BlurValues(Body(written.Text))));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FillBlur");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowBlur");
        Assert.Equal(4, line.Style.FillBlur);
        Assert.Equal(7, line.Style.ShadowBlur);
    }

    [Fact]
    public void AbsentStrokeReportsItsUnrepresentableBlurAndVisibleShadowCannotBeBorrowedAsFillBlur()
    {
        var line = Line() with
        {
            Style = Line().Style with { StrokeWidth = 0, StrokeBlur = 4, ShadowColor = SceneColor.Black, ShadowBlur = 7 }
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.DoesNotContain("\\blur", Body(written.Text), StringComparison.Ordinal);
        var imported = Effective(Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines));
        Assert.Equal(0, imported.FillBlur);
        Assert.Equal(0, imported.StrokeBlur);
        Assert.Equal(0, imported.ShadowBlur);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StrokeBlur");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowBlur");
    }

    [Fact]
    public void UniformScaleCompensatesBlurButDoesNotChangeSpacingAndImportReversesTheCompensation()
    {
        var line = Line() with { Style = Line().Style with { LetterSpacing = 3, FillBlur = 2 * AssBlurConversion.SigmaPerUnit } };
        var layer = Layer(line) with { Transform = new() { Scale = new(2, 2) } };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var fields = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal)).Split(',');
        Assert.Equal("3", fields[13]);
        Assert.Equal(4, Assert.Single(BlurValues(Body(written.Text))));
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        var style = Effective(Assert.Single(imported.Lines));
        Assert.Equal(3, style.LetterSpacing);
        Assert.Equal(line.Style.FillBlur, style.FillBlur, 8);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformAppearance");
    }

    [Fact]
    public void AnisotropicBlurUsesGeometricMeanAndReportsTheAppearanceApproximation()
    {
        var line = Line() with { Style = Line().Style with { FillBlur = 2 * AssBlurConversion.SigmaPerUnit } };
        var layer = Layer(line) with { Transform = new() { Scale = new(4, 1) } };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        Assert.Equal(4, Assert.Single(BlurValues(Body(written.Text))));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformAppearance");
    }

    [Fact]
    public void LargeIntermediateAssBlurReportsThePlaybackClampBeforeInverseLayerScale()
    {
        var line = Line() with { Style = Line().Style with { FillBlur = 300 } };
        var layer = Layer(line) with { Transform = new() { Scale = new(4, 4) } };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        Assert.True(Assert.Single(BlurValues(Body(written.Text))) * AssBlurConversion.SigmaPerUnit > 512);
        var parsed = AssSubtitleFormat.Parse(written.Text, 640, 360);

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        Assert.Equal(100 * AssBlurConversion.SigmaPerUnit / 4, Effective(Assert.Single(parsed.Lines)).FillBlur, 8);
        Assert.Equal(300, line.Style.FillBlur);
    }

    [Fact]
    public void BlurUnitConversionReportsPrecisionOfTheActualAssValue()
    {
        var line = Line() with { Style = Line().Style with { FillBlur = 1 } };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Equal(Math.Round(1 / AssBlurConversion.SigmaPerUnit, 9), Assert.Single(BlurValues(Body(written.Text))));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.NumberPrecision");
    }

    [Fact]
    public void KaraokeInstantVisualChangeReevaluatesTheBlurChannelWhenBorderAppears()
    {
        var sigma = AssBlurConversion.SigmaPerUnit;
        var line = Line() with
        {
            Style = Line().Style with { FillBlur = sigma },
            Karaoke = [new(0, 1, new(1, 4), new(3, 4), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.STEP
            }],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 2, FillBlur = 0, StrokeBlur = 3 * sigma })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains("\\t(250,250,\\bord2\\blur3)", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        var active = Assert.Single(imported.KaraokeStyleSpans).ActiveStyle!;
        Assert.Equal(0, active.FillBlur);
        Assert.Equal(3 * sigma, active.StrokeBlur!.Value, 8);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeVisual");
    }

    [Fact]
    public void ConstantTypographyTracksOverrideInlineValuesButKeepKaraokeVisualPrecedence()
    {
        var sigma = AssBlurConversion.SigmaPerUnit;
        var line = Line() with
        {
            InlineSpans = [new(0, 1, new() { LetterSpacing = -20, FillBlur = 8 })],
            Karaoke = [new(0, 1, new(1, 4), new(3, 4), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.STEP
            }],
            KaraokeStyleSpans = [new(0, 1, new() { FillBlur = 3 * sigma })]
        };
        var layer = Layer(line) with
        {
            Tracks =
            [
                new(AnimationProperty.LETTER_SPACING, [new(new(0), 5)]),
                new(AnimationProperty.FILL_BLUR, [new(new(0), sigma)])
            ]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        Assert.Contains("\\fsp5", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\fsp-20", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains("\\t(250,250,\\blur3)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void DynamicTypographyTrackExportsItsLinearTransformAndPreservesTheNativeTrack()
    {
        var line = Line();
        var layer = Layer(line) with { Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(2), 10)])] };
        var document = Document(line, layer);
        var written = AssSubtitleFormat.Write(document);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
        Assert.Contains("\\fsp0\\t(0,2000,1,\\fsp10)", Body(written.Text), StringComparison.Ordinal);
        Assert.Same(layer, document.Layers[0]);
    }

    [Fact]
    public void ConstantSpacingMeasuresAnOverrideOfEveryInlineRunForCustomPivotCompensation()
    {
        var line = Line() with
        {
            Style = Line().Style with { Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.3, 0.5) } },
            InlineSpans = [new(1, 1, new() { LetterSpacing = -20 })]
        };
        var layer = Layer(line) with { Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(0), 8)])] };
        var document = Document(line, layer);
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(320, 180), new(40, 30), new(10, 20), new(100, 20)));
        var written = AssSubtitleFormat.Write(document, placementMeasurer: measurer);

        Assert.Same(document, measurer.Document);
        var measured = Assert.IsType<SubtitleLine>(measurer.Line);
        Assert.Equal(8, measured.Style.LetterSpacing);
        Assert.Equal(8, Assert.Single(measured.InlineSpans).Style.LetterSpacing);
        Assert.Equal(line.Id, measured.Id);
        Assert.Equal(-20, Assert.Single(line.InlineSpans).Style.LetterSpacing);
        Assert.Equal(0, line.Style.LetterSpacing);
        Assert.Contains("\\pos(290,170)", Body(written.Text), StringComparison.Ordinal);
    }

    private static SubtitleLine Line()
    {
        return new()
        {
            Text = "ab", End = new(2),
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                Alignment = TextAlignment.TOP_LEFT, Position = new() { Anchor = new(0, 0), Pivot = new(0, 0), Offset = new(20, 20) },
                StrokeWidth = 0, ShadowBlur = 0, ShadowColor = SceneColor.Transparent, WrapMode = SubtitleWrapMode.NATURAL
            }
        };
    }

    private static ProjectLayer Layer(SubtitleLine line)
    {
        return new() { SubtitleId = line.Id, End = line.End };
    }

    private static ProjectDocument Document(SubtitleLine line, ProjectLayer? layer = null)
    {
        return new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer ?? Layer(line)] };
    }

    private static string Body(string source)
    {
        return Assert.Single(source.Split('\n'), row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).Split(',', 10)[9];
    }

    private static double[] BlurValues(string source)
    {
        return Regex.Matches(source, @"\\blur([-+\d.]+)")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static SubtitleStyle Effective(SubtitleLine line)
    {
        return line.InlineSpans.IsEmpty ? line.Style : line.InlineSpans[0].Style.ApplyTo(line.Style);
    }
}
