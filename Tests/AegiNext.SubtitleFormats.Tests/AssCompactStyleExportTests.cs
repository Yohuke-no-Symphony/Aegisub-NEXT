using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

/// <summary>验证公开文件出口只覆盖共享样式表之外的必要差异。</summary>
public sealed class AssCompactStyleExportTests
{
    /// <summary>相同基础样式的普通文字直接引用一个样式表项，不展开正文。</summary>
    [Fact]
    public void PlainMainStyleProducesOnlyTextAndOneSharedStyle()
    {
        var line = SourceLine("plain Main");
        var second = line with { Id = Guid.NewGuid(), Start = new(4), End = new(8), Text = "second" };
        var document = Document(line, second);
        var written = AssSubtitleFormat.Write(document);
        Assert.Single(Styles(written.Text));
        Assert.Equal(new[] { "Main", "Main" }, Dialogues(written.Text).Select(row => row.Split(',', 10)[3]));
        Assert.Equal(new[] { "plain Main", "second" }, Dialogues(written.Text).Select(row => row.Split(',', 10)[9]));
        Assert.Equal(line, document.Subtitles[0]);
        Assert.Equal(second, document.Subtitles[1]);
    }

    /// <summary>只编辑导出样式表就能改变没有局部覆盖的文字。</summary>
    [Theory]
    [InlineData("Fontname", "Edited Family")]
    [InlineData("Fontsize", "60")]
    [InlineData("PrimaryColour", "&H000000FF")]
    [InlineData("Alignment", "3")]
    public void EditingOnlyTheStyleTableChangesUncoveredText(string field, string value)
    {
        var line = SourceLine("ABC");
        var written = AssSubtitleFormat.Write(Document(line));
        var edited = AssSubtitleFormat.Parse(EditStyle(written.Text, field, value), 640, 360);
        var changed = Assert.Single(edited.Lines);
        Assert.Equal("Main", changed.StyleName);
        Assert.Equal("ABC", changed.Text);
        foreach (var offset in Enumerable.Range(0, changed.Text.Length))
        {
            var style = StyleAt(changed, offset);
            switch (field)
            {
                case "Fontname":
                    Assert.Equal(value, style.FontFamily);
                    break;
                case "Fontsize":
                    Assert.Equal(60, style.FontSize);
                    break;
                case "PrimaryColour":
                    Assert.Equal(new SceneColor(1, 0, 0), style.Fill);
                    break;
                case "Alignment":
                    Assert.Equal(TextAlignment.BOTTOM_RIGHT, changed.Style.Alignment);
                    Assert.Null(changed.Style.Position);
                    break;
            }
        }
    }

    /// <summary>恢复基础样式不冻结旧字号，保留真正的局部字号覆盖。</summary>
    [Fact]
    public void FontSizeOverrideAndReturnToBaseFollowLaterStyleTableEdits()
    {
        var line = SourceLine(@"A{\fs40}B{\r}C");
        var written = AssSubtitleFormat.Write(Document(line));
        var changed = Assert.Single(AssSubtitleFormat.Parse(EditStyle(written.Text, "Fontsize", "60"), 640, 360).Lines);
        Assert.Equal(new double[] { 60, 40, 60 }, Enumerable.Range(0, 3).Select(offset => StyleAt(changed, offset).FontSize));
    }

    /// <summary>返回基础字体或颜色时继续继承表值，局部覆盖维持自身值。</summary>
    [Theory]
    [InlineData("Fontname", "Edited Family", @"A{\fnOther Family}B{\r}C")]
    [InlineData("PrimaryColour", "&H000000FF", @"A{\1c&HFF0000&}B{\r}C")]
    public void LocalOverrideRemainsIndependentWhenTheStyleTableChanges(string field, string value, string body)
    {
        var line = SourceLine(body);
        var written = AssSubtitleFormat.Write(Document(line));
        var changed = Assert.Single(AssSubtitleFormat.Parse(EditStyle(written.Text, field, value), 640, 360).Lines);
        if (field == "Fontname")
        {
            Assert.Equal(new[] { "Edited Family", "Other Family", "Edited Family" },
                Enumerable.Range(0, 3).Select(offset => StyleAt(changed, offset).FontFamily));
        }
        else
        {
            Assert.Equal(new[] { new SceneColor(1, 0, 0), new(0, 0, 1), new(1, 0, 0) },
                Enumerable.Range(0, 3).Select(offset => StyleAt(changed, offset).Fill));
        }
    }

    /// <summary>只在原生精度不同但导出编码相同时，不产生虚假的局部覆盖。</summary>
    [Fact]
    public void QuantizedColorAndAlphaUseTheExportedStyleAsTheirBaseline()
    {
        var line = SourceLine("ABC");
        line = line with
        {
            Style = line.Style with { Fill = new(0.18, 0.27, 0.36, 0.731) },
            InlineSpans = [new(1, 1, new() { Fill = new(0.18001, 0.27001, 0.36001, 0.73101) })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Equal("ABC", Body(written.Text).Replace(@"{\r}", string.Empty, StringComparison.Ordinal));
        Assert.Single(written.Diagnostics, item => item.Code == "Ass.ColorPrecision" && item.SubtitleId == line.Id);
        Assert.Single(written.Diagnostics, item => item.Code == "Ass.AlphaPrecision" && item.SubtitleId == line.Id);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(StyleAt(imported, 0).Fill, StyleAt(imported, 1).Fill);
        Assert.Equal(StyleAt(imported, 0).Fill, StyleAt(imported, 2).Fill);
    }

    /// <summary>九位小数编码相同的字号不额外展开，仍报告实际数字精度损失。</summary>
    [Fact]
    public void QuantizedNumbersUseTheExportedStyleAsTheirBaseline()
    {
        var line = SourceLine("ABC");
        line = line with
        {
            Style = line.Style with { FontSize = 64 + 1d / 3 },
            InlineSpans = [new(1, 1, new() { FontSize = 64.3333333334 })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Equal("ABC", Body(written.Text).Replace(@"{\r}", string.Empty, StringComparison.Ordinal));
        Assert.Single(written.Diagnostics, item => item.Code == "Ass.NumberPrecision" && item.SubtitleId == line.Id);
    }

    /// <summary>负字距与样式表单值阴影无法表达的轴差异保留必要标签。</summary>
    [Fact]
    public void NegativeSpacingAndAsymmetricShadowRemainNecessaryOverrides()
    {
        var line = SourceLine("ABC");
        line = line with
        {
            Style = line.Style with { ShadowColor = SceneColor.Black, ShadowOffset = new(-4, 3) },
            InlineSpans = [new(1, 1, new() { LetterSpacing = -1.25 })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains(@"\fsp-1.25", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(@"\xshad-4", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        Assert.Equal(new double[] { 0, -1.25, 0 }, Enumerable.Range(0, 3).Select(offset => StyleAt(imported, offset).LetterSpacing));
        Assert.All(Enumerable.Range(0, 3), offset => Assert.Equal(new ScenePoint(-4, 3), StyleAt(imported, offset).ShadowOffset));
    }

    /// <summary>布尔表字段不能替代精确字重和字体变体的有效斜体。</summary>
    [Fact]
    public void PreciseWeightAndVariantItalicRemainNecessaryOverrides()
    {
        var line = SourceLine("A");
        line = line with
        {
            Style = line.Style with
            {
                Bold = false, Italic = false,
                FontVariant = new() { Name = "SemiBold Italic", PostScriptName = "Fixture-SemiBoldItalic", Weight = 600, Width = 5, Italic = true }
            }
        };
        var written = AssSubtitleFormat.Write(Document(line));
        Assert.Contains(@"\b600", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(@"\i1", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.FontVariant");
        Assert.False(line.Style.Bold);
        Assert.False(line.Style.Italic);
    }

    /// <summary>局部样式返回基础样式后仍保留整句动画的事件相位。</summary>
    [Fact]
    public void ReturningToBaseStyleDoesNotCancelOrRestartTheAnimation()
    {
        var line = SourceLine(@"A{\fs40}B{\r}C");
        var document = Document(line);
        var layer = document.Layers[0] with
        {
            Tracks = [new(AnimationProperty.LETTER_SPACING, [new(MediaTime.Zero, 0), new(new(4), 12)])]
        };
        document = document with { Layers = [layer] };
        var written = AssSubtitleFormat.Write(document);
        var clip = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var importedLayer = layer with { Transform = clip.Transform, Tracks = clip.Tracks, AnimationOffset = clip.ContentOffset };
        foreach (var time in new[] { MediaTime.Zero, new(1, 4), new(1), new(5, 2), new(3999, 1000) })
        {
            var evaluated = SceneEvaluator.EvaluateLayer(importedLayer, clip.Line, clip.ContentOffset + time);
            foreach (var offset in Enumerable.Range(0, 3))
            {
                var style = SubtitleAnimationEvaluation.ApplyTypographyAnimations(evaluated, StyleAt(clip.Line, offset), offset);
                Assert.Equal(3d * time.Numerator / time.Denominator, style.LetterSpacing, 8);
            }
        }
        Assert.Equal(new double[] { 24, 40, 24 }, Enumerable.Range(0, 3).Select(offset => StyleAt(clip.Line, offset).FontSize));
    }

    /// <summary>离开卡拉 OK 范围时恢复普通颜色和透明度，不残留状态通道。</summary>
    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void KaraokeReturnsToOrdinaryStyleWithoutLeakingItsFill(KaraokeHighlightKind kind)
    {
        var line = SourceLine("ABC");
        line = line with
        {
            Style = line.Style with { Fill = new(0, 0, 1, 0.8) },
            Karaoke = [new(1, 1, new(1, 2), new(3, 2), new(0, 1, 0)) { HighlightKind = kind }],
            KaraokeStyleSpans = [new(1, 1, new() { Fill = new(0, 1, 0) }, new() { Fill = new(1, 0, 0, 0.4) })]
        };
        var written = AssSubtitleFormat.Write(Document(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);
        foreach (var offset in new[] { 0, 2 })
        {
            Assert.Equal(0, StyleAt(imported, offset).Fill.Red);
            Assert.Equal(0, StyleAt(imported, offset).Fill.Green);
            Assert.Equal(1, StyleAt(imported, offset).Fill.Blue);
            Assert.Equal(0.8, StyleAt(imported, offset).Fill.Alpha, 12);
        }
        var group = Assert.Single(imported.Karaoke);
        Assert.Equal((1, 1, new MediaTime(1, 2), new MediaTime(3, 2), kind),
            (group.Utf16Start, group.Utf16Length, group.Start, group.End, group.HighlightKind));
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset) => line.InlineSpans
        .FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static ProjectDocument Document(params SubtitleLine[] lines) => SubtitleFormatTestDocument.Create(lines) with { Width = 640, Height = 360 };

    private static string[] Styles(string source) => source.Split('\n').Where(row => row.StartsWith("Style: ", StringComparison.Ordinal)).ToArray();

    private static string[] Dialogues(string source) => source.Split('\n').Where(row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).Select(row => row.TrimEnd('\r')).ToArray();

    private static string Body(string source) => Assert.Single(Dialogues(source)).Split(',', 10)[9];

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

    private static SubtitleLine SourceLine(string body)
    {
        var source = "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\nYCbCr Matrix: None\nScaledBorderAndShadow: yes\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Main,Noto Sans,24,&H0000FF00,&H0000FF00,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,13,41,27,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:04.00,Main,,0,0,0,," + body;
        return Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Lines);
    }
}
