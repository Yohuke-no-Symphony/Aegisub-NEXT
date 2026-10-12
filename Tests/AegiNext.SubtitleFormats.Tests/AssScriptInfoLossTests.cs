using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssScriptInfoLossTests
{
    [Theory]
    [InlineData("TV.601")]
    [InlineData("PC.601")]
    [InlineData("TV.709")]
    [InlineData("PC.709")]
    [InlineData("TV.240M")]
    [InlineData("PC.240M")]
    [InlineData("TV.FCC")]
    [InlineData("PC.FCC")]
    [InlineData("  tv.709  ")]
    public void ExplicitVideoColorMatchingReportsOneScriptLevelLoss(string matrix)
    {
        var source = Script("YCbCr Matrix: " + matrix);
        var result = AssSubtitleFormat.Parse(source, 640, 360);
        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Code == "Ass.YCbCrMatrix"));

        Assert.Null(diagnostic.SubtitleId);
        Assert.Equal(0, diagnostic.SourceStart);
        Assert.Equal(0, diagnostic.SourceLength);
        Assert.Contains("RGB", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("may differ", diagnostic.Message, StringComparison.Ordinal);
        var baseline = AssSubtitleFormat.Parse(Script("YCbCr Matrix: None"), 640, 360);
        Assert.Equal(baseline.Lines[0].Start, result.Lines[0].Start);
        Assert.Equal(baseline.Lines[0].End, result.Lines[0].End);
        Assert.Equal(baseline.Lines[0].Style, result.Lines[0].Style);
        Assert.Equal(baseline.Lines[0].InlineSpans.ToArray(), result.Lines[0].InlineSpans.ToArray());
        Assert.Equal(baseline.Lines[0].Text, result.Lines[0].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("YCbCr Matrix:")]
    [InlineData("YCbCr Matrix: None")]
    [InlineData("YCbCr Matrix:  nOnE  ")]
    [InlineData("YCbCr Matrix: unknown")]
    [InlineData("YCbCr Matrix: TV.2020")]
    [InlineData("YCbCr Matrix: TV.601\nYCbCr Matrix: None")]
    [InlineData("Timer: 50\nCollisions: Reverse")]
    public void MissingDirectOrUnrecognizedMatrixDoesNotAddConversionLoss(string info)
    {
        var result = AssSubtitleFormat.Parse(Script(info), 640, 360);
        var baseline = AssSubtitleFormat.Parse(Script(""), 640, 360);

        Assert.Equal(baseline.Diagnostics.Select(item => item.Code), result.Diagnostics.Select(item => item.Code));
        Assert.Equal(baseline.Lines[0].Start, result.Lines[0].Start);
        Assert.Equal(baseline.Lines[0].End, result.Lines[0].End);
    }

    [Fact]
    public void RepeatedHeaderUsesTheLastEffectiveMatrixAndOnlyReportsOnce()
    {
        var result = AssSubtitleFormat.Parse(Script("YCbCr Matrix: None\nYCbCr Matrix: TV.601\nYCbCr Matrix: PC.709"), 640, 360);

        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Code == "Ass.YCbCrMatrix"));
        Assert.Contains("PC.709", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,")]
    public void FractionalTransformTimesUseTheSameIntegerMillisecondsWithoutAdditionalLoss(string acceleration)
    {
        var fractional = AssSubtitleFormat.Parse(Script("", "{\\t(100.9,200.9," + acceleration + "\\fs48)}a"), 640, 360);
        var integer = AssSubtitleFormat.Parse(Script("", "{\\t(100,200," + acceleration + "\\fs48)}a"), 640, 360);

        Assert.NotEmpty(integer.Clips[0].Tracks.SelectMany(track => track.Transforms));
        Assert.Equal(integer.Diagnostics.Select(item => item.Code), fractional.Diagnostics.Select(item => item.Code));
        Assert.Equal(integer.Clips[0].Tracks.SelectMany(track => track.Transforms).Select(item => (item.Start, item.End)),
            fractional.Clips[0].Tracks.SelectMany(track => track.Transforms).Select(item => (item.Start, item.End)));
    }

    private static string Script(string info, string text = @"{\1c&H123456&}a") => $"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        WrapStyle: 1
        ScaledBorderAndShadow: yes
        {info}
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour
        Style: Default,Test Sans,24,&H00FFFFFF
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{text}
        """;
}
