using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssConversionFidelityTests
{
    [Theory]
    [InlineData("be1")]
    [InlineData("fax0.25")]
    [InlineData("fay-0.25")]
    [InlineData("fe128")]
    public void UnsupportedLegalTagKeepsFollowingFormattingAndText(string tag)
    {
        var body = "{\\" + tag + "\\fs48}保留 text";
        var imported = AssSubtitleFormat.Parse(Source(body));
        var line = Assert.Single(imported.Lines);
        var diagnostic = Assert.Single(imported.Diagnostics.Where(item => item.Code == "Ass.UnsupportedTag"));

        Assert.Equal("\\" + tag, body.Substring(diagnostic.SourceStart, diagnostic.SourceLength));
        Assert.Equal("保留 text", line.Text);
        var style = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);
        Assert.Equal(48, style.FontSize);
        Assert.False(style.Bold);
        Assert.False(style.Italic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ExternalEdgeBlurUsesNativeStrokeAndShadowSigma(int blur)
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\blur" + blur + "\\fs48}text"));
        var line = Assert.Single(imported.Lines);
        var style = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);

        Assert.Equal(0, line.Style.ShadowBlur);
        Assert.Equal(0, style.FillBlur);
        Assert.Equal(blur * 2 / Math.Sqrt(Math.Log(256)), style.StrokeBlur, 10);
        Assert.Equal(style.StrokeBlur, style.ShadowBlur);
        Assert.Equal(48, style.FontSize);
        if (blur == 0)
        {
            Assert.Equal("Ass.ShadowComposition", Assert.Single(imported.Diagnostics).Code);
        }
        else
        {
            Assert.Equal<string>(["Ass.BlurAppearance", "Ass.ShadowComposition"],
                imported.Diagnostics.Select(item => item.Code));
        }
        Assert.Equal("text", line.Text);
    }

    [Fact]
    public void UndeclaredAssStyleDoesNotInheritNativeDefaultShadowBlur()
    {
        var source = "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Missing,,0,0,0,,text";
        var imported = AssSubtitleFormat.Parse(source);
        var line = Assert.Single(imported.Lines);

        Assert.Equal("Missing", line.StyleName);
        Assert.Equal("text", line.Text);
        Assert.Equal(0, line.Style.ShadowBlur);
        Assert.Contains(imported.Diagnostics, item => item.Code == "Ass.UnknownStyle");
        Assert.Equal(2, new SubtitleStyle().ShadowBlur);
    }

    [Fact]
    public void ExternalEdgeBlurEntersKaraokeStrokeAndShadowStylesWithoutChangingFill()
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\k50}a{\\blur4\\k50}b"));
        var line = Assert.Single(imported.Lines);

        Assert.Equal("ab", line.Text);
        Assert.Equal(2, line.Karaoke.Length);
        Assert.Equal<string>(["Ass.BlurAppearance", "Ass.ShadowComposition"],
            imported.Diagnostics.Select(item => item.Code));
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            var segment = line.Karaoke[index];
            var body = line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= segment.Utf16Start &&
                span.Utf16Start + span.Utf16Length > segment.Utf16Start)?.Style.ApplyTo(line.Style) ?? line.Style;
            var expected = index == 0 ? 0 : 4 * 2 / Math.Sqrt(Math.Log(256));
            foreach (var style in new[] { body, KaraokeVisualStyleResolver.ResolveInactive(body, segment,
                KaraokeVisualStyleResolver.RangeStyleAt(line, segment.Utf16Start, KaraokeVisualState.INACTIVE)),
                KaraokeVisualStyleResolver.ResolveActive(body, line.KaraokeStyle, segment,
                    KaraokeVisualStyleResolver.RangeStyleAt(line, segment.Utf16Start, KaraokeVisualState.ACTIVE)) })
            {
                Assert.Equal(0, style.FillBlur);
                Assert.Equal(expected, style.StrokeBlur, 10);
                Assert.Equal(expected, style.ShadowBlur, 10);
            }
        }
    }

    [Fact]
    public void BlurInsideInstantKaraokeTransformKeepsStrokeWidthAndConvertedSigma()
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\k50}a{\\t(500,500,\\blur8\\bord6)\\k50}b"));
        var line = Assert.Single(imported.Lines);
        var segment = line.Karaoke[^1];
        var active = KaraokeVisualStyleResolver.ResolveActive(line.Style, line.KaraokeStyle, segment,
            KaraokeVisualStyleResolver.RangeStyleAt(line, segment.Utf16Start, KaraokeVisualState.ACTIVE));

        Assert.Equal("ab", line.Text);
        Assert.Equal(new MediaTime(1, 2), segment.Start);
        Assert.Equal(6, active.StrokeWidth);
        Assert.Equal(0, active.FillBlur);
        Assert.Equal(8 * 2 / Math.Sqrt(Math.Log(256)), active.StrokeBlur, 10);
        Assert.Equal(active.StrokeBlur, active.ShadowBlur);
        Assert.Equal<string>(["Ass.BlurAppearance", "Ass.ShadowComposition"],
            imported.Diagnostics.Select(item => item.Code));
    }

    [Fact]
    public void InstantBlurTransformWithoutKaraokeCreatesAnEditableTrackAndKeepsFollowingFontSize()
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\t(500,500,\\blur8)\\fs48}text"));
        var line = Assert.Single(imported.Lines);
        var style = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);

        Assert.Equal("text", line.Text);
        Assert.Equal(48, style.FontSize);
        Assert.Equal(0, style.ShadowBlur);
        Assert.Equal(0, style.FillBlur);
        Assert.Equal(0, style.StrokeBlur);
        Assert.Empty(line.Karaoke);
        var clip = Assert.Single(imported.Clips);
        var track = Assert.Single(clip.Tracks, value => value.Property == AnimationProperty.STROKE_BLUR);
        Assert.True(track.IsOrdered);
        Assert.Equal(8 * 2 / Math.Sqrt(Math.Log(256)),
            SceneEvaluator.EvaluateScalarTrack(track, clip.ContentOffset + new MediaTime(1, 2)), 10);
        var shadow = Assert.Single(clip.Tracks, value => value.Property == AnimationProperty.SHADOW_BLUR);
        Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, new(1, 2)), SceneEvaluator.EvaluateScalarTrack(shadow, new(1, 2)));
        Assert.Equal(2, imported.Diagnostics.Length);
        Assert.Contains(imported.Diagnostics, value => value.Code == "Ass.BlurAppearance");
        Assert.Contains(imported.Diagnostics, value => value.Code == "Ass.ShadowComposition");
    }

    [Fact]
    public void ExportOmitsNativeShadowBlurFromOrdinaryAndKaraokeTagsWithoutMutatingSource()
    {
        var line = Line("Lyrics", "abc") with
        {
            Style = new() { ShadowBlur = 3 },
            InlineSpans = [new(1, 1, new() { ShadowBlur = 4 })],
            Karaoke =
            [
                new(0, 1, new(1, 2), new(1), SceneColor.White)
                {
                    HighlightKind = KaraokeHighlightKind.STEP
                }
            ],
            KaraokeStyleSpans = [new(0, 1, new() { ShadowBlur = 7 }, new() { ShadowBlur = 5 })]
        };
        var document = Document(line);
        var written = AssSubtitleFormat.Write(document);

        AssertOnlyZeroExternalBlur(written.Text);
        Assert.Single(written.Diagnostics.Where(item => item.SubtitleId == line.Id && item.Code == "Ass.ShadowBlur"));
        Assert.Same(line, document.Subtitles[0]);
        Assert.Equal(3, line.Style.ShadowBlur);
        Assert.Equal(4, line.InlineSpans[0].Style.ShadowBlur);
        Assert.Equal(5, line.KaraokeStyleSpans[0].InactiveStyle!.ShadowBlur);
        Assert.Equal(7, line.KaraokeStyleSpans[0].ActiveStyle!.ShadowBlur);
        Assert.Equal("abc", Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines).Text);
    }

    [Fact]
    public void NativeAssProjectionStillEditsShadowBlurAndRetainsUntouchedValues()
    {
        var line = Line("Native", "ab") with
        {
            Style = new() { ShadowBlur = 3, FillBlur = 1.25, StrokeBlur = 2.5 },
            InlineSpans = [new(1, 1, new() { ShadowBlur = 4 })]
        };
        var projection = AssTextProjection.Create(line);
        var changedText = AssTextProjection.Apply(line, projection.Source.Replace("}a", "}A", StringComparison.Ordinal));
        var changedBlur = AssTextProjection.Apply(line, projection.Source.Replace("\\blur4", "\\blur6", StringComparison.Ordinal));

        Assert.Equal("Ab", changedText.Line.Text);
        Assert.Empty(changedText.Diagnostics);
        Assert.Equal(line.Style, changedText.Line.Style);
        Assert.True(line.InlineSpans.SequenceEqual(changedText.Line.InlineSpans));
        Assert.Equal(6, Assert.Single(changedBlur.Line.InlineSpans).Style.ShadowBlur);
        var editedStyle = Assert.Single(changedBlur.Line.InlineSpans).Style.ApplyTo(changedBlur.Line.Style);
        Assert.Equal(1.25, editedStyle.FillBlur);
        Assert.Equal(2.5, editedStyle.StrokeBlur);
        Assert.Equal(4, line.InlineSpans[0].Style.ShadowBlur);
    }

    [Fact]
    public void DifferentNamesWithEqualStyleValuesRemainSeparateAssStyles()
    {
        var first = Line("对白 Dialogue", "first");
        var second = Line("歌词 Lyrics", "second", 2);
        var written = AssSubtitleFormat.Write(Document(first, second));
        var imported = AssSubtitleFormat.Parse(written.Text);

        Assert.Equal(new[] { first.StyleName, second.StyleName }, StyleNames(written.Text));
        Assert.Equal(new[] { first.StyleName, second.StyleName }, imported.Lines.Select(line => line.StyleName));
    }

    [Fact]
    public void SameNameAndSameExportedValuesReuseOneStyleDespiteNativeOnlyDifferences()
    {
        var first = Line("Dialogue", "first");
        var second = Line("Dialogue", "second", 2) with
        {
            Style = first.Style with { ShadowBlur = 8, LineHeight = 1.5 }
        };
        var written = AssSubtitleFormat.Write(Document(first, second));

        Assert.Equal("Dialogue", Assert.Single(StyleNames(written.Text)));
        Assert.All(AssSubtitleFormat.Parse(written.Text).Lines, line => Assert.Equal("Dialogue", line.StyleName));
        Assert.Equal(8, second.Style.ShadowBlur);
        Assert.Equal(1.5, second.Style.LineHeight);
    }

    [Fact]
    public void SameNameAndColorsThatQuantizeToTheSameAssValuesReuseOneStyle()
    {
        var first = Line("Dialogue", "first") with
        {
            Style = new() { ShadowBlur = 0, Fill = new(0.18, 0.27, 0.36, 0.731) }
        };
        var second = Line("Dialogue", "second", 2) with
        {
            Style = first.Style with { Fill = new(0.18001, 0.27001, 0.36001, 0.73101) }
        };
        var written = AssSubtitleFormat.Write(Document(first, second));

        Assert.Equal("Dialogue", Assert.Single(StyleNames(written.Text)));
        Assert.All(AssSubtitleFormat.Parse(written.Text).Lines, line => Assert.Equal("Dialogue", line.StyleName));
        Assert.NotEqual(first.Style.Fill, second.Style.Fill);
        Assert.All(new[] { first.Id, second.Id }, id =>
        {
            Assert.Single(written.Diagnostics.Where(item => item.SubtitleId == id && item.Code == "Ass.ColorPrecision"));
            Assert.Single(written.Diagnostics.Where(item => item.SubtitleId == id && item.Code == "Ass.AlphaPrecision"));
        });
    }

    [Fact]
    public void ConflictingStyleNamesUseStableUniqueNamesWithoutTakingExistingIdentities()
    {
        var first = Line("Dialogue", "first");
        var conflicting = Line("Dialogue", "changed", 2) with { Style = first.Style with { FontSize = 96 } };
        var reserved = Line("Dialogue_2", "reserved", 4);
        var document = Document(first, conflicting, reserved);
        var written = AssSubtitleFormat.Write(document);
        var imported = AssSubtitleFormat.Parse(written.Text);
        var changedName = imported.Lines.Single(line => line.Text == "changed").StyleName;

        Assert.Equal(written.Text, AssSubtitleFormat.Write(document).Text);
        Assert.Equal(3, StyleNames(written.Text).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Dialogue", imported.Lines.Single(line => line.Text == "first").StyleName);
        Assert.Equal("Dialogue_2", imported.Lines.Single(line => line.Text == "reserved").StyleName);
        Assert.NotEqual("Dialogue", changedName);
        Assert.NotEqual("Dialogue_2", changedName);
        Assert.Equal("Dialogue", conflicting.StyleName);
    }

    [Theory]
    [InlineData(1019)]
    [InlineData(1020)]
    public void SuffixingMaximumLengthStyleNamesKeepsValidUnicodeAndReadableAss(int prefixLength)
    {
        var name = new string('字', prefixLength) + "😀😀" + new string('尾', 1020 - prefixLength);
        var first = Line(name, "first");
        var conflicting = Line(name, "changed", 2) with { Style = first.Style with { FontSize = 96 } };
        var written = AssSubtitleFormat.Write(Document(first, conflicting));
        var imported = AssSubtitleFormat.Parse(written.Text);

        Assert.Equal(1024, name.Length);
        Assert.Equal(name, imported.Lines.Single(line => line.Text == "first").StyleName);
        Assert.Equal(2, StyleNames(written.Text).Distinct(StringComparer.Ordinal).Count());
        Assert.All(imported.Lines, line =>
        {
            Assert.InRange(line.StyleName.Length, 1, 1024);
            ProjectValidator.ValidateSubtitleStyleName(line.StyleName);
        });
        Assert.Equal(name, conflicting.StyleName);
    }

    [Fact]
    public void CommaInNativeStyleNameIsReplacedOnlyInAssOutputAndReported()
    {
        var line = Line("对白,主角", "text");
        var document = Document(line);
        var written = AssSubtitleFormat.Write(document);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        var diagnostic = Assert.Single(written.Diagnostics.Where(item => item.Code == "Ass.StyleName"));

        Assert.Equal(line.Id, diagnostic.SubtitleId);
        Assert.DoesNotContain(',', imported.StyleName);
        Assert.Equal(imported.StyleName, Assert.Single(StyleNames(written.Text)));
        Assert.Equal("对白,主角", line.StyleName);
        Assert.Equal("text", imported.Text);
    }

    [Fact]
    public void SanitizingAnEarlierStyleNameDoesNotTakeALaterValidStyleIdentity()
    {
        var invalid = Line("Dialogue,Main", "sanitized");
        var valid = Line("Dialogue_Main", "preserved", 2);
        var written = AssSubtitleFormat.Write(Document(invalid, valid));
        var imported = AssSubtitleFormat.Parse(written.Text);
        var sanitized = imported.Lines.Single(line => line.Text == "sanitized");
        var preserved = imported.Lines.Single(line => line.Text == "preserved");

        Assert.Equal(valid.StyleName, preserved.StyleName);
        Assert.NotEqual(preserved.StyleName, sanitized.StyleName);
        Assert.DoesNotContain(',', sanitized.StyleName);
        Assert.Equal(2, StyleNames(written.Text).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(written.Diagnostics.Where(item => item.SubtitleId == invalid.Id && item.Code == "Ass.StyleName"));
        Assert.DoesNotContain(written.Diagnostics, item => item.SubtitleId == valid.Id && item.Code == "Ass.StyleName");
        Assert.Equal("Dialogue,Main", invalid.StyleName);
        Assert.Equal("Dialogue_Main", valid.StyleName);
    }

    [Fact]
    public void PrecisionLossesAreReportedOncePerSubtitleAcrossInlineAndKaraokeStyles()
    {
        var color = new SceneColor(0.18, 0.27, 0.36, 0.731);
        var line = Line("Precise", "ab") with
        {
            Start = new(1, 300),
            End = new(601, 300),
            Style = new()
            {
                FontSize = 64 + 1.0 / 3,
                Fill = color,
                Stroke = color,
                ShadowColor = color,
                ShadowBlur = 0
            },
            InlineSpans = [new(1, 1, new() { FontSize = 72 + 1.0 / 7, Fill = new(0.21, 0.32, 0.43, 0.613) })],
            Karaoke =
            [
                new(0, 1, new(1, 7), new(2, 7), color),
                new(1, 1, new(2, 7), new(3, 7), color)
            ]
        };
        var document = Document(line);
        var written = AssSubtitleFormat.Write(document);

        foreach (var code in new[] { "Ass.ColorPrecision", "Ass.AlphaPrecision", "Ass.NumberPrecision", "Ass.TimeQuantization", "Ass.KaraokeQuantization" })
        {
            Assert.Single(written.Diagnostics.Where(item => item.SubtitleId == line.Id && item.Code == code));
        }
        Assert.Same(line, document.Subtitles[0]);
        Assert.Equal(color, line.Style.Fill);
        Assert.Equal(new MediaTime(1, 300), line.Start);
        Assert.Equal(new MediaTime(1, 7), line.Karaoke[0].Start);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        Assert.Equal(MediaTime.Zero, imported.Start);
        Assert.Equal(new MediaTime(201, 100), imported.End);
    }

    [Fact]
    public void QuantizedAssColorsNumbersAndTimesDoNotProduceFalsePrecisionWarnings()
    {
        var imported = AssSubtitleFormat.Parse(Source("{\\1c&H765432&\\1a&H7F&\\fs48.125\\k50}a{\\3c&H123456&\\3a&H21&\\k50}b"));
        var line = Assert.Single(imported.Lines);
        var written = AssSubtitleFormat.Write(Document(line));

        Assert.DoesNotContain(written.Diagnostics, item => item.Code is "Ass.ColorPrecision" or "Ass.AlphaPrecision" or
            "Ass.NumberPrecision" or "Ass.TimeQuantization" or "Ass.KaraokeQuantization");
        Assert.Equal("ab", Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines).Text);
    }

    [Fact]
    public void WholeLayerBlurIsReportedWithoutExportingTextBlurOrChangingTheLayer()
    {
        var line = Line("Dialogue", "text");
        var document = Document(line);
        var layer = document.Layers[0] with { Blur = 6.5 };
        document = document with { Layers = [layer] };
        var written = AssSubtitleFormat.Write(document);
        var diagnostic = Assert.Single(written.Diagnostics.Where(item => item.Code == "Ass.LayerBlur"));

        Assert.Equal(line.Id, diagnostic.SubtitleId);
        AssertOnlyZeroExternalBlur(written.Text);
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(6.5, document.Layers[0].Blur);
        Assert.Equal("text", Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines).Text);
    }

    [Fact]
    public void SrtReportsWholeLayerBlurWithoutRequiringOtherCompositionFeatures()
    {
        var line = Line("Dialogue", "text");
        var document = Document(line);
        var layer = document.Layers[0] with { Blur = 6.5 };
        document = document with { Layers = [layer] };
        var diagnostics = SubtitleFormatLossAnalysis.ForSrt(document);
        var blur = Assert.Single(diagnostics.Where(item => item.Code == "Srt.Blur"));

        Assert.Equal(line.Id, blur.SubtitleId);
        Assert.DoesNotContain(diagnostics, item => item.Code is "Subtitle.Composition" or "Srt.Mask" or "Srt.Animation");
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(6.5, layer.Blur);
    }

    private static SubtitleLine Line(string styleName, string text, int start = 0)
    {
        return new()
        {
            StyleName = styleName,
            Text = text,
            Start = new(start),
            End = new(start + 2),
            Style = new() { ShadowBlur = 0, ShadowColor = SceneColor.Black }
        };
    }

    private static ProjectDocument Document(params SubtitleLine[] lines)
    {
        return new()
        {
            Subtitles = [.. lines],
            Layers = [.. lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            })]
        };
    }

    private static string[] StyleNames(string source)
    {
        return source.Split('\n').Where(row => row.StartsWith("Style: ", StringComparison.Ordinal))
            .Select(row => row[7..].Split(',')[0]).ToArray();
    }

    private static void AssertOnlyZeroExternalBlur(string source)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(source, @"\\blur([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)"))
        {
            Assert.Equal(0, double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static string Source(string body)
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nLayoutResX: 1920\nLayoutResY: 1080\nWrapStyle: 1\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Outline, Shadow, Alignment, MarginL, MarginR, MarginV\n" +
            "Style: Default,sans-serif,64,&H00FFFFFF,&H00808080,&H00000000,&H00000000,2,2,2,20,20,20\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,," + body;
    }
}
