using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssTypographyImportTests
{
    [Fact]
    public void StyleAndInlineSpacingKeepSignedValuesResetStylesAndApplyFontScaleOnlyOnce()
    {
        var parsed = AssSubtitleFormat.Parse(File("a{\\fsp-1.25}b{\\rAlternate\\fsp9\\fsp}c{\\r}d", fontScaleX: "200"), 1280, 720);
        var line = Assert.Single(parsed.Lines);

        Assert.Equal(4, line.Style.LetterSpacing);
        Assert.Equal(4, StyleAt(line, 0).LetterSpacing);
        Assert.Equal(-2.5, StyleAt(line, 1).LetterSpacing);
        Assert.Equal(14, StyleAt(line, 2).LetterSpacing);
        Assert.Equal(4, StyleAt(line, 3).LetterSpacing);
        Assert.Equal(2, Assert.Single(parsed.Clips).Transform.Scale.X);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.UnsupportedTag" or "Ass.StyleGeometry");
    }

    [Fact]
    public void NamedResetBecomesTheBaselineForEmptyFontSizeFamilyAndChannelAlphaTags()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\rAlternate\\fs80\\fs\\fnBogus\\fn\\1a&H00&\\1a}a{\\r}b{\\rAlternate\\fs55\\rMissing}c"), 640, 360);
        var line = Assert.Single(parsed.Lines);
        var first = StyleAt(line, 0);

        Assert.Equal(30, first.FontSize);
        Assert.Equal("Alternate Font", first.FontFamily);
        Assert.Equal(127d / 255, first.Fill.Alpha, 12);
        Assert.Equal(line.Style, StyleAt(line, 1));
        Assert.Equal(line.Style, StyleAt(line, 2));
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnknownStyle");
    }

    [Fact]
    public void EmptyGlobalAlphaAfterNamedResetRestoresEachChannelIndependently()
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File("{\\rAlternate\\alpha&H00&\\alpha\\k100}a"), 640, 360).Lines);
        var style = StyleAt(line, 0);
        var segment = Assert.Single(line.Karaoke);

        Assert.Equal(191d / 255, style.Fill.Alpha, 12);
        Assert.Equal(223d / 255, style.Stroke.Alpha, 12);
        Assert.Equal(159d / 255, style.ShadowColor.Alpha, 12);
        Assert.Equal(127d / 255, Assert.Single(line.KaraokeStyleSpans).ActiveStyle!.Fill!.Value.Alpha, 12);
        Assert.Equal(191d / 255, Assert.Single(line.KaraokeStyleSpans).InactiveStyle!.Fill!.Value.Alpha, 12);
    }

    [Theory]
    [InlineData("\\blur4\\bord0", 4, 0)]
    [InlineData("\\bord0\\blur4", 4, 0)]
    [InlineData("\\blur4\\bord3", 0, 4)]
    [InlineData("\\bord3\\blur4", 0, 4)]
    [InlineData("\\blur4\\bord3\\3a&HFF&", 0, 4)]
    public void ExternalBlurUsesFinalBorderGeometryIndependentlyOfTagOrderAndColorAlpha(string tags,
        double fill, double stroke)
    {
        var parsed = AssSubtitleFormat.Parse(File("{" + tags + "}a"), 640, 360);
        var style = StyleAt(Assert.Single(parsed.Lines), 0);

        Assert.Equal(fill * AssBlurConversion.SigmaPerUnit, style.FillBlur, 12);
        Assert.Equal(stroke * AssBlurConversion.SigmaPerUnit, style.StrokeBlur, 12);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, style.ShadowBlur, 12);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurAppearance");
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.ShadowBlur" or "Ass.BlurLayoutResolution");
    }

    [Fact]
    public void BlurTracksBorderChangesClearsExplicitlyAndResetsAcrossNamedStyles()
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File("{\\blur4}a{\\bord0}b{\\blur0}c{\\blur5\\rAlternate}d{\\blur3\\r}e"), 640, 360).Lines);

        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, StyleAt(line, 0).StrokeBlur, 12);
        Assert.Equal(0, StyleAt(line, 0).FillBlur);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, StyleAt(line, 1).FillBlur, 12);
        Assert.Equal(0, StyleAt(line, 1).StrokeBlur);
        foreach (var position in new[] { 2, 3, 4 })
        {
            var style = StyleAt(line, position);
            Assert.Equal(0, style.FillBlur);
            Assert.Equal(0, style.StrokeBlur);
            Assert.Equal(0, style.ShadowBlur);
        }
    }

    [Theory]
    [InlineData("\\blur4\\bord0", "", 4, 0, 0)]
    [InlineData("\\bord0\\blur4", "", 4, 0, 0)]
    [InlineData("\\blur4\\bord3", "", 0, 4, 3)]
    [InlineData("\\bord3\\blur4", "", 0, 4, 3)]
    [InlineData("\\blur4", "\\bord0", 4, 0, 0)]
    [InlineData("\\blur4\\bord3", "\\blur2", 0, 2, 3)]
    [InlineData("\\bord3", "\\bord0\\blur2", 2, 0, 0)]
    public void InstantKaraokeBlurUsesItsFinalTargetAndLaterStaticOverrides(string animated, string following,
        double fill, double stroke, double border)
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\bord0\\blur1\\kt50\\k100\\t(500,500," + animated + ")" + following + "}a"), 640, 360);
        var line = Assert.Single(parsed.Lines);
        var segment = Assert.Single(line.Karaoke);
        var ordinary = StyleAt(line, 0);
        var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, null, segment,
            KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE));

        Assert.Equal(fill * AssBlurConversion.SigmaPerUnit, active.FillBlur, 12);
        Assert.Equal(stroke * AssBlurConversion.SigmaPerUnit, active.StrokeBlur, 12);
        Assert.Equal(Math.Max(fill, stroke) * AssBlurConversion.SigmaPerUnit, active.ShadowBlur, 12);
        Assert.Equal(border, active.StrokeWidth);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    [Fact]
    public void DifferentInstantBlurTargetsWithinOneKaraokeSegmentReportTheRepresentationalLoss()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\kt50\\k100\\t(500,500,\\blur4)}a{\\bord0}b"), 640, 360);

        Assert.Equal("ab", Assert.Single(parsed.Lines).Text);
        var line = Assert.Single(parsed.Lines);
        Assert.Single(line.Karaoke);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, line.KaraokeStyleSpans[0].ActiveStyle!.StrokeBlur!.Value, 12);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, line.KaraokeStyleSpans[1].ActiveStyle!.FillBlur!.Value, 12);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
    }

    [Fact]
    public void EmptyBlurClearsTheAssEffectAndResetCancelsAnInstantKaraokeTarget()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\blur4\\blur}a{\\kt50\\k100\\t(500,500,\\blur8)\\r}b"), 640, 360);
        var line = Assert.Single(parsed.Lines);

        Assert.Equal(0, StyleAt(line, 0).StrokeBlur);
        var segment = Assert.Single(line.Karaoke);
        var active = KaraokeVisualStyleResolver.ResolveActive(StyleAt(line, 1), null, segment,
            KaraokeVisualStyleResolver.RangeStyleAt(line, 1, KaraokeVisualState.ACTIVE));
        Assert.Equal(0, active.FillBlur);
        Assert.Equal(0, active.StrokeBlur);
        Assert.Equal(0, active.ShadowBlur);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    [Theory]
    [InlineData("\\t(500,500,\\blur8)", AnimationProperty.STROKE_BLUR, true)]
    [InlineData("\\t(0,1000,\\blur8)", AnimationProperty.STROKE_BLUR, false)]
    [InlineData("\\t(500,500,\\fsp8)", AnimationProperty.LETTER_SPACING, true)]
    public void WholeLineTypographyTransformsKeepTextAndCreateEditableNativeAnimation(string tags, AnimationProperty property, bool ordered)
    {
        var parsed = AssSubtitleFormat.Parse(File("{" + tags + "}a"), 640, 360);
        var line = Assert.Single(parsed.Lines);
        var style = StyleAt(line, 0);

        Assert.Equal("a", line.Text);
        Assert.Equal(0, style.FillBlur);
        Assert.Equal(0, style.StrokeBlur);
        Assert.Equal(0, style.ShadowBlur);
        var clip = Assert.Single(parsed.Clips);
        var track = Assert.Single(clip.Tracks, value => value.Property == property);
        Assert.Equal(ordered, track.IsOrdered);
        Assert.Equal(property == AnimationProperty.STROKE_BLUR ? 8 * AssBlurConversion.SigmaPerUnit : 8,
            SceneEvaluator.EvaluateScalarTrack(track, clip.ContentOffset + new MediaTime(1)), 10);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        if (property == AnimationProperty.STROKE_BLUR)
        {
            Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurAppearance");
            var shadow = Assert.Single(clip.Tracks, value => value.Property == AnimationProperty.SHADOW_BLUR);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, new(1)), SceneEvaluator.EvaluateScalarTrack(shadow, new(1)));
        }
        else
        {
            Assert.Equal("Ass.ShadowComposition", Assert.Single(parsed.Diagnostics).Code);
        }
    }

    [Fact]
    public void LayoutResolutionControlsBlurAndLayerScalingIsCompensatedSeparatelyFromSpacing()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\blur4}a", fontScaleX: "200", fontScaleY: "200"), 1280, 720);
        var line = Assert.Single(parsed.Lines);
        var style = StyleAt(line, 0);

        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, style.StrokeBlur, 12);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, style.ShadowBlur, 12);
        Assert.Equal(4, style.LetterSpacing);
        Assert.Equal(new ScenePoint(2, 2), Assert.Single(parsed.Clips).Transform.Scale);
    }

    [Fact]
    public void DifferentLayoutAxesUseAnExplicitlyDiagnosedCircularApproximation()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\blur4\\bord0}a", headers: "WrapStyle: 1\nLayoutResX: 1280\nLayoutResY: 360"), 640, 360);
        var style = StyleAt(Assert.Single(parsed.Lines), 0);

        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit * Math.Sqrt(0.5), style.FillBlur, 12);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurResampling");
    }

    [Theory]
    [InlineData("WrapStyle: 1")]
    [InlineData("WrapStyle: 1\nLayoutResX: 640")]
    [InlineData("WrapStyle: 1\nLayoutResX: 640\nLayoutResY: 0")]
    public void MissingOrIncompleteLayoutResolutionOnlyWarnsWhenVisibleBlurUsesTheFallback(string headers)
    {
        var blurred = AssSubtitleFormat.Parse(File("{\\blur4}a", headers: headers), 1280, 720);
        Assert.Equal(8 * AssBlurConversion.SigmaPerUnit, StyleAt(Assert.Single(blurred.Lines), 0).StrokeBlur, 12);
        Assert.Single(blurred.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.BlurLayoutResolution"));
        var clear = AssSubtitleFormat.Parse(File("{\\blur4\\blur0}a", headers: headers), 1280, 720);
        Assert.DoesNotContain(clear.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurLayoutResolution");
    }

    [Theory]
    [InlineData("\\fsp5000", "Ass.LetterSpacingRange", 2)]
    [InlineData("\\fsp-5000", "Ass.LetterSpacingRange", 2)]
    [InlineData("\\blur700", "", 100)]
    [InlineData("\\blur-1", "", 0)]
    public void OutOfRangeTagsOnlyDiscardTheirOwnValueAndKeepOtherFormatting(string tag, string code, double expectedBlur)
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\fsp3\\blur2" + tag + "\\i1}a"), 640, 360);
        var style = StyleAt(Assert.Single(parsed.Lines), 0);

        Assert.Equal(3, style.LetterSpacing);
        Assert.Equal(expectedBlur * AssBlurConversion.SigmaPerUnit, style.StrokeBlur, 12);
        Assert.True(style.Italic);
        if (code.Length > 0)
        {
            Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == code);
        }
        else
        {
            Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        }
    }

    [Fact]
    public void BlurRangeIsCheckedAfterTheWholeLineScaleHasBeenCompensated()
    {
        var amount = (1200 / AssBlurConversion.SigmaPerUnit).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var parsed = AssSubtitleFormat.Parse(File("{\\blur" + amount + "}a", fontScaleX: "400", fontScaleY: "400"), 640, 360);
        var style = StyleAt(Assert.Single(parsed.Lines), 0);

        Assert.Equal(100 * AssBlurConversion.SigmaPerUnit / 4, style.StrokeBlur, 10);
        Assert.Equal(100 * AssBlurConversion.SigmaPerUnit / 4, style.ShadowBlur, 10);
        Assert.Equal(0, style.FillBlur);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
    }

    [Fact]
    public void OutOfRangeStyleSpacingKeepsTheStyleAndUsesTheNativeDefaultForThatField()
    {
        var parsed = AssSubtitleFormat.Parse(File("a", spacing: "5000"), 640, 360);
        var style = Assert.Single(parsed.Lines).Style;

        Assert.Equal(0, style.LetterSpacing);
        Assert.Equal("Noto Sans", style.FontFamily);
        Assert.Equal(20, style.FontSize);
        Assert.Equal(2, style.StrokeWidth);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.LetterSpacingRange");
    }

    [Theory]
    [InlineData(0, SubtitleWrapMode.NATURAL, "a b\nc", true)]
    [InlineData(1, SubtitleWrapMode.NATURAL, "a b\nc", false)]
    [InlineData(2, SubtitleWrapMode.NO_WRAP, "a\nb\nc", false)]
    [InlineData(3, SubtitleWrapMode.NATURAL, "a b\nc", true)]
    public void HeaderWrapStyleAndInlineQHaveTheSameWrappingAndSoftBreakSemantics(int wrapping,
        SubtitleWrapMode expected, string text, bool approximation)
    {
        foreach (var source in new[]
                 {
                     File("a\\nb\\Nc", headers: "WrapStyle: " + wrapping),
                     File("{\\q" + wrapping + "}a\\nb\\Nc")
                 })
        {
            var parsed = AssSubtitleFormat.Parse(source, 640, 360);
            var line = Assert.Single(parsed.Lines);
            Assert.Equal(text, line.Text);
            Assert.Equal(expected, line.Style.WrapMode);
            Assert.Equal(approximation, parsed.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.WrapModeApproximation"));
        }
    }

    [Fact]
    public void MixedQUsesItsFinalLineModeButEachSoftBreakUsesTheModeAtThatPositionAndResetDoesNotChangeIt()
    {
        var parsed = AssSubtitleFormat.Parse(File("a{\\q2}\\nb{\\rAlternate}\\nc{\\q1}\\nd{\\q2}"), 640, 360);
        var line = Assert.Single(parsed.Lines);

        Assert.Equal("a\nb\nc d", line.Text);
        Assert.Equal(SubtitleWrapMode.NO_WRAP, line.Style.WrapMode);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.UnsupportedTag" or "Ass.WrapModeApproximation");
    }

    [Fact]
    public void EmptyAndInvalidQRestoreTheHeaderAndMissingHeaderUsesSmartWrapping()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\q2\\q}a\\nb{\\q2\\q99}\\nc"), 640, 360);
        Assert.Equal("a b c", Assert.Single(parsed.Lines).Text);
        Assert.Equal(SubtitleWrapMode.NATURAL, parsed.Lines[0].Style.WrapMode);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.WrapStyle");
        var missing = AssSubtitleFormat.Parse(File("a", headers: string.Empty), 640, 360);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "Ass.WrapModeApproximation");
    }

    [Fact]
    public void ProjectSourceKeepsNativeShadowBlurAndWrapModeWhileAllowingLetterSpacing()
    {
        var original = new SubtitleLine { Style = new() { FillBlur = 3, StrokeBlur = 5, ShadowBlur = 7, WrapMode = SubtitleWrapMode.GRAPHEME } };
        var parsed = new AssTextParser(original, new Dictionary<string, AssStyleDefinition>(), SceneColor.White,
            projectSource: true).Parse("{\\blur4\\fsp-2\\q2}a\\nb");
        var style = StyleAt(parsed.Line, 0);

        Assert.Equal(3, style.FillBlur);
        Assert.Equal(5, style.StrokeBlur);
        Assert.Equal(4, style.ShadowBlur);
        Assert.Equal(-2, style.LetterSpacing);
        Assert.Equal(SubtitleWrapMode.GRAPHEME, parsed.Line.Style.WrapMode);
        Assert.Equal("a b", parsed.Line.Text);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;
    }

    private static string File(string body, string spacing = "2", string fontScaleX = "100", string fontScaleY = "100",
        string headers = "WrapStyle: 1\nLayoutResX: 640\nLayoutResY: 360") => $$"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        {{headers}}
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Spacing, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H00FFFFFF,&H400000FF,&H20000000,&H60000000,{{fontScaleX}},{{fontScaleY}},{{spacing}},2,2,2
        Style: Alternate,Alternate Font,30,&H80FFFFFF,&H400000FF,&H20000000,&H60000000,{{fontScaleX}},{{fontScaleY}},7,0,2,2
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:10.00,0:00:12.00,Default,{{body}}
        """;
}
