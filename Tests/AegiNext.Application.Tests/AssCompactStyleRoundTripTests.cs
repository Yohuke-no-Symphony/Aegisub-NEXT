using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

/// <summary>验证公开文件交换及工程保存后仍保持基础样式继承和必要字体差异。</summary>
public sealed class AssCompactStyleRoundTripTests
{
    /// <summary>工程保存读取不使纯文本展开为完整样式覆盖。</summary>
    [Fact]
    public void ProjectPersistenceKeepsPureTextDependentOnItsSharedStyle()
    {
        var parsed = AssSubtitleFormat.Parse(Source("ABC"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Compact ASS");
        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);
        var written = AssSubtitleFormat.Write(restored);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: Main,", StringComparison.Ordinal));
        Assert.Equal("ABC", Body(written.Text));
        var line = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal("Main", line.StyleName);
        Assert.Equal("ABC", line.Text);
        Assert.Equal(24, StyleAt(line, 0).FontSize);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
    }

    /// <summary>保存后的字号覆盖不冻结其他基础属性，返回基础文字继续继承样式表。</summary>
    [Theory]
    [InlineData("Fontname", "Edited Family")]
    [InlineData("Fontsize", "60")]
    [InlineData("PrimaryColour", "&H000000FF")]
    [InlineData("Alignment", "3")]
    public void EditingTheExportedStyleAfterProjectPersistencePreservesOnlyRealOverrides(string field, string value)
    {
        var parsed = AssSubtitleFormat.Parse(Source(@"A{\fs40}B{\r}C"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Compact ASS");
        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);
        var original = Assert.Single(document.Subtitles);
        var persisted = Assert.Single(restored.Subtitles);
        Assert.Equal(original.InlineSpans.ToArray(), persisted.InlineSpans.ToArray());
        var written = AssSubtitleFormat.Write(restored);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        var edited = AssSubtitleFormat.Parse(EditStyle(written.Text, field, value), 640, 360);
        var line = Assert.Single(edited.Lines);
        Assert.Equal("Main", line.StyleName);
        Assert.Equal(original.Start, line.Start);
        Assert.Equal(original.End, line.End);
        Assert.Equal(original.Text, line.Text);
        Assert.Equal(original.Style.Margins, line.Style.Margins);
        Assert.Equal(field == "Fontsize" ? new double[] { 60, 40, 60 } : new double[] { 24, 40, 24 },
            Enumerable.Range(0, 3).Select(offset => StyleAt(line, offset).FontSize));
        foreach (var offset in Enumerable.Range(0, 3))
        {
            var style = StyleAt(line, offset);
            if (field == "Fontname")
            {
                Assert.Equal("Edited Family", style.FontFamily);
            }
            if (field == "PrimaryColour")
            {
                Assert.Equal(new SceneColor(1, 0, 0), style.Fill);
            }
        }
        Assert.Equal(field == "Alignment" ? TextAlignment.BOTTOM_RIGHT : TextAlignment.TOP_LEFT, line.Style.Alignment);
        Assert.Null(line.Style.Position);
    }

    /// <summary>精简出口仍保留表外精确字重、有效斜体及保存的字体变体身份。</summary>
    [Fact]
    public void ExactWeightsAndVariantItalicSurvivePersistenceAndCompactFileOutput()
    {
        var semiBold = new SubtitleFontVariant { Name = "Fixture SemiBold Italic", PostScriptName = "Fixture-SemiBoldItalic", Weight = 600, Width = 5, Italic = true };
        var black = new SubtitleFontVariant { Name = "Fixture Black Italic", PostScriptName = "Fixture-BlackItalic", Weight = 900, Width = 5, Italic = true };
        var resolver = new RecordingAssFontWeightResolver(("Noto Sans", semiBold), ("Noto Sans", black));
        var parsed = AssSubtitleFormat.Parse(Source(@"{\b600\i1}A{\b900}B{\r\b600\i1}C"), 640, 360, resolver);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "Compact ASS weights");
        var bytes = ProjectStore.Serialize(document);
        var restored = ProjectStore.Deserialize(bytes);
        var written = AssSubtitleFormat.Write(restored);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        var roundTripped = AssSubtitleFormat.Parse(written.Text, 640, 360, resolver);
        var line = Assert.Single(roundTripped.Lines);
        Assert.Equal(new[] { semiBold, black, semiBold }, Enumerable.Range(0, 3).Select(offset => StyleAt(line, offset).FontVariant!.Value));
        Assert.All(Enumerable.Range(0, 3), offset => Assert.True(StyleAt(line, offset).Italic));
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        Assert.DoesNotContain(roundTripped.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontVariant");
        Assert.Equal("Main", line.StyleName);
        Assert.Equal("ABC", line.Text);
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset) => line.InlineSpans
        .FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static string Body(string source) => source.Split('\n').Single(row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).TrimEnd('\r').Split(',', 10)[9];

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

    private static string Source(string body)
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\nYCbCr Matrix: None\nScaledBorderAndShadow: yes\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Main,Noto Sans,24,&H0000FF00,&H0000FF00,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,13,41,27,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:04.00,Main,,0,0,0,," + body;
    }
}
