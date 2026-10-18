using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>固定字体下以独立 libass 验证共享样式编辑、必要覆盖和普通文字恢复。</summary>
public sealed class AssStyleInheritanceReferenceTests
{
    /// <summary>仅修改实际出口的样式表，未覆盖文字随字号、颜色和对齐变化。</summary>
    [LibassReferenceTheory]
    [InlineData("Fontsize", "64")]
    [InlineData("PrimaryColour", "&H000000FF")]
    [InlineData("Alignment", "3")]
    public void EditingOnlyTheExportedStyleChangesUncoveredInk(string field, string value)
    {
        var source = Source("Wi");
        var exported = ExportThroughPersistence(source);
        var edited = EditStyle(exported, field, value);
        var canonical = Source("Wi", fontSize: field == "Fontsize" ? "64" : "48",
            primary: field == "PrimaryColour" ? "&H000000FF" : "&H0000FF00",
            alignment: field == "Alignment" ? "3" : "7");
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 733, 3999 })
        {
            var original = reference.Render(source, time);
            var expected = reference.Render(canonical, time);
            var actual = reference.Render(edited, time);
            AssertCoverage(expected, actual, time);
            var difference = 0d;
            for (var index = 0; index < expected.Pixels.Length; index++)
            {
                difference += Math.Abs(original.Pixels[index] - expected.Pixels[index]);
            }
            Assert.True(difference / expected.Energy(3) > 0.1, "The independent Style edit must visibly change the fixture.");
        }
    }

    /// <summary>字号表编辑改变基础文字，同时保留局部字号及返回基础的继承。</summary>
    [LibassReferenceFact]
    public void FontSizeTableEditKeepsTheLocalOverrideAndUpdatesTheReturnedBase()
    {
        const string body = @"A{\fs40}B{\r}C";
        var source = Source(body, fontSize: "24");
        var edited = EditStyle(ExportThroughPersistence(source), "Fontsize", "60");
        AssertReferenceCoverage(Source(body, fontSize: "60"), edited, [0, 733, 3999]);
    }

    /// <summary>颜色表编辑改变未覆盖文字，局部蓝色范围继续保持自身颜色。</summary>
    [LibassReferenceFact]
    public void FillTableEditKeepsTheLocalOverrideAndUpdatesTheReturnedBase()
    {
        const string body = @"A{\1c&HFF0000&}B{\r}C";
        var source = Source(body);
        var edited = EditStyle(ExportThroughPersistence(source), "PrimaryColour", "&H000000FF");
        AssertReferenceCoverage(Source(body, primary: "&H000000FF"), edited, [0, 733, 3999]);
    }

    /// <summary>基础样式恢复后的字距动画仍使用原事件的连续时钟。</summary>
    [LibassReferenceFact]
    public void NecessaryResetsDoNotCancelOrRestartAnimationInLaterText()
    {
        var source = Source(@"{\fsp0\t(0,4000,\fsp12)}A{\fs40}B{\r\fsp0\t(0,4000,\fsp12)}C");
        var exported = ExportThroughPersistence(source);
        AssertReferenceCoverage(source, exported, [0, 499, 500, 733, 1000, 1500, 2500, 3999]);
    }

    /// <summary>三个卡拉 OK 模式离开局部范围后都恢复普通主次颜色和 alpha。</summary>
    [LibassReferenceTheory]
    [InlineData("k")]
    [InlineData("kf")]
    [InlineData("ko")]
    public void OrdinaryTextAfterKaraokeDoesNotKeepHighlightOrInactiveChannels(string tag)
    {
        var source = Source(@"A{\1c&H00FF00&\1a&H00&\2c&H0000FF&\2a&H99&}{\k50}{\" + tag + @"100}MM{\r\k0}C",
            primary: "&H33FF0000", secondary: "&H33FF0000");
        var exported = ExportThroughPersistence(source);
        AssertReferenceCoverage(source, exported, [0, 499, 500, 733, 1000, 1499, 1500, 2500, 3999]);
    }

    /// <summary>同名样式共享后，零、小数及编码成零的边距不意外继承另一句的非零值。</summary>
    [LibassReferenceTheory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("0.0000000001")]
    public void SharedMarginStylePreservesTheIndependentPerStyleLibassMeaning(string secondMargin)
    {
        var source = MarginSource(secondMargin);
        var parsed = AssSubtitleFormat.Parse(source, 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Shared margin reference");
        document = document with { Subtitles = document.Subtitles.Select(line => line with { StyleName = "Main" }).ToImmutableArray() };
        var expectedMargin = double.Parse(secondMargin, CultureInfo.InvariantCulture);
        Assert.Equal(new[] { 10d, expectedMargin }, document.Subtitles.Select(line => line.Style.Margins.Left));
        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);
        var written = AssSubtitleFormat.Write(restored);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        var style = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal));
        Assert.StartsWith("Style: Main,", style, StringComparison.Ordinal);
        Assert.Equal("0", style.Split(',')[19]);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
        if (secondMargin == "0.0000000001")
        {
            Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.NumberPrecision");
        }
        AssertReferenceCoverage(source, written.Text, [0, 733, 1999, 2000, 2500, 3999]);
    }

    private static string ExportThroughPersistence(string source)
    {
        var parsed = AssSubtitleFormat.Parse(source, 640, 360);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Style inheritance reference");
        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);
        var written = AssSubtitleFormat.Write(restored);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Ass.StyleName" or "Subtitle.Composition");
        return written.Text;
    }

    private static void AssertReferenceCoverage(string expectedSource, string actualSource, long[] times)
    {
        using var reference = new LibassReferenceRenderer();
        foreach (var time in times)
        {
            AssertCoverage(reference.Render(expectedSource, time), reference.Render(actualSource, time), time);
        }
    }

    private static void AssertCoverage(SubtitleReferenceFrame expected, SubtitleReferenceFrame actual, long time)
    {
        var expectedEnergy = expected.Energy(3);
        var actualEnergy = actual.Energy(3);
        Assert.True(expectedEnergy > 100 && actualEnergy > 100, $"Empty reference ink at {time} ms.");
        Assert.InRange(Math.Abs(actualEnergy / expectedEnergy - 1), 0, 0.02);
        Assert.InRange(Math.Abs(actual.MaximumAlpha - expected.MaximumAlpha), 0, 0.005);
        for (var channel = 0; channel < 4; channel++)
        {
            var difference = 0d;
            for (var index = channel; index < expected.Pixels.Length; index += 4)
            {
                difference += Math.Abs(actual.Pixels[index] - expected.Pixels[index]);
            }
            Assert.InRange(difference / expectedEnergy, 0, 0.02);
        }
        var expectedBounds = expected.InkBounds();
        var actualBounds = actual.InkBounds();
        Assert.InRange(Math.Abs(actualBounds.Left - expectedBounds.Left), 0, 1);
        Assert.InRange(Math.Abs(actualBounds.Top - expectedBounds.Top), 0, 1);
        Assert.InRange(Math.Abs(actualBounds.Right - expectedBounds.Right), 0, 1);
        Assert.InRange(Math.Abs(actualBounds.Bottom - expectedBounds.Bottom), 0, 1);
    }

    private static string EditStyle(string source, string field, string value)
    {
        var rows = source.Split('\n');
        var start = Array.FindIndex(rows, row => row.Trim() == "[V4+ Styles]");
        var format = rows.Skip(start + 1).First(row => row.StartsWith("Format: ", StringComparison.Ordinal))[8..]
            .Split(',', StringSplitOptions.TrimEntries);
        var index = Array.IndexOf(format, field);
        Assert.True(index >= 0);
        var style = Array.FindIndex(rows, start + 1, row => row.StartsWith("Style: Main,", StringComparison.Ordinal));
        Assert.True(style > start);
        var fields = rows[style][7..].TrimEnd('\r').Split(',');
        fields[index] = value;
        rows[style] = "Style: " + string.Join(',', fields);
        return string.Join('\n', rows);
    }

    private static string Source(string body, string fontSize = "48", string primary = "&H0000FF00",
        string secondary = "&H0000FF00", string alignment = "7")
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\nYCbCr Matrix: None\nScaledBorderAndShadow: yes\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            $"Style: Main,Noto Sans,{fontSize},{primary},{secondary},&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,{alignment},32,32,32,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:04.00,Main,,0,0,0,," + body;
    }

    private static string MarginSource(string secondMargin)
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\nYCbCr Matrix: None\nScaledBorderAndShadow: yes\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: A,Noto Sans,48,&H0000FF00,&H0000FF00,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,10,32,32,1\n" +
            $"Style: B,Noto Sans,48,&H0000FF00,&H0000FF00,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,{secondMargin},32,32,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,A,,0,0,0,,Wi\nDialogue: 0,0:00:02.00,0:00:04.00,B,,0,0,0,,Wi";
    }
}
