using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssStyleImportTests
{
    private const string STYLE_FORMAT = "Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding";
    private const string EVENTS = "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,text";

    [Theory]
    [InlineData("")]
    [InlineData(EVENTS)]
    [InlineData("[Events]\nnot a valid event")]
    [InlineData("[Events]\nFormat: Text, Start\nDialogue: broken")]
    [InlineData("[Events]\nFormat: Start, Start, Text\nDialogue: invalid")]
    [InlineData("[Events]\nDialogue: missing format")]
    public void AllDeclaredStylesAreImportedWithoutReadingEvents(string events)
    {
        var source = File(rows: StyleRow() + "\n" + StyleRow("Unused"), events: events);

        var result = AssSubtitleFormat.ParseStyles(source);

        Assert.Equal(new[] { "Default", "Unused" }, result.Styles.Select(style => style.Name));
        Assert.All(result.Styles, style => Assert.Equal(28, style.Style.FontSize));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void AStylesOnlyFileSupportsBomUnicodeAndAllLineEndings(string newline)
    {
        var source = "\uFEFF" + ("[V4+ Styles]\nFormat: " + STYLE_FORMAT + "\n" + StyleRow("中文字幕😀"))
            .Replace("\n", newline, StringComparison.Ordinal);

        var result = AssSubtitleFormat.ParseStyles(source);
        var imported = Assert.Single(result.Styles);

        Assert.Equal("中文字幕😀", imported.Name);
        Assert.Equal((1920, 1080), (imported.Style.Scaling.ReferenceWidth, imported.Style.Scaling.ReferenceHeight));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
    }

    [Fact]
    public void FormatsAndSectionsAllowReorderingAndCaseInsensitiveFields()
    {
        var result = AssSubtitleFormat.ParseStyles("""
            [sCrIpT iNfO]
            sCrIpTtYpE: v4.00+
            pLaYrEsX: 640
            pLaYrEsY: 360
            [v4+ sTyLeS]
            fOrMaT: FONTsize, PRIMARYcolour, NAME, FONTname
            sTyLe: 35,&H800000FF,重排样式,Fixture
            """);
        var imported = Assert.Single(result.Styles);

        Assert.Equal("重排样式", imported.Name);
        Assert.Equal(35, imported.Style.FontSize);
        Assert.Equal("Fixture", imported.Style.FontFamily);
        Assert.Equal(new SceneColor(1, 0, 0, 127d / 255), imported.Style.Fill);
        Assert.Empty(imported.Diagnostics);
    }

    [Fact]
    public void SupportedAppearanceValuesArePreservedInSourceReferenceUnits()
    {
        var style = Assert.Single(AssSubtitleFormat.ParseStyles(File()).Styles).Style;

        Assert.Equal("Fixture", style.FontFamily);
        Assert.Null(style.FontAssetId);
        Assert.Equal(28, style.FontSize);
        Assert.Equal(2.5, style.LetterSpacing);
        Assert.Equal(SubtitleWrapMode.NATURAL, style.WrapMode);
        Assert.Equal(new SceneColor(1, 0, 0, 127d / 255), style.Fill);
        Assert.Equal(new SceneColor(0, 1, 0, 191d / 255), style.Stroke);
        Assert.Equal(new SceneColor(0, 0, 1, 223d / 255), style.ShadowColor);
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.True(style.Strikethrough);
        Assert.Equal(3, style.StrokeWidth);
        Assert.Equal(new ScenePoint(4, 4), style.ShadowOffset);
        Assert.Equal(0, style.ShadowBlur);
        Assert.Equal(TextAlignment.TOP_RIGHT, style.Alignment);
        Assert.Equal(new SubtitleMargins(11, 22, 33), style.Margins);
        Assert.Equal((640, 360), (style.Scaling.ReferenceWidth, style.Scaling.ReferenceHeight));
    }

    [Theory]
    [InlineData("WrapStyle: 0")]
    [InlineData("WrapStyle: 2")]
    [InlineData("WrapStyle: 3")]
    [InlineData("WrapStyle: 9")]
    [InlineData("ScaledBorderAndShadow: no")]
    [InlineData("ScaledBorderAndShadow: no\nLayoutResX: 1280\nLayoutResY: 720")]
    [InlineData("ScaledBorderAndShadow: no\nLayoutResX: 1280\nLayoutResY: 1080")]
    public void AppearanceMatchesCompleteSourceResolutionImport(string extraInfo)
    {
        var source = File(info: "PlayResX: 640\nPlayResY: 360\n" + extraInfo, events: EVENTS);

        var styles = AssSubtitleFormat.ParseStyles(source);
        var complete = AssSubtitleFormat.ParseAtSourceResolution(source);

        Assert.Equal(Assert.Single(complete.Result.Lines).Style, Assert.Single(styles.Styles).Style);
    }

    [Theory]
    [InlineData("PlayResX: 640\nPlayResY: 360", 640, 360, false)]
    [InlineData("PlayResX: 640", 640, 1080, true)]
    [InlineData("PlayResY: 360", 1920, 360, true)]
    [InlineData("", 1920, 1080, true)]
    public void MissingResolutionAxesUseIndependentDefaults(string info, int width, int height, bool hasDiagnostic)
    {
        var result = AssSubtitleFormat.ParseStyles(File(info));
        var style = Assert.Single(result.Styles).Style;

        Assert.Equal((width, height), (style.Scaling.ReferenceWidth, style.Scaling.ReferenceHeight));
        Assert.Equal(28, style.FontSize);
        Assert.Equal(new SubtitleMargins(11, 22, 33), style.Margins);
        Assert.Equal(hasDiagnostic, result.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.PlayRes"));
    }

    [Fact]
    public void StyleReferenceDimensionsDoNotAllocateOrValidateAProjectCanvas()
    {
        var source = File("PlayResX: 8192\nPlayResY: 4320", events: EVENTS);

        var style = Assert.Single(AssSubtitleFormat.ParseStyles(source).Styles).Style;

        Assert.Equal((8192, 4320), (style.Scaling.ReferenceWidth, style.Scaling.ReferenceHeight));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseAtSourceResolution(source));
        Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Lines);
    }

    [Fact]
    public void RepeatedScriptInfoUsesItsLastValueAndKeepsGlobalDiagnosticsSeparate()
    {
        var source = File("PlayResX: 1280\nPlayResX: 640\nYCbCr Matrix: None\nYCbCr Matrix: TV.709\nWrapStyle: 9\nScaledBorderAndShadow: no");

        var result = AssSubtitleFormat.ParseStyles(source);

        Assert.Equal(new[] { "Ass.YCbCrMatrix", "Ass.WrapStyle", "Ass.BorderLayoutResolution", "Ass.WrapModeApproximation", "Ass.PlayRes" },
            result.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal((640, 1080), (result.Styles[0].Style.Scaling.ReferenceWidth, result.Styles[0].Style.Scaling.ReferenceHeight));
        Assert.DoesNotContain(result.Styles[0].Diagnostics, diagnostic => diagnostic.Code is "Ass.YCbCrMatrix" or "Ass.WrapStyle" or "Ass.PlayRes");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("WrapStyle: 0", true)]
    [InlineData("WrapStyle: 1", false)]
    [InlineData("WrapStyle: 2", false)]
    [InlineData("WrapStyle: 3", true)]
    [InlineData("WrapStyle: 9", true)]
    public void SmartWrappingLossIsReportedAtScriptLevel(string extraInfo, bool approximates)
    {
        var result = AssSubtitleFormat.ParseStyles(File("PlayResX: 640\nPlayResY: 360\n" + extraInfo));

        Assert.Equal(approximates, result.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.WrapModeApproximation"));
        Assert.DoesNotContain(result.Styles[0].Diagnostics, diagnostic => diagnostic.Code == "Ass.WrapModeApproximation");
    }

    [Fact]
    public void UnrelatedSectionsAreIgnoredAndMixedNewlinesKeepOriginalDiagnosticLocations()
    {
        var row = "\t" + StyleRow("Mixed😀", "Angle", "15");
        var source = "\uFEFF[Script Info]\r\nPlayResX: 640\rPlayResY: 360\nWrapStyle: 1\r\n" +
            "[Fonts]\ninvalid font payload\r[Graphics]\r\ninvalid image payload\n" +
            "[V4+ Styles]\rFormat: " + STYLE_FORMAT + "\r\n" + row + "\n[Events]\rbroken event";

        var result = AssSubtitleFormat.ParseStyles(source);

        Assert.Empty(result.Diagnostics);
        Assert.All(Assert.Single(result.Styles).Diagnostics,
            diagnostic => Assert.Equal(row, source.Substring(diagnostic.SourceStart, diagnostic.SourceLength)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Events]\nnot a valid event")]
    [InlineData("[Events]\nFormat: Text, Start\nDialogue: broken")]
    [InlineData("[Events]\nDialogue: missing format")]
    public void StylesOnlySupportDoesNotRelaxCompleteImportStructureValidation(string events)
    {
        var source = File(events: events);

        Assert.Single(AssSubtitleFormat.ParseStyles(source).Styles);
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseAtSourceResolution(source));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(source));
    }

    [Theory]
    [InlineData("ScaleX", "125", "Ass.StyleScale")]
    [InlineData("ScaleY", "80", "Ass.StyleScale")]
    [InlineData("Angle", "15", "Ass.StyleRotation")]
    [InlineData("BorderStyle", "3", "Ass.StyleGeometry")]
    [InlineData("SecondaryColour", "&H0000FFFF", "Ass.StyleSecondaryColor")]
    [InlineData("Spacing", "5000", "Ass.LetterSpacingRange")]
    public void UnsupportedAppearanceDiagnosticsBelongOnlyToTheirSourceStyle(string field, string value, string code)
    {
        var source = File(rows: StyleRow("Changed", field, value) + "\n" + MinimalStyleRow("Unchanged"));

        var result = AssSubtitleFormat.ParseStyles(source);

        Assert.Contains(result.Styles[0].Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Empty(result.Styles[1].Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void DefaultTransformsDoNotReportTransformLossesAndNonuniformBorderLossIsPerStyle()
    {
        var source = File("PlayResX: 640\nPlayResY: 360\nScaledBorderAndShadow: no\nLayoutResX: 1280\nLayoutResY: 1080");

        var result = AssSubtitleFormat.ParseStyles(source);
        var imported = Assert.Single(result.Styles);

        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code is "Ass.StyleScale" or "Ass.StyleRotation" or "Ass.StyleGeometry");
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderResampling");
        Assert.Equal(3 * Math.Sqrt(1d / 6), imported.Style.StrokeWidth, 12);
        Assert.Equal(new ScenePoint(2, 4d / 3), imported.Style.ShadowOffset);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void StyleDiagnosticsPointToTheOriginalUtf16Line(string newline)
    {
        var row = "  " + StyleRow("中文字幕😀", "Angle", "15");
        var source = "\uFEFF" + File(rows: MinimalStyleRow("Earlier😀") + "\nFormat: " + STYLE_FORMAT + "\n" + row)
            .Replace("\n", newline, StringComparison.Ordinal);

        var imported = AssSubtitleFormat.ParseStyles(source).Styles[1];

        Assert.All(imported.Diagnostics, diagnostic =>
        {
            Assert.Equal(row, source.Substring(diagnostic.SourceStart, diagnostic.SourceLength));
            Assert.Null(diagnostic.SubtitleId);
            Assert.Null(diagnostic.LayerId);
        });
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleRotation");
    }

    [Theory]
    [InlineData("PlayResX: 0")]
    [InlineData("PlayResY: -1")]
    [InlineData("PlayResX: 32769")]
    [InlineData("PlayResY: invalid")]
    [InlineData("LayoutResX: invalid")]
    [InlineData("WrapStyle: invalid")]
    [InlineData("ScriptType: v4.00")]
    public void InvalidScriptMetadataIsRejected(string info)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseStyles(File(info)));
    }

    [Theory]
    [InlineData("Fontsize", "not-a-number")]
    [InlineData("Fontsize", "NaN")]
    [InlineData("Fontsize", "0")]
    [InlineData("PrimaryColour", "not-a-color")]
    [InlineData("SecondaryColour", "not-a-color")]
    [InlineData("Alignment", "10")]
    [InlineData("MarginL", "-1")]
    [InlineData("ScaleX", "invalid")]
    [InlineData("ScaleY", "Infinity")]
    [InlineData("Angle", "invalid")]
    [InlineData("BorderStyle", "invalid")]
    public void InvalidStyleValuesFailWithTheStyleNameAndSourceLine(string field, string value)
    {
        var source = File(rows: StyleRow("Broken", field, value));

        var error = Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseStyles(source));

        Assert.Contains("Broken", error.Message, StringComparison.Ordinal);
        Assert.Contains("line", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(error.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Script Info]\nScriptType: v4.00+")]
    [InlineData("[V4 Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour\nStyle: Default,Fixture,20,&H00FFFFFF")]
    [InlineData("[V4+ Styles]")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour")]
    [InlineData("[V4+ Styles]\nStyle: Default,Fixture,20,&H00FFFFFF")]
    [InlineData("[V4+ Styles]\nFormat: Name, Name, Fontname\nStyle: Default,Default,Fixture")]
    [InlineData("[V4+ Styles]\nFormat: Name,,Fontname\nStyle: Default,,Fixture")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour\nStyle: Default,Fixture,20")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, Custom\nStyle: Default,Fixture,20,&H00FFFFFF,ignored,extra")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontsize, PrimaryColour\nStyle: Default,20,&H00FFFFFF")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour\nStyle: ,Fixture,20,&H00FFFFFF")]
    [InlineData("[V4+ Styles]\nmalformed row")]
    public void MissingOrMalformedStyleTablesAreRejected(string source)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseStyles(source));
    }

    [Fact]
    public void ExactDuplicateNamesFailButCaseDifferencesRemainDistinctAssStyles()
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseStyles(File(rows: StyleRow() + "\n" + StyleRow())));

        var result = AssSubtitleFormat.ParseStyles(File(rows: StyleRow() + "\n" + StyleRow("default")));

        Assert.Equal(new[] { "Default", "default" }, result.Styles.Select(style => style.Name));
    }

    [Theory]
    [InlineData("Spacing")]
    [InlineData("Outline")]
    [InlineData("Shadow")]
    public void ExistingNegativeAppearanceNormalizationIsPreserved(string field)
    {
        var imported = Assert.Single(AssSubtitleFormat.ParseStyles(File(rows: StyleRow(field: field, value: "-4"))).Styles);

        Assert.Equal(field == "Spacing" ? 0 : 2.5, imported.Style.LetterSpacing);
        Assert.Equal(field == "Outline" ? 0 : 3, imported.Style.StrokeWidth);
        Assert.Equal(field == "Shadow" ? new ScenePoint(0, 0) : new(4, 4), imported.Style.ShadowOffset);
    }

    private static string File(string info = "PlayResX: 640\nPlayResY: 360\nWrapStyle: 1", string? rows = null, string events = "") => $"""
        [Script Info]
        ScriptType: v4.00+
        {info}
        [V4+ Styles]
        Format: {STYLE_FORMAT}
        {rows ?? StyleRow()}
        {events}
        """;

    private static string StyleRow(string name = "Default", string? field = null, string? value = null)
    {
        var values = "Default,Fixture,28,&H800000FF,&H000000FF,&H4000FF00,&H20FF0000,-1,-1,-1,-1,100,100,2.5,0,1,3,4,9,11,22,33,1".Split(',');
        values[0] = name;
        if (field is not null)
        {
            values[Array.IndexOf(STYLE_FORMAT.Split(',', StringSplitOptions.TrimEntries), field)] = value!;
        }
        return "Style: " + string.Join(',', values);
    }

    private static string MinimalStyleRow(string name) => $"""
        Format: Name, Fontname, Fontsize, PrimaryColour
        Style: {name},Fixture,28,&H00FFFFFF
        """;
}
