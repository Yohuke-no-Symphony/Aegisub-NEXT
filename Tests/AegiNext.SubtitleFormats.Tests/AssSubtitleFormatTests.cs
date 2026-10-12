using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssSubtitleFormatTests
{
    private static readonly string[] expectedOrder = ["earlier", "later"];
    [Fact]
    public void BomUnicodeCommasAndEscapesPreserveTextAndMapScriptResolution()
    {
        var parsed = AssSubtitleFormat.Parse("\uFEFF" + File("{\\an7\\pos(120,80)}中文,😀\\Nsecond\\hword  "), 1280, 720);
        var line = Assert.Single(parsed.Lines);
        Assert.Equal("中文,😀\nsecond\u00a0word  ", line.Text);
        Assert.Equal(new MediaTime(125, 100), line.Start);
        Assert.Equal(40, line.Style.FontSize);
        Assert.Equal(TextAlignment.TOP_LEFT, line.Style.Alignment);
        Assert.Equal(new ScenePoint(240, 160), line.Style.Position!.Offset);
        Assert.Equal(new ScenePoint(0, 0), line.Style.Position.Pivot);
    }

    [Fact]
    public void InlineResetsColorsAlphaAndTypographyRemainStructured()
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File("a{\\b1\\i1\\u1\\s1\\fs30\\c&H0000FF&\\1a&H80&\\bord3\\xshad4\\yshad5}b{\\r}c"), 640, 360).Lines);
        Assert.Equal("abc", line.Text);
        var span = Assert.Single(line.InlineSpans);
        Assert.Equal((1, 1), (span.Utf16Start, span.Utf16Length));
        var style = span.Style.ApplyTo(line.Style);
        Assert.True(style.Bold && style.Italic && style.Underline && style.Strikethrough);
        Assert.Equal(30, style.FontSize);
        Assert.Equal(1, style.Fill.Red);
        Assert.Equal(0, style.Fill.Blue);
        Assert.Equal(1 - 128 / 255.0, style.Fill.Alpha);
        Assert.Equal(new ScenePoint(4, 5), style.ShadowOffset);
    }

    [Fact]
    public void KaraokeModesUseCumulativeBoundariesAndSecondaryInactiveColor()
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File("{\\k10}a{\\kf20}b{\\K30}c{\\ko40}d")).Lines);
        Assert.Equal(new[] { KaraokeHighlightKind.STEP, KaraokeHighlightKind.SWEEP, KaraokeHighlightKind.SWEEP, KaraokeHighlightKind.OUTLINE_STEP }, line.Karaoke.Select(clip => clip.HighlightKind));
        Assert.Equal(new MediaTime(1, 10), line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(1), line.Karaoke[^1].End);
        Assert.Equal(new SceneColor(1, 0, 0), line.KaraokeStyleSpans[0].InactiveStyle!.Fill);
        Assert.Equal(4, line.Karaoke.Select(clip => clip.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("0:60:00.00", "0:00:02.00")]
    [InlineData("0:00:01.250", "0:00:02.00")]
    [InlineData("0:00:03.00", "0:00:02.00")]
    [InlineData("-1:00:00.00", "0:00:02.00")]
    [InlineData("0:0:01.25", "0:00:02.00")]
    [InlineData("0:00:1.25", "0:00:02.00")]
    public void MalformedDialogueTimeRejectsWholeFile(string start, string end)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File("text").Replace("0:00:01.25,0:00:03.50", start + "," + end, StringComparison.Ordinal)));
    }

    [Fact]
    public void UnsupportedEffectsReportLossWhileSupportedTextCanBeImported()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\move(0,0,10,10)\\t(0,100,\\fs40)\\clip(0,0,100,100)}keep{\\p1}m 0 0 l 10 10{\\p0} text"));
        Assert.Equal("keep text", Assert.Single(parsed.Lines).Text);
        Assert.Contains(Assert.Single(parsed.Clips).Tracks, track => track.Property == AnimationProperty.FONT_SIZE);
        Assert.Contains(Assert.Single(parsed.Clips).Tracks, track => track.Property == AnimationProperty.POSITION);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.Drawing");
        Assert.All(parsed.Diagnostics, diagnostic => Assert.NotNull(diagnostic.SubtitleId));
    }

    [Fact]
    public void ExportUsesTrackClipTimeOrderAndDeduplicatesStaticStyles()
    {
        var first = new SubtitleLine { Start = new(3), End = new(4), Text = "later" };
        var second = new SubtitleLine { Start = new(0), End = new(1), Text = "earlier" };
        var document = SubtitleFormatTestDocument.Create(first, second);
        var result = AssSubtitleFormat.Write(document);
        Assert.Equal(1, result.Text.Split('\n').Count(row => row.StartsWith("Style:", StringComparison.Ordinal)));
        Assert.Equal(expectedOrder, AssSubtitleFormat.Parse(result.Text).Lines.Select(line => line.Text));
        var srt = SubtitleTextFormat.WriteSrt(document.Subtitles);
        Assert.Equal("earlier", SubtitleTextFormat.ParseSrt(srt)[0].Text);
    }

    [Fact]
    public void CumulativeQuantizationKeepsRepresentableClipsPositiveWithinSubtitleRange()
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(1, 10), Karaoke =
            [
                new(0, 1, new(0), new(1, 3000), SceneColor.White),
                new(1, 1, new(1, 3000), new(2, 3000), SceneColor.White),
                new(2, 1, new(2, 3000), new(1, 1000), SceneColor.White)
            ]
        };
        var result = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(result.Text).Lines);
        Assert.Equal(3, imported.Karaoke.Length);
        Assert.All(imported.Karaoke, clip => Assert.True(clip.End > clip.Start));
        Assert.Equal(line.End, imported.End);
        Assert.True(imported.Karaoke[^1].End <= imported.End - imported.Start);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeQuantization");
    }

    [Fact]
    public void QuantizationPreservesTinyPositiveClipsWithoutMovingTheirIndependentStarts()
    {
        var line = new SubtitleLine { Text = "ab", End = new(1, 1000),
            Karaoke = [new(0, 1, new(0), new(1, 2000), SceneColor.White), new(1, 1, new(1, 2000), new(1, 1000), SceneColor.White)] };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        Assert.All(imported.Karaoke, group => Assert.Equal((MediaTime.Zero, new MediaTime(1, 100)), (group.Start, group.End)));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeQuantization");
        Assert.Equal(new MediaTime(1, 1000), line.End);
        Assert.Equal(2, line.Karaoke.Length);
    }

    [Fact]
    public void InlineStyleChangeInsideFullDurationClipDoesNotConsumeKaraokeTimeAgain()
    {
        var line = new SubtitleLine { Text = "ab", End = new(1),
            InlineSpans = [new(1, 1, new() { Bold = true })],
            Karaoke = [new(0, 2, MediaTime.Zero, new(1), SceneColor.White)] };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        var group = Assert.Single(imported.Karaoke);
        Assert.Equal((0, 2), (group.Utf16Start, group.Utf16Length));
        Assert.Equal(MediaTime.Zero, group.Start);
        Assert.Equal(new MediaTime(1), group.End);
        Assert.Contains(imported.InlineSpans, span => span.Style.Bold == true);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void KaraokeAcrossRichRunsRetainsInactiveRunColorsAndFixedActiveColor(KaraokeHighlightKind kind)
    {
        var blue = new SceneColor(0, 0, 1);
        var green = new SceneColor(0, 1, 0);
        var red = new SceneColor(1, 0, 0);
        var line = new SubtitleLine { Text = "ab", End = new(1), Style = new() { Fill = blue },
            InlineSpans = [new(1, 1, new() { Fill = green, Bold = true })],
            Karaoke = [new(0, 2, MediaTime.Zero, new(1), red) { HighlightKind = kind }] };
        var result = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(result.Text).Lines);
        var group = Assert.Single(imported.Karaoke);
        Assert.Equal(kind, group.HighlightKind);
        Assert.Equal(2, group.Utf16Length);
        Assert.All(imported.KaraokeStyleSpans, span => Assert.Equal(red, span.ActiveStyle!.Fill));
        Assert.Equal(blue, KaraokeVisualStyleResolver.RangeStyleAt(imported, 0, KaraokeVisualState.INACTIVE)!.Fill);
        Assert.Equal(green, KaraokeVisualStyleResolver.RangeStyleAt(imported, 1, KaraokeVisualState.INACTIVE)!.Fill);
        Assert.Equal(new MediaTime(1), group.End);
        Assert.Equal(blue, imported.InlineSpans.FirstOrDefault(span => span.Utf16Start == 0)?.Style.Fill ?? imported.Style.Fill);
        Assert.Contains(imported.InlineSpans, span => span.Utf16Start == 1 && span.Style.Fill == green && span.Style.Bold == true);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
    }

    [Fact]
    public void MultipleActiveColorsInsideSingleAssClipArePreservedAndRenderRunSemanticsAreDiagnosed()
    {
        var result = AssSubtitleFormat.Parse(File("{\\kf100\\1c&H0000FF&}a{\\1c&H00FF00&}b"));
        var line = Assert.Single(result.Lines);
        Assert.Single(line.Karaoke);
        Assert.Equal(new SceneColor(1, 0, 0), line.KaraokeStyleSpans[0].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(0, 1, 0), line.KaraokeStyleSpans[1].ActiveStyle!.Fill);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeStyleRuns");
    }

    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(true, false, 2, 1)]
    [InlineData(false, true, 1, 2)]
    public void MissingPlayResAxesUseTargetDimensionsAndReportInterpretation(bool includeX, bool includeY, int expectedScaleX, int expectedScaleY)
    {
        var source = File("{\\pos(120,80)}text");
        if (!includeX)
        {
            source = source.Replace("PlayResX: 640\n", "", StringComparison.Ordinal);
        }
        if (!includeY)
        {
            source = source.Replace("PlayResY: 360\n", "", StringComparison.Ordinal);
        }
        var result = AssSubtitleFormat.Parse(source, 1280, 720);
        var line = Assert.Single(result.Lines);
        Assert.Equal(new ScenePoint(120 * expectedScaleX, 80 * expectedScaleY), line.Style.Position!.Offset);
        Assert.Equal(20 * expectedScaleY, line.Style.FontSize);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
    }

    [Fact]
    public void ExportPreservesAllKaraokeAgainstContentOriginWithoutExtendingDialogue()
    {
        var line = new SubtitleLine { Text = "abcd", Start = new(5), End = new(6),
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White), new(1, 1, new(4), new(11, 2), SceneColor.White),
                new(2, 1, new(11, 2), new(7), SceneColor.White), new(3, 1, new(8), new(9), SceneColor.White)] };
        var document = SubtitleFormatTestDocument.Create(line);
        document = document with { Layers = [document.Layers[0] with { AnimationOffset = new(5) }] };
        var result = AssSubtitleFormat.Write(document);
        var importedClip = Assert.Single(AssSubtitleFormat.Parse(result.Text).Clips);
        var imported = importedClip.Line;
        Assert.Equal(line.Start, imported.Start);
        Assert.Equal(line.End, imported.End);
        Assert.Equal("abcd", imported.Text);
        Assert.Equal(new MediaTime(5), importedClip.ContentOffset);
        Assert.Equal(line.Karaoke.Select(group => (group.Utf16Start, group.Utf16Length, group.Start, group.End)),
            imported.Karaoke.Select(group => (group.Utf16Start, group.Utf16Length, group.Start, group.End)));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeCrop");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeClockCompatibility");
    }

    [Fact]
    public void AdvancedStyleEditPreservesPreciseTimingColorsFontResourceAndClipIdentity()
    {
        var font = Guid.NewGuid();
        var line = new SubtitleLine
        {
            Start = new(1, 7), End = new(2), Text = "ab", Style = new() { FontAssetId = font, Fill = new(0.123456789, 3, -0.2) },
            Karaoke = [new(0, 1, new(0), new(1, 333), new(0.123456789, 3, -0.2)), new(1, 1, new(1, 333), new(2, 333), SceneColor.White)]
        };
        var projection = AssTextProjection.Create(line);
        Assert.Same(line, AssTextProjection.Apply(line, projection.Source).Line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal));
        Assert.Equal(line.Start, edited.Line.Start);
        Assert.Equal(line.End, edited.Line.End);
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)), edited.Line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
        Assert.All(edited.Line.InlineSpans, span => Assert.Equal(font, span.Style.ApplyTo(line.Style).FontAssetId));
        Assert.Equal(line.Style.Fill, edited.Line.InlineSpans[0].Style.ApplyTo(line.Style).Fill);
    }

    [Fact]
    public void AdvancedSmallStyleChangesRemainEditsWhileUntouchedValuesKeepPrecision()
    {
        var line = new SubtitleLine
        {
            Text = "x",
            Style = new()
            {
                FontSize = 30.123456789123, StrokeWidth = 2.123456789123, ShadowBlur = 3.123456789123
            }
        };
        var projection = AssTextProjection.Create(line);
        var bold = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal)).Line;
        var boldStyle = Assert.Single(bold.InlineSpans).Style.ApplyTo(bold.Style);
        Assert.Equal(line.Style.FontSize, boldStyle.FontSize);
        Assert.Equal(line.Style.StrokeWidth, boldStyle.StrokeWidth);
        Assert.Equal(line.Style.ShadowBlur, boldStyle.ShadowBlur);
        var source = projection.Source.Replace("\\fs30.123456789", "\\fs30.12345679", StringComparison.Ordinal)
            .Replace("\\bord2.123456789", "\\bord2.12345679", StringComparison.Ordinal)
            .Replace("\\blur3.123456789", "\\blur3.12345679", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source).Line;
        var style = Assert.Single(edited.InlineSpans).Style.ApplyTo(edited.Style);
        Assert.Equal(30.12345679, style.FontSize);
        Assert.Equal(2.12345679, style.StrokeWidth);
        Assert.Equal(3.12345679, style.ShadowBlur);
        Assert.NotEqual(line.Style.FontSize, style.FontSize);
        Assert.NotEqual(line.Style.StrokeWidth, style.StrokeWidth);
        Assert.NotEqual(line.Style.ShadowBlur, style.ShadowBlur);
    }

    [Fact]
    public void AdvancedProjectionMapsTagsAndEscapedTextAndRejectsBrokenBlocks()
    {
        var line = new SubtitleLine { Text = "a\nb\u00a0c" };
        var projection = AssTextProjection.Create(line);
        Assert.Contains(projection.SourceMap, mapping => mapping.Utf16Length == 0 && mapping.SourceLength > 0);
        Assert.Contains(projection.SourceMap, mapping => mapping.SourceLength == 2 && mapping.Utf16Length == 1);
        Assert.Throws<InvalidDataException>(() => AssTextProjection.Apply(line, "{\\b1"));
        Assert.Throws<InvalidDataException>(() => AssTextProjection.Apply(line, "{\\k-1}text"));
    }

    [Fact]
    public void AdvancedPageRetainsOldContentTimesOutsideCroppedWindow()
    {
        var line = new SubtitleLine { Text = "ab", Start = new(5), End = new(6),
            Karaoke = [new(0, 1, new(0), new(1, 333), SceneColor.White), new(1, 1, new(8), new(9), SceneColor.White)] };
        var projection = AssTextProjection.Create(line, new(5));
        var result = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal), new(5));
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)), result.Line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
    }

    [Fact]
    public void AdvancedProjectSourceOmitsPositionAndPreservesNativeProperties()
    {
        var line = new SubtitleLine { Text = "x", Style = new() { Position = new() { Anchor = new(0.5, 1), Pivot = new(0.5, 1), Offset = new(0, -23.12345678912) } } };
        var projection = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360);
        Assert.DoesNotContain("\\pos", projection.Source, StringComparison.Ordinal);
        Assert.Same(line, AssTextProjection.Apply(line, projection.Source, canvasWidth: 640, canvasHeight: 360).Line);
        var styleEdit = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal), canvasWidth: 640, canvasHeight: 360);
        Assert.Equal(line.Style.Position, styleEdit.Line.Style.Position);
        Assert.Contains(styleEdit.Line.InlineSpans, span => span.Style.Bold == true);
        var alignmentEdit = AssTextProjection.Apply(line, projection.Source.Replace("\\an2", "\\an7", StringComparison.Ordinal), canvasWidth: 640, canvasHeight: 360);
        Assert.Equal(TextAlignment.TOP_LEFT, alignmentEdit.Line.Style.Alignment);
        Assert.Equal(line.Style.Position, alignmentEdit.Line.Style.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdvancedProjectPositionTagReportsFixWithoutTakingNativePosition(bool hasPosition)
    {
        var line = new SubtitleLine { Text = "x", Style = new() { Position = hasPosition ? new()
        {
            Anchor = new(0.5, 1), Pivot = new(0.25, 0.75), Offset = new(12.12345678912, -23.12345678912)
        } : null } };
        var projection = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360);
        var source = "{\\pos(100,200)}" + projection.Source;
        var result = AssTextProjection.Apply(line, source, canvasWidth: 640, canvasHeight: 360);
        Assert.Equal(line.Style.Position, result.Line.Style.Position);
        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Code == "Ass.ProjectPositionUnsupported"));
        Assert.Equal(line.Id, diagnostic.SubtitleId);
        Assert.Equal("\\pos(100,200)", source.Substring(diagnostic.SourceStart, diagnostic.SourceLength));
        Assert.Contains("Remove", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileAssPositionStillExportsAndImportsIntoNativePlacement()
    {
        var line = new SubtitleLine { Text = "x", Style = new() { Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }, Position = new()
        {
            Anchor = new(0.5, 1), Pivot = new(0.5, 1), Offset = new(12, -24)
        } } };
        var document = SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360 };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains("\\pos(332,336)", written.Text, StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(new ScenePoint(0, 0), imported.Style.Position!.Anchor);
        Assert.Equal(new ScenePoint(0.5, 1), imported.Style.Position.Pivot);
        Assert.Equal(new ScenePoint(332, 336), imported.Style.Position.Offset);
        Assert.DoesNotContain("\\pos", AssTextProjection.Create(imported, canvasWidth: 640, canvasHeight: 360).Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssEntryPointsPreserveCompleteUnicodeGroupAndPreciseTimes(bool fileImport)
    {
        const string TEXT = "a😀e\u0301";
        var line = fileImport
            ? Assert.Single(AssSubtitleFormat.Parse(File("{\\kf100}" + TEXT)).Lines)
            : AssTextProjection.Apply(new() { Text = TEXT }, "{\\kf100}" + TEXT).Line;
        var group = Assert.Single(line.Karaoke);
        Assert.Equal((0, 5), (group.Utf16Start, group.Utf16Length));
        Assert.Equal(MediaTime.Zero, group.Start);
        Assert.Equal(new MediaTime(1), group.End);
        var projection = AssTextProjection.Create(line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal)).Line;
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)), edited.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
    }

    [Fact]
    public void UnchangedAdvancedSourcePreservesCompleteGroupWithoutTimeQuantization()
    {
        var line = new SubtitleLine { Text = "a😀", Karaoke = [new(0, 3, MediaTime.Zero, new(1, 7), SceneColor.White)] };
        var projection = AssTextProjection.Create(line);
        var result = AssTextProjection.Apply(line, projection.Source).Line;
        Assert.Same(line, result);
        Assert.Equal(line.Karaoke[0], Assert.Single(result.Karaoke));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdvancedCodeEditingRetainsSavedHighlightConfigurationWithoutKaraoke(bool removeKaraoke)
    {
        var line = new SubtitleLine
        {
            Text = "x", End = new(1),
            Karaoke = removeKaraoke ? [new(0, 1, MediaTime.Zero, new(1), SceneColor.White)] : [],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "saved highlight", new()
            {
                Fill = new(4, 2, 1, 0.75), Stroke = new(2, 1, 0), StrokeWidth = 2.123456789123,
                ShadowOffset = new(3, 4), ShadowBlur = 1.123456789123
            })
        };
        var source = AssTextProjection.Create(line).Source.Replace("\\b0", "\\b1", StringComparison.Ordinal);
        if (removeKaraoke)
        {
            source = source.Replace("\\kf100", "\\k0", StringComparison.Ordinal);
        }
        var edited = AssTextProjection.Apply(line, source).Line;
        Assert.Empty(edited.Karaoke);
        Assert.Same(line.KaraokeStyle, edited.KaraokeStyle);
        Assert.Contains(edited.InlineSpans, span => span.Style.Bold == true);
    }

    [Fact]
    public void ExplicitWeightsAndScaledStylePreserveMarginsAndReportLayoutDifferences()
    {
        var source = File("{\\b400}normal{\\b700}bold").Replace("Outline, Shadow, Alignment, MarginV", "Outline, Shadow, Alignment, MarginV, ScaleX", StringComparison.Ordinal)
            .Replace(",2,2,2,20\n", ",2,2,2,20,120\n", StringComparison.Ordinal);
        var parsed = AssSubtitleFormat.Parse(source, 640, 360);
        var line = Assert.Single(parsed.Lines);
        Assert.False(line.InlineSpans.FirstOrDefault(span => span.Utf16Start == 0)?.Style.Bold ?? false);
        Assert.Contains(line.InlineSpans, span => span.Style.Bold == true);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleGeometry");
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformLayout");
        Assert.Equal(new ScenePoint(1.2, 1), Assert.Single(parsed.Clips).Transform.Scale);
        Assert.Equal(new SubtitleMargins(20, 20, 20), line.Style.Margins);
        Assert.Null(line.Style.Position);
    }

    [Fact]
    public void AttachmentsAndHighlightHdrLossAreExplicit()
    {
        var parsed = AssSubtitleFormat.Parse(File("text") + "\n[Fonts]\nfontname: Test.ttf\nencoded-data");
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.Attachments");
        var line = new SubtitleLine { Text = "a", Karaoke = [new(0, 1, new(0), new(1), new(4, -1, 0))] };
        Assert.Contains(AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)).Diagnostics, diagnostic => diagnostic.Code == "Ass.ColorRange");
    }

    [Fact]
    public void AssParseRejectsWrongHeadersFormatsAndUnsafeTagText()
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse("plain text"));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File("text").Replace("v4.00+", "v4.00", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File("text").Replace("Layer, Start", "Layer, Layer", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File("{\\pos(1)}text")));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File(new string((char)0xd800, 1))));
    }

    [Fact]
    public void SrtLossAnalysisAndWriterKeepTagsAsOrdinaryText()
    {
        var line = new SubtitleLine { Text = "{literal} <b>content</b>", InlineSpans = [new(0, 1, new() { Bold = true })] };
        Assert.Contains(SubtitleFormatLossAnalysis.ForSrt(SubtitleFormatTestDocument.Create(line)), diagnostic => diagnostic.Code == "Srt.Appearance");
        Assert.Equal(line.Text, Assert.Single(SubtitleTextFormat.ParseSrt(SubtitleTextFormat.WriteSrt([line]))).Text);
    }

    [Theory]
    [InlineData(@"C:\path\file")]
    [InlineData(@"two \\slashes")]
    public void OrdinaryBackslashesUseNativeAssLiteralSemantics(string text)
    {
        Assert.Equal(text, Assert.Single(AssSubtitleFormat.Parse(File(text)).Lines).Text);
        var line = new SubtitleLine { Text = text };
        Assert.Equal(text, Assert.Single(AssSubtitleFormat.Parse(AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)).Text).Lines).Text);
    }

    [Theory]
    [InlineData(@"literal \N")]
    [InlineData(@"literal \n")]
    [InlineData(@"literal \h")]
    public void NativeAssCannotLosslesslyExportLiteralControlEscapes(string text)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(new SubtitleLine { Text = text })));
    }

    [Fact]
    public void LiteralBracesReportLibassExtensionCompatibility()
    {
        var line = new SubtitleLine { Text = "{literal}" };
        var result = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = AssSubtitleFormat.Parse(result.Text);
        Assert.Equal(line.Text, Assert.Single(imported.Lines).Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.LiteralBraces");
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.LiteralBraces");
    }

    [Fact]
    public void BackslashImmediatelyBeforeGeneratedTagRefusesLossyExport()
    {
        var line = new SubtitleLine { Text = @"\a", InlineSpans = [new(1, 1, new() { Bold = true })] };
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)));
    }

    [Fact]
    public void StaticTextAfterKaraokeRestoresItsRunFill()
    {
        var blue = new SceneColor(0, 0, 1);
        var line = new SubtitleLine { Text = "ab", Style = new() { Fill = blue },
            Karaoke = [new(0, 1, MediaTime.Zero, new(1), new(1, 0, 0))] };
        var imported = Assert.Single(AssSubtitleFormat.Parse(AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)).Text).Lines);
        Assert.Single(imported.Karaoke);
        var second = imported.InlineSpans.FirstOrDefault(span => span.Utf16Start <= 1 && span.Utf16Start + span.Utf16Length > 1)?.Style.ApplyTo(imported.Style) ?? imported.Style;
        Assert.Equal(blue, second.Fill);
    }

    [Fact]
    public void RelativeFontSizeTagsApplyNativeScaleToCurrentSize()
    {
        var imported = Assert.Single(AssSubtitleFormat.Parse(File("{\\fs+5}a{\\fs-5}b{\\fs-20}c"), 640, 360).Lines);
        Assert.Contains(imported.InlineSpans, span => span.Utf16Start == 0 && span.Style.FontSize.Equals(30d));
        Assert.Contains(imported.InlineSpans, span => span.Utf16Start == 1 && span.Style.FontSize.Equals(15d));
        Assert.DoesNotContain(imported.InlineSpans, span => span.Utf16Start == 2);
    }

    [Fact]
    public void DuplicatePlacementTagsKeepNativeFirstValueAndReportIgnoredValues()
    {
        var result = AssSubtitleFormat.Parse(File("{\\an7\\an9\\pos(10,20)\\pos(30,40)}a"), 640, 360);
        var imported = Assert.Single(result.Lines);
        Assert.Equal(TextAlignment.TOP_LEFT, imported.Style.Alignment);
        Assert.Equal(new ScenePoint(10, 20), imported.Style.Position!.Offset);
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "Ass.DuplicatePlacement"));
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("e\u0301")]
    [InlineData("👩‍💻")]
    public void AdvancedGraphemeReplacementRetainsUntouchedClipTagsAndExactTimes(string grapheme)
    {
        var font = Guid.NewGuid();
        var fill = new SceneColor(0.123456789123, 3, -0.2);
        var line = new SubtitleLine { Text = "a" + grapheme + "c", Style = new() { FontAssetId = font, Fill = fill }, Karaoke =
            [new(0, 1, MediaTime.Zero, new(1, 7), SceneColor.White), new(1, grapheme.Length, new(1, 7), new(2, 7), SceneColor.White),
                new(1 + grapheme.Length, 1, new(2, 7), new(3, 7), SceneColor.White)] };
        var projection = AssTextProjection.Create(line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace(grapheme, "字", StringComparison.Ordinal)).Line;
        Assert.Equal("a字c", edited.Text);
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)), edited.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
        Assert.Equal((1, 1), (edited.Karaoke[1].Utf16Start, edited.Karaoke[1].Utf16Length));
        Assert.Equal((2, 1), (edited.Karaoke[2].Utf16Start, edited.Karaoke[2].Utf16Length));
        var style = edited.InlineSpans.FirstOrDefault(span => span.Utf16Start <= 1 && span.Utf16Start + span.Utf16Length > 1)?.Style.ApplyTo(edited.Style) ?? edited.Style;
        Assert.Equal(font, style.FontAssetId);
        Assert.Equal(fill, style.Fill);
    }

    [Fact]
    public void AdvancedTextInsertionRetainsFollowingClipIdentityAndExactTimes()
    {
        var line = new SubtitleLine { Text = "a😀c", Karaoke =
            [new(0, 1, MediaTime.Zero, new(1, 7), SceneColor.White), new(1, 2, new(1, 7), new(2, 7), SceneColor.White),
                new(3, 1, new(2, 7), new(3, 7), SceneColor.White)] };
        var projection = AssTextProjection.Create(line);
        var first = projection.SourceMap.First(mapping => mapping.Utf16Length == 1 && mapping.Utf16Start == 0);
        var edited = AssTextProjection.Apply(line, projection.Source.Insert(first.SourceStart, "前")).Line;
        Assert.Equal("前a😀c", edited.Text);
        Assert.Equal(3, edited.Karaoke.Length);
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)), edited.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
        Assert.Equal((0, 2), (edited.Karaoke[0].Utf16Start, edited.Karaoke[0].Utf16Length));
        Assert.Equal((2, 2), (edited.Karaoke[1].Utf16Start, edited.Karaoke[1].Utf16Length));
        Assert.Equal((4, 1), (edited.Karaoke[2].Utf16Start, edited.Karaoke[2].Utf16Length));
    }

    private static string File(string body) => """
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, Outline, Shadow, Alignment, MarginV
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,2,2,2,20
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.25,0:00:03.50,Default,,0,0,0,,
        """ + body;
}
