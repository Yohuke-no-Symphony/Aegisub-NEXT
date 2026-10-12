using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class SubtitleTextFormatTests
{
    [Fact]
    public void SrtPreservesMultilineUnicodeAndMillisecondTiming()
    {
        var text = "\uFEFF1\r\n00:01:02,003 --> 00:01:04,567\r\n第一行\r\nمرحبا 😀\r\n\r\n2\r\n01:00:00,000 --> 01:00:00,001\r\n尾\r\n";
        var imported = SubtitleTextFormat.ParseSrt(text);
        Assert.Equal(2, imported.Length);
        Assert.Equal(new MediaTime(62003, 1000), imported[0].Start);
        Assert.Equal("第一行\nمرحبا 😀", imported[0].Text);
        var again = SubtitleTextFormat.ParseSrt(SubtitleTextFormat.WriteSrt(imported));
        Assert.Equal(imported.Select(line => (line.Start, line.End, line.Text)), again.Select(line => (line.Start, line.End, line.Text)));
    }

    [Theory]
    [InlineData("x\n00:00:00,000 --> 00:00:01,000\ntext")]
    [InlineData("1\n00:60:00,000 --> 00:61:00,000\ntext")]
    [InlineData("1\n00:00:02,000 --> 00:00:01,000\ntext")]
    [InlineData("1\n00:00:00,000 --> 00:00:01,000\n")]
    [InlineData("1\n00:00:00,000 --> 00:00:01,000 X1:0\ntext")]
    public void MalformedSrtIsReportedRatherThanPartiallyImported(string text)
    {
        Assert.Throws<InvalidDataException>(() => SubtitleTextFormat.ParseSrt(text));
    }

    [Fact]
    public void SrtQuantizesExplicitlyAndPreservesSubMillisecondNonemptyInterval()
    {
        SubtitleLine[] lines = [new() { Start = new(1, 3000), End = new(2, 3000), Text = "short" }];
        var result = Assert.Single(SubtitleTextFormat.ParseSrt(SubtitleTextFormat.WriteSrt(lines)));
        Assert.Equal(MediaTime.Zero, result.Start);
        Assert.Equal(new MediaTime(1, 1000), result.End);
        Assert.Throws<InvalidDataException>(() => SubtitleTextFormat.WriteSrt([lines[0] with { Start = new(-1) }]));
    }

    [Fact]
    public void TextImportUsesRequestedExactTimingAndKeepsTextOnlyOnExport()
    {
        var imported = SubtitleTextFormat.ImportText("\n one\n\ntwo 😀\n", new(1, 3), new(5, 7));
        Assert.Equal(2, imported.Length);
        Assert.Equal(new MediaTime(5, 7), imported[0].Start);
        Assert.Equal(new MediaTime(29, 21), imported[1].End);
        Assert.Equal(" one\ntwo 😀", SubtitleTextFormat.WriteText(imported));
    }
}
