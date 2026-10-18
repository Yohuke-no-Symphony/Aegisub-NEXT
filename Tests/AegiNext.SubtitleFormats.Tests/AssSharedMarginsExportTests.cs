using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

/// <summary>验证共享 ASS 样式的边距基线和每条对白覆盖保持有效布局。</summary>
public sealed class AssSharedMarginsExportTests
{
    /// <summary>仅有差异的轴使用零样式基线，另外两轴仍由共享样式继承。</summary>
    [Theory]
    [InlineData(0, "13", "23")]
    [InlineData(1, "41", "51")]
    [InlineData(2, "27", "37")]
    public void EachChangingAxisUsesItsOwnZeroStyleBaseline(int axis, string firstValue, string secondValue)
    {
        var first = Line("first", new(13, 41, 27));
        var secondMargins = axis switch
        {
            0 => first.Style.Margins with { Left = 23 },
            1 => first.Style.Margins with { Right = 51 },
            _ => first.Style.Margins with { Vertical = 37 }
        };
        var second = Line("second", secondMargins) with { Start = new(2), End = new(4) };
        var document = Document(first, second);
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(StyleRows(written.Text));
        var dialogues = DialogueRows(written.Text);
        var expectedStyleMargins = new[] { "13", "41", "27" };
        expectedStyleMargins[axis] = "0";

        Assert.Equal(expectedStyleMargins, style.Skip(19).Take(3));
        Assert.Equal(firstValue, dialogues[0][5 + axis]);
        Assert.Equal(secondValue, dialogues[1][5 + axis]);
        for (var otherAxis = 0; otherAxis < 3; otherAxis++)
        {
            if (otherAxis != axis)
            {
                Assert.All(dialogues, row => Assert.Equal("0", row[5 + otherAxis]));
            }
        }
        Assert.All(dialogues, row => Assert.Equal("Shared", row[3]));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
        Assert.DoesNotContain(@"\pos", written.Text, StringComparison.Ordinal);
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        Assert.Equal(new[] { first.Style.Margins, second.Style.Margins }, imported.Lines.Select(line => line.Style.Margins));
        Assert.All(imported.Lines, line => Assert.Null(line.Style.Position));
        Assert.Same(first, document.Subtitles[0]);
        Assert.Same(second, document.Subtitles[1]);
    }

    /// <summary>零、小于一及格式化为零的边距不会继承另一行的正边距。</summary>
    [Theory]
    [InlineData(10, 0, "10", "0", false)]
    [InlineData(10, 0.5, "10", "0.5", false)]
    [InlineData(0, 0.5, "0", "0.5", false)]
    [InlineData(10, 0.0000000001, "10", "0", true)]
    public void SmallVariableMarginsKeepAZeroBaseline(double firstMargin, double secondMargin,
        string firstField, string secondField, bool secondLosesPrecision)
    {
        var first = Line("first", new(firstMargin, 41, 27));
        var second = Line("second", new(secondMargin, 41, 27)) with { Start = new(2), End = new(4) };
        var written = AssSubtitleFormat.Write(Document(first, second));
        var style = Assert.Single(StyleRows(written.Text));
        var dialogues = DialogueRows(written.Text);

        Assert.Equal("0", style[19]);
        Assert.Equal(new[] { firstField, secondField }, dialogues.Select(row => row[5]));
        Assert.All(dialogues, row => Assert.Equal("Shared", row[3]));
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        Assert.Equal(new[] { double.Parse(firstField, CultureInfo.InvariantCulture), double.Parse(secondField, CultureInfo.InvariantCulture) },
            imported.Lines.Select(line => line.Style.Margins.Left));
        Assert.Equal(secondLosesPrecision, written.Diagnostics.Any(diagnostic =>
            diagnostic.SubtitleId == second.Id && diagnostic.Code == "Ass.NumberPrecision"));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
    }

    /// <summary>最终序列化值相同即可共享样式基线，不以原始 double 差异创建覆盖。</summary>
    [Theory]
    [InlineData(0.5, 0.5, "0.5", false)]
    [InlineData(13.0000000001, 13.0000000002, "13", true)]
    [InlineData(0.0000000001, 0.0000000002, "0", true)]
    public void EqualSerializedMarginsRemainInTheSharedStyle(double firstMargin, double secondMargin,
        string expectedField, bool losesPrecision)
    {
        var first = Line("first", new(firstMargin, 41, 27));
        var second = Line("second", new(secondMargin, 41, 27)) with { Start = new(2), End = new(4) };
        var written = AssSubtitleFormat.Write(Document(first, second));
        var style = Assert.Single(StyleRows(written.Text));

        Assert.Equal(expectedField, style[19]);
        Assert.All(DialogueRows(written.Text), row => Assert.Equal(new[] { "0", "0", "0" }, row.Skip(5).Take(3)));
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        Assert.All(imported.Lines, line => Assert.Equal(double.Parse(expectedField, CultureInfo.InvariantCulture), line.Style.Margins.Left));
        foreach (var line in new[] { first, second })
        {
            Assert.Equal(losesPrecision, written.Diagnostics.Any(diagnostic =>
                diagnostic.SubtitleId == line.Id && diagnostic.Code == "Ass.NumberPrecision"));
        }
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
    }

    /// <summary>分组及边距比较采用画布像素，原行参考单位和属性快照保持不变。</summary>
    [Fact]
    public void SharedMarginsAreComputedAfterCanvasProjection()
    {
        var first = Line("first", new(13, 41, 27));
        var second = Line("second", new(13, 41, 27)) with
        {
            Start = new(2), End = new(4),
            Style = first.Style with
            {
                Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 },
                FontSize = 10, StrokeWidth = 1, ShadowOffset = new(1, 1)
            }
        };
        var document = Document(first, second);
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(StyleRows(written.Text));
        var dialogues = DialogueRows(written.Text);

        Assert.Equal(new[] { "0", "0", "0" }, style.Skip(19).Take(3));
        Assert.Equal(new[] { "13", "41", "27" }, dialogues[0].Skip(5).Take(3));
        Assert.Equal(new[] { "26", "82", "54" }, dialogues[1].Skip(5).Take(3));
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        Assert.Equal(new[] { new SubtitleMargins(13, 41, 27), new(26, 82, 54) }, imported.Lines.Select(line => line.Style.Margins));
        Assert.All(imported.Lines, line => Assert.Equal(20, line.Style.FontSize));
        Assert.Same(first, document.Subtitles[0]);
        Assert.Same(second, document.Subtitles[1]);
        Assert.Equal(new SubtitleMargins(13, 41, 27), second.Style.Margins);
        Assert.Equal(320, second.Style.Scaling.ReferenceWidth);
    }

    /// <summary>边距合并不会合并其他导出字段不同的定义，也不会占用既有样式名称。</summary>
    [Fact]
    public void NonMarginConflictsKeepStableReservedSuffixes()
    {
        var first = Line("first", new(13, 41, 27));
        var changed = Line("changed", new(41, 13, 27)) with
        {
            Start = new(2), End = new(4), Style = first.Style with { FontSize = 21, Margins = new(41, 13, 27) }
        };
        var reserved = Line("reserved", new(17, 19, 23)) with
        {
            Start = new(4), End = new(6), StyleName = "Shared_2"
        };
        var document = Document(first, changed, reserved);
        var written = AssSubtitleFormat.Write(document);

        Assert.Equal(new[] { "Shared", "Shared_3", "Shared_2" }, StyleRows(written.Text).Select(row => row[0]));
        Assert.Equal(new[] { "Shared", "Shared_3", "Shared_2" }, DialogueRows(written.Text).Select(row => row[3]));
        Assert.Single(written.Diagnostics, diagnostic => diagnostic.SubtitleId == changed.Id && diagnostic.Code == "Ass.StyleName");
        Assert.Equal(written.Text, AssSubtitleFormat.Write(document).Text);
    }

    /// <summary>不同名称仍代表不同输出样式，即使属性仅边距不同。</summary>
    [Fact]
    public void DifferentNamesKeepSeparateStyles()
    {
        var first = Line("first", new(13, 41, 27));
        var second = Line("second", new(41, 13, 27)) with { Start = new(2), End = new(4), StyleName = "Other" };
        var written = AssSubtitleFormat.Write(Document(first, second));

        Assert.Equal(new[] { "Shared", "Other" }, StyleRows(written.Text).Select(row => row[0]));
        Assert.All(DialogueRows(written.Text), row => Assert.Equal(new[] { "0", "0", "0" }, row.Skip(5).Take(3)));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
    }

    /// <summary>蒙版展开的全部事件沿用所属行的同一组有效边距。</summary>
    [Fact]
    public void ExpandedMaskSamplesReuseTheLineDialogueMargins()
    {
        var first = Line("first", new(13, 41, 27)) with { End = new(1) };
        var second = Line("second", new(41, 41, 27)) with { Start = new(1), End = new(2) };
        var document = Document(first, second) with { FrameRate = new(2, 1) };
        var maskedLayer = document.Layers[0] with
        {
            Mask = new RectangleClipMask
            {
                BottomRight = new(40, 60), ReferenceFrame = new() { Size = new(40, 60) },
                Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) }
            },
            Tracks = [new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(20, 10))])]
        };
        document = document with { Layers = document.Layers.SetItem(0, maskedLayer) };
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(StyleRows(written.Text));
        var dialogues = DialogueRows(written.Text);
        var firstSamples = dialogues.Where(row => row[9].EndsWith("first", StringComparison.Ordinal)).ToArray();
        var secondSample = Assert.Single(dialogues, row => row[9].EndsWith("second", StringComparison.Ordinal));

        Assert.Equal(new[] { "0", "41", "27" }, style.Skip(19).Take(3));
        Assert.Equal(2, firstSamples.Length);
        Assert.All(firstSamples, row => Assert.Equal(new[] { "13", "0", "0" }, row.Skip(5).Take(3)));
        Assert.Equal(new[] { "41", "0", "0" }, secondSample.Skip(5).Take(3));
        Assert.All(dialogues, row => Assert.Equal("Shared", row[3]));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360);
        Assert.All(imported.Lines.Where(line => line.Text == "first"), line => Assert.Equal(first.Style.Margins, line.Style.Margins));
        Assert.Equal(second.Style.Margins, Assert.Single(imported.Lines, line => line.Text == "second").Style.Margins);
    }

    /// <summary>共享输出基线不会污染局部轴心补偿所需的真实排版测量。</summary>
    [Fact]
    public void PlacementMeasurementReceivesTheEffectiveLineMargins()
    {
        var first = Line("first", new(13, 41, 27));
        first = first with
        {
            Style = first.Style with { Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.2, 0.3) } }
        };
        var second = first with
        {
            Id = Guid.NewGuid(), Text = "second", Start = new(2), End = new(4),
            Style = first.Style with { Margins = new(41, 13, 27) }
        };
        var document = Document(first, second);
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(320, 180), new(50, 40), new(10, 20), new(100, 60)));
        var written = AssSubtitleFormat.Write(document, placementMeasurer: measurer);

        Assert.Single(StyleRows(written.Text));
        Assert.Equal(new[] { "0", "0", "27" }, StyleRows(written.Text)[0].Skip(19).Take(3));
        Assert.Same(document, measurer.Document);
        var measured = Assert.IsType<SubtitleLine>(measurer.Line);
        Assert.Equal(second.Id, measured.Id);
        Assert.Equal(second.Style.Margins, measured.Style.Margins);
        Assert.Equal(second.Style.Position, measured.Style.Position);
        Assert.Equal(new SubtitleMargins(13, 41, 27), first.Style.Margins);
        Assert.Equal(new SubtitleMargins(41, 13, 27), second.Style.Margins);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Ass.StyleName" or "Ass.PlacementMeasurement");
    }

    private static SubtitleLine Line(string text, SubtitleMargins margins) => new()
    {
        Text = text, End = new(2), StyleName = "Shared",
        Style = new()
        {
            Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
            FontFamily = "Noto Sans", FontSize = 20, ShadowBlur = 0, Margins = margins
        }
    };

    private static ProjectDocument Document(params SubtitleLine[] lines) =>
        SubtitleFormatTestDocument.Create(lines) with { Width = 640, Height = 360 };

    private static string[][] StyleRows(string source) => source.Split('\n')
        .Where(row => row.StartsWith("Style: ", StringComparison.Ordinal))
        .Select(row => row.TrimEnd('\r')[7..].Split(',')).ToArray();

    private static string[][] DialogueRows(string source) => source.Split('\n')
        .Where(row => row.StartsWith("Dialogue: ", StringComparison.Ordinal))
        .Select(row => row.TrimEnd('\r').Split(',', 10)).ToArray();
}
