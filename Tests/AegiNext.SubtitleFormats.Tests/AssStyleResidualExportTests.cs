using AegiNext.Core.Projects;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

/// <summary>验证共享样式表无法承载的有符号外观及独立颜色分量仍作为必要差量保留。</summary>
public sealed class AssStyleResidualExportTests
{
    /// <summary>负字距与有符号阴影不能被表内的非负标量基线覆盖。</summary>
    [Theory]
    [InlineData(8, 2, 1, 0)]
    [InlineData(-2, -2, 1, 0)]
    [InlineData(2, -2, 1, 0)]
    [InlineData(8, 2, 2, 23)]
    [InlineData(-2, -2, 2, 23)]
    [InlineData(2, -2, 2, 23)]
    public void SignedBaseAppearanceSurvivesTheStyleTableAndTransformCompensation(double x, double y, double scale, double rotation)
    {
        var line = Line() with { Style = Line().Style with { LetterSpacing = -2, ShadowOffset = new(x, y) } };
        var document = Document(line);
        document = document with
        {
            Layers = [document.Layers[0] with { Transform = new() { Scale = new(scale, scale), Rotation = rotation } }]
        };
        var written = AssSubtitleFormat.Write(document);
        var fields = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal)).Split(',');
        Assert.Equal("0", fields[13]);
        Assert.True(double.Parse(fields[17], System.Globalization.CultureInfo.InvariantCulture) >= 0);
        Assert.Contains(@"\fsp-2", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        var style = StyleAt(imported, 0);
        Assert.Equal(-2, style.LetterSpacing);
        Assert.Equal(x, style.ShadowOffset.X, 8);
        Assert.Equal(y, style.ShadowOffset.Y, 8);
        Assert.Equal(new ScenePoint(x, y), line.Style.ShadowOffset);
        Assert.Equal(-2, line.Style.LetterSpacing);
    }

    /// <summary>RGB 与透明度独立编码，只有改变的颜色分量需要行内覆盖。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyTheChangedColorComponentIsEmitted(bool rgb)
    {
        var target = rgb ? new SceneColor(1, 0, 0) : new SceneColor(1, 1, 1, 0.4);
        var line = Line() with { InlineSpans = [new(1, 1, new() { Fill = target })] };
        var written = AssSubtitleFormat.Write(Document(line));
        var body = Body(written.Text);
        Assert.Contains(rgb ? @"\1c" : @"\1a", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rgb ? @"\1a" : @"\1c", body, StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(SceneColor.White, StyleAt(imported, 0).Fill);
        Assert.Equal(target, StyleAt(imported, 1).Fill);
        Assert.Equal(SceneColor.White, StyleAt(imported, 2).Fill);
    }

    /// <summary>省略表内字体标签也不能绕过原有的字体控制字符检查。</summary>
    [Theory]
    [InlineData("Bad\\Family")]
    [InlineData("Bad{Family")]
    [InlineData("Bad}Family")]
    public void InheritedFontControlCharactersAreStillRejected(string family)
    {
        var line = Line() with { Style = Line().Style with { FontFamily = family } };
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(Document(line)));
    }

    /// <summary>模糊差量仍按真正输出值报告播放器范围限制。</summary>
    [Fact]
    public void CompactBlurRetainsItsRangeDiagnostic()
    {
        var line = Line() with { Style = Line().Style with { FillBlur = 200 } };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains(@"\blur", Body(written.Text), StringComparison.Ordinal);
        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        Assert.Equal(200, line.Style.FillBlur);
    }

    private static SubtitleLine Line() => new()
    {
        Text = "ABC", StyleName = "Main", Style = new()
        {
            FontFamily = "Noto Sans", FontSize = 24, Fill = SceneColor.White, StrokeWidth = 0,
            ShadowColor = SceneColor.Black, ShadowOffset = default, ShadowBlur = 0,
            Alignment = TextAlignment.TOP_LEFT, Position = new() { Anchor = default, Pivot = default, Offset = new(200, 180) },
            Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }
        }
    };

    private static ProjectDocument Document(SubtitleLine line) => SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360 };

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset) => line.InlineSpans
        .FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static string Body(string source) => Assert.Single(source.Split('\n'), row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).Split(',', 10)[9];
}
