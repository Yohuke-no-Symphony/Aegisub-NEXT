using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssExportStyleBaselineTests
{
    [Fact]
    public void DescriptorKeepsTheExistingSerializedStyleFields()
    {
        var style = new SubtitleStyle
        {
            FontFamily = "Test Sans", FontSize = 42.5, Fill = SceneColor.White,
            Stroke = SceneColor.Black, ShadowColor = new(0, 0, 0, 0.5),
            Bold = true, Underline = true, Strikethrough = true, LetterSpacing = 1.25,
            StrokeWidth = 3.5, ShadowOffset = new(9, 4), Alignment = TextAlignment.BOTTOM_CENTER,
            Margins = new(12, 24, 36)
        };
        var line = new SubtitleLine { Text = "text", StyleName = "Shared", Style = style };
        var output = new StringBuilder();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var mapping = AssStyleTable.Write([line], output, diagnostics);
        var exported = mapping[line.Id];
        const string FIELDS = "Test Sans,42.5,&H00FFFFFF&,&H00FFFFFF&,&H00000000&,&H80000000&,-1,0,-1,-1,100,100,1.25,0,1,3.5,4,2,12,24,36,1";

        Assert.Equal(FIELDS, exported.Fields);
        Assert.Equal("Style: Shared," + FIELDS + Environment.NewLine, output.ToString());
        Assert.Equal("Shared", exported.Name);
        Assert.Equal(default, exported.DialogueMargins);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(1.25, 4)]
    [InlineData(-2, 4)]
    [InlineData(1.25, -2)]
    [InlineData(-2, -2)]
    [InlineData(1.2345678906, 4.2345678906)]
    public void EncodedBaselineMatchesAStyleImportedWithoutBodyOverrides(double spacing, double shadow)
    {
        var style = new SubtitleStyle
        {
            FontFamily = "Test Sans", FontSize = 42.1234567896, LetterSpacing = spacing,
            Fill = new(0.18, 0.27, 0.36, 0.731), Stroke = new(0.31, 0.42, 0.53, 0.619),
            ShadowColor = new(0.64, 0.75, 0.86, 0.427), ShadowOffset = new(8, shadow),
            StrokeWidth = 3.1234567896, Bold = true, Italic = true, Underline = true, Strikethrough = true
        };
        var exported = AssExportStyle.Create("Shared", style);
        var imported = Assert.Single(AssSubtitleFormat.Parse(StyleSource(exported.Fields)).Lines).Style;
        var baseline = exported.Baseline;

        Assert.Equal(imported.FontFamily, baseline["fn"]);
        Assert.Equal(AssFormatValues.Number(imported.FontSize), baseline["fs"]);
        Assert.Equal(AssFormatValues.Number(imported.LetterSpacing), baseline["fsp"]);
        Assert.Equal(AssFormatValues.Number(imported.StrokeWidth), baseline["bord"]);
        Assert.Equal(AssFormatValues.Number(imported.ShadowOffset.X), baseline["xshad"]);
        Assert.Equal(AssFormatValues.Number(imported.ShadowOffset.Y), baseline["yshad"]);
        Assert.Equal("1", baseline["b"]);
        Assert.Equal("1", baseline["i"]);
        Assert.Equal("1", baseline["u"]);
        Assert.Equal("1", baseline["s"]);
        Assert.Equal(AssFormatValues.Color(imported.Fill, false), baseline["1c"]);
        Assert.Equal(AssFormatValues.Alpha(imported.Fill), baseline["1a"]);
        Assert.Equal(baseline["1c"], baseline["2c"]);
        Assert.Equal(baseline["1a"], baseline["2a"]);
        Assert.Equal(AssFormatValues.Color(imported.Stroke, false), baseline["3c"]);
        Assert.Equal(AssFormatValues.Alpha(imported.Stroke), baseline["3a"]);
        Assert.Equal(AssFormatValues.Color(imported.ShadowColor, false), baseline["4c"]);
        Assert.Equal(AssFormatValues.Alpha(imported.ShadowColor), baseline["4a"]);
        Assert.Equal(AssFormatValues.Alignment(imported.Alignment).ToString(CultureInfo.InvariantCulture), baseline["an"]);
    }

    [Fact]
    public void NativeResidualsDoNotBecomeInheritedStyleProperties()
    {
        var style = new SubtitleStyle
        {
            FontVariant = new() { Name = "SemiBold Italic", Weight = 600, Width = 7, Italic = true },
            FontAssetId = Guid.NewGuid(), LetterSpacing = -2, ShadowOffset = new(8, -2),
            FillBlur = 4, StrokeBlur = 5, ShadowBlur = 6, WrapMode = SubtitleWrapMode.NO_WRAP
        };
        var exported = AssExportStyle.Create("Shared", style);
        var baseline = exported.Baseline;

        Assert.Equal("0", baseline["b"]);
        Assert.Equal("0", baseline["i"]);
        Assert.Equal("0", baseline["fsp"]);
        Assert.Equal("0", baseline["xshad"]);
        Assert.Equal("0", baseline["yshad"]);
        Assert.Equal("0", baseline["blur"]);
        Assert.Equal("1", baseline["q"]);
        Assert.Equal("100", baseline["fscx"]);
        Assert.Equal("100", baseline["fscy"]);
        Assert.Equal("0", baseline["frz"]);
        Assert.Equal("0", baseline["frx"]);
        Assert.Equal("0", baseline["fry"]);
        Assert.Equal("0", exported.Fields.Split(',')[12]);
        Assert.Equal("0", exported.Fields.Split(',')[16]);
        Assert.Equal(new ScenePoint(8, -2), style.ShadowOffset);
        Assert.Equal(600, style.FontVariant!.Value.Weight);
    }

    [Fact]
    public void EqualDefinitionsReuseTheDescriptorAndKeepEveryLineMapping()
    {
        var first = new SubtitleLine { Text = "first", StyleName = "Shared" };
        var second = first with { Id = Guid.NewGuid(), Text = "second" };
        var output = new StringBuilder();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var mapping = AssStyleTable.Write([first, second], output, diagnostics);

        Assert.Equal(2, mapping.Count);
        Assert.Same(mapping[first.Id], mapping[second.Id]);
        Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(default, mapping[first.Id].DialogueMargins);
        Assert.Empty(diagnostics);
    }

    private static string StyleSource(string fields) => $"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 1920
        PlayResY: 1080
        WrapStyle: 1
        ScaledBorderAndShadow: yes
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Shared,{fields}
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Shared,,0,0,0,,text
        """;
}
