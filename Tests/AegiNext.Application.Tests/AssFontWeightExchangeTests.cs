using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssFontWeightExchangeTests
{
    private static readonly int[] expectedResetWeights = [600, 900, 600, 600];

    [Theory]
    [InlineData(100, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    [InlineData(600, false)]
    [InlineData(700, true)]
    [InlineData(800, true)]
    [InlineData(900, true)]
    [InlineData(1000, true)]
    public void ExactNumericWeightUsesTheRealVariantAndNativeBoldConvention(int weight, bool bold)
    {
        var variant = Variant("Real Face", weight);
        var resolver = new RecordingAssFontWeightResolver(("Example", variant));
        var parsed = Parse("{\\b" + weight + "}x", resolver);
        var style = StyleAt(parsed.Line, 0);
        Assert.Equal(variant, style.FontVariant);
        Assert.Equal(bold, style.Bold);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        Assert.Equal(("Example", weight, 5, false), Assert.Single(resolver.Requests));
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(600, true)]
    [InlineData(900, true)]
    public void UnmatchedWeightKeepsTheExistingBooleanFallbackAndReportsItsSource(int weight, bool bold)
    {
        var parsed = Parse("{\\b" + weight + "}xy");
        var style = StyleAt(parsed.Line, 0);
        Assert.Null(style.FontVariant);
        Assert.Equal(bold, style.Bold);
        var loss = Assert.Single(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        Assert.Equal(1, loss.SourceStart);
        Assert.Equal(5, loss.SourceLength);
    }

    [Fact]
    public void OnlyTheFinalFontRequestAppliedToTextIsResolvedOrReported()
    {
        var variant = Variant("Real SemiBold Italic", 600, true);
        var resolver = new RecordingAssFontWeightResolver(("Other", variant));
        var parsed = Parse("{\\b500\\fnOther\\b600\\i1}x{\\b800}", resolver);
        Assert.Equal(variant, StyleAt(parsed.Line, 0).FontVariant);
        Assert.Single(resolver.Requests);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Theory]
    [InlineData("{\\b600\\r}x")]
    [InlineData("{\\b600\\b0\\i1}x")]
    [InlineData("{\\b600}")]
    [InlineData("{\\b600\\p1}m 0 0 l 10 10")]
    public void DiscardedWeightRequestsDoNotResolveFacesOrReportAppearanceLoss(string source)
    {
        var resolver = new RecordingAssFontWeightResolver();
        var parsed = Parse(source, resolver);
        Assert.Empty(resolver.Requests);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Theory]
    [InlineData(600, false, "0", false)]
    [InlineData(900, true, "1", true)]
    public void BooleanBoldTagsClearAnInheritedVariantEvenWhenTheBooleanDoesNotChange(
        int weight, bool currentBold, string tag, bool expectedBold)
    {
        var original = Line() with { Style = Style() with { FontVariant = Variant("Inherited", weight), Bold = currentBold } };
        var parsed = Parse("{\\b" + tag + "}x", original: original);
        var style = StyleAt(parsed.Line, 0);
        Assert.Null(style.FontVariant);
        Assert.Equal(expectedBold, style.Bold);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Fact]
    public void FamilyAndItalicChangesRematchTheCurrentNumericWeightWithoutReusingAnOldIdentity()
    {
        var regular = Variant("Real SemiBold", 600);
        var italic = Variant("Real SemiBold Italic", 600, true);
        var otherItalic = Variant("Other SemiBold Italic", 600, true);
        var otherRegular = Variant("Other SemiBold", 600);
        var resolver = new RecordingAssFontWeightResolver(("Example", regular), ("Example", italic),
            ("Other", otherItalic), ("Other", otherRegular));
        var parsed = Parse("{\\b600}A{\\i1}B{\\fnOther}C{\\i0}D", resolver);
        Assert.Equal(regular, StyleAt(parsed.Line, 0).FontVariant);
        Assert.Equal(italic, StyleAt(parsed.Line, 1).FontVariant);
        Assert.Equal(otherItalic, StyleAt(parsed.Line, 2).FontVariant);
        Assert.Equal(otherRegular, StyleAt(parsed.Line, 3).FontVariant);
        Assert.Equal(4, resolver.Requests.Count);
        Assert.All(resolver.Requests, request => Assert.Equal(600, request.Weight));
    }

    [Theory]
    [InlineData("\\b600\\fnOther\\i1")]
    [InlineData("\\b600\\i1\\fnOther")]
    [InlineData("\\fnOther\\b600\\i1")]
    [InlineData("\\fnOther\\i1\\b600")]
    [InlineData("\\i1\\b600\\fnOther")]
    [InlineData("\\i1\\fnOther\\b600")]
    public void FontTagsInAnyOrderResolveTheSameFinalFace(string tags)
    {
        var variant = Variant("Other SemiBold Italic", 600, true);
        var resolver = new RecordingAssFontWeightResolver(("Other", variant));
        var parsed = Parse("{" + tags + "}x", resolver);
        var style = StyleAt(parsed.Line, 0);
        Assert.Equal("Other", style.FontFamily);
        Assert.True(style.Italic);
        Assert.Equal(variant, style.FontVariant);
        Assert.False(style.Bold);
        Assert.Equal(("Other", 600, 5, true), Assert.Single(resolver.Requests));
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Theory]
    [InlineData(500, 5, false)]
    [InlineData(600, 4, false)]
    [InlineData(600, 5, true)]
    public void IncorrectResolverCharacteristicsAreRejectedAndReported(int weight, int width, bool italic)
    {
        var resolver = new RecordingAssFontWeightResolver
        {
            ForcedVariant = Variant("Mismatched Face", weight, italic) with { Width = width }
        };
        var parsed = Parse("{\\b600}x", resolver);
        var style = StyleAt(parsed.Line, 0);
        Assert.Null(style.FontVariant);
        Assert.True(style.Bold);
        Assert.False(style.Italic);
        Assert.Equal(("Example", 600, 5, false), Assert.Single(resolver.Requests));
        Assert.Single(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Fact]
    public void EmptyBoldRestoresTheResetWeightWhileKeepingTheCurrentFamilyAndItalic()
    {
        var initial = Variant("Real SemiBold", 600);
        var other = Variant("Other SemiBold Italic", 600, true);
        var resolver = new RecordingAssFontWeightResolver(("Other", other));
        var original = Line() with { Style = Style() with { FontVariant = initial } };
        var parsed = Parse("{\\b900\\fnOther\\i1\\b}x", resolver, original);
        var style = StyleAt(parsed.Line, 0);
        Assert.Equal("Other", style.FontFamily);
        Assert.True(style.Italic);
        Assert.Equal(other, style.FontVariant);
        Assert.Equal(("Other", 600, 5, true), Assert.Single(resolver.Requests));
    }

    [Fact]
    public void NamedResetReplacesTheWeightBaselineAndEmptyItalicAndFamilyRematchIt()
    {
        var named = Style() with { FontFamily = "Named", FontVariant = Variant("Named Light", 300) };
        var lightItalic = Variant("Named Light Italic", 300, true);
        var light = named.FontVariant!.Value;
        var resolver = new RecordingAssFontWeightResolver(("Named", lightItalic), ("Named", light));
        var styles = new Dictionary<string, AssStyleDefinition> { ["NamedStyle"] = new("NamedStyle", named, SceneColor.White) };
        var parsed = Parse("{\\b900\\rNamedStyle\\i1}A{\\fnOther\\i\\fn\\b}B", resolver, styles: styles);
        Assert.Equal(lightItalic, StyleAt(parsed.Line, 0).FontVariant);
        Assert.Equal(light, StyleAt(parsed.Line, 1).FontVariant);
        Assert.Equal("Named", StyleAt(parsed.Line, 1).FontFamily);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Fact]
    public void ProjectSourceRetainsItsBooleanEditingSemanticsAndDoesNotUseTheResolver()
    {
        var resolver = new RecordingAssFontWeightResolver(("Example", Variant("Real SemiBold", 600)));
        var parsed = Parse("{\\b600}x", resolver, projectSource: true);
        Assert.True(StyleAt(parsed.Line, 0).Bold);
        Assert.Null(StyleAt(parsed.Line, 0).FontVariant);
        Assert.Empty(resolver.Requests);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(600)]
    [InlineData(900)]
    [InlineData(1000)]
    public void FileBodyWritesExactSystemWeightAndKeepsTheIdentityLoss(int weight)
    {
        var line = Line() with { Text = "x", Style = Style() with { FontVariant = Variant("Real Face", weight) } };
        var written = AssTextWriter.Write(line, MediaTime.Zero);
        Assert.Contains("\\b" + weight + "\\i0", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontVariant");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        var resolver = new RecordingAssFontWeightResolver(("Example", line.Style.FontVariant!.Value));
        var parsed = Parse(written.Text, resolver);
        Assert.Equal(line.Style.FontVariant, StyleAt(parsed.Line, 0).FontVariant);
    }

    [Fact]
    public void PublicFileImportSurvivesProjectPersistenceAndFullFileExportImport()
    {
        var semiBold = Variant("Real SemiBold", 600);
        var blackItalic = Variant("Real Black Italic", 900, true);
        var light = Variant("Real Light", 300);
        var resolver = new RecordingAssFontWeightResolver(("Noto Sans", semiBold), ("Noto Sans", blackItalic),
            ("Noto Sans", light));
        var parsed = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\b600}A{\\b900\\i1}B{\\r\\b300}C"),
            640, 360, resolver);
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Imported");
        var bytes = ProjectStore.Serialize(imported);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        var persistedLine = Assert.Single(restored.Subtitles);
        Assert.Equal(Assert.Single(imported.Subtitles).InlineSpans.ToArray(), persistedLine.InlineSpans.ToArray());
        Assert.Equal(new[] { semiBold, blackItalic, light }, Enumerable.Range(0, 3)
            .Select(offset => StyleAt(persistedLine, offset).FontVariant!.Value).ToArray());
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");

        var written = AssSubtitleFormat.Write(restored);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontVariant");
        var reparsed = AssSubtitleFormat.Parse(written.Text, 640, 360, resolver);
        var roundTripped = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, reparsed, "Round trip");
        var line = Assert.Single(roundTripped.Subtitles);
        Assert.Equal("ABC", line.Text);
        for (var offset = 0; offset < line.Text.Length; offset++)
        {
            var before = StyleAt(persistedLine, offset);
            var after = StyleAt(line, offset);
            Assert.Equal((before.FontFamily, before.FontVariant, before.Bold, before.Italic),
                (after.FontFamily, after.FontVariant, after.Bold, after.Italic));
        }
        Assert.DoesNotContain(reparsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Fact]
    public void FileOutputReemitsPreciseWeightAfterInlineAndKaraokeResets()
    {
        var semiBold = Variant("Real SemiBold", 600);
        var black = Variant("Real Black", 900);
        var line = Line() with
        {
            Text = "ABCD",
            Style = Style() with { FontVariant = semiBold },
            InlineSpans = [new(1, 1, new() { FontVariant = black, Bold = true })],
            Karaoke = [new(0, 3, MediaTime.Zero, new(1), SceneColor.White), new(3, 1, new(1), new(2), SceneColor.White)]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var body = written.Text.Split('\n').Single(value => value.StartsWith("Dialogue:", StringComparison.Ordinal)).Split(',', 10)[9];
        var resetWeights = Regex.Matches(body, @"\{\\r[^}]*\\b(\d+)\\i")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(expectedResetWeights, resetWeights);
        var resolver = new RecordingAssFontWeightResolver(("Example", semiBold), ("Example", black));
        var parsed = AssSubtitleFormat.Parse(written.Text, fontWeightResolver: resolver);
        var imported = Assert.Single(parsed.Lines);
        Assert.Equal(line.Text, imported.Text);
        Assert.Equal(new[] { semiBold, black, semiBold, semiBold }, Enumerable.Range(0, 4)
            .Select(offset => StyleAt(imported, offset).FontVariant!.Value).ToArray());
        Assert.Equal(line.Karaoke.Select(group => (group.Utf16Start, group.Utf16Length, group.Start, group.End)),
            imported.Karaoke.Select(group => (group.Utf16Start, group.Utf16Length, group.Start, group.End)));
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    public void NativeWeightsBelowTheExchangeRangeUseBooleanOutputAndReportTheirLoss(int weight)
    {
        var line = Line() with { Text = "x", Style = Style() with { FontVariant = Variant("Real Thin Face", weight), Bold = true } };
        var written = AssTextWriter.Write(line, MediaTime.Zero);
        Assert.Contains("\\b1\\i0", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        Assert.Equal(weight, line.Style.FontVariant!.Value.Weight);
    }

    [Fact]
    public void EmbeddedFontStylesDoNotExportTheIgnoredSystemVariantAsAnExactWeight()
    {
        var line = Line() with { Text = "x", Style = Style() with
        {
            FontAssetId = Guid.NewGuid(), FontVariant = Variant("System SemiBold", 600)
        } };
        Assert.Contains("\\b0\\i0", AssTextWriter.Write(line, MediaTime.Zero).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FileOutputUsesEffectiveVariantItalicWhileProjectionKeepsOriginalFlags()
    {
        var line = Line() with { Text = "x", Style = Style() with { FontVariant = Variant("Real SemiBold Italic", 600, true) } };
        var written = AssTextWriter.Write(line, MediaTime.Zero);
        Assert.Contains("\\b600\\i1", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontVariant" && diagnostic.Message.Contains("synthesize", StringComparison.Ordinal));
        var projected = AssTextProjection.Create(line);
        Assert.Contains("\\b0\\i0", projected.Source, StringComparison.Ordinal);
        Assert.Equal(line, AssTextProjection.Apply(line, projected.Source).Line);
    }

    private static AssTextEditResult Parse(string source, IAssFontWeightResolver? resolver = null,
        SubtitleLine? original = null, IReadOnlyDictionary<string, AssStyleDefinition>? styles = null, bool projectSource = false)
    {
        return new AssTextParser(original ?? Line(), styles ?? new Dictionary<string, AssStyleDefinition>(), SceneColor.White,
            projectSource: projectSource, wrapStyle: 1, fontWeightResolver: resolver).Parse(source);
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)
            ?.Style.ApplyTo(line.Style) ?? line.Style;
    }

    private static SubtitleLine Line() => new() { Style = Style(), End = new(2) };

    private static SubtitleStyle Style() => new()
    {
        FontFamily = "Example", FontSize = 20, WrapMode = SubtitleWrapMode.NATURAL,
        StrokeWidth = 0, FillBlur = 0, StrokeBlur = 0, ShadowBlur = 0, ShadowColor = SceneColor.Transparent
    };

    private static SubtitleFontVariant Variant(string name, int weight, bool italic = false) => new()
    {
        Name = name, PostScriptName = name.Replace(" ", string.Empty, StringComparison.Ordinal), Weight = weight, Italic = italic
    };
}
