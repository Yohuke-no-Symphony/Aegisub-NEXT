using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssDrawingSnapshotTests
{
    [Fact]
    public void DrawingRunsKeepRawSourceRangesAndSnapshotStyleBeforeEachBody()
    {
        var source = @"{\p2\pbo3\fs20\fscx150\frz20}m 0 0 l 40 0 40 40 0 40{\fs30\fscx200} m 10 10 l 20 10 20 20{\p0\pos(100,80)\an7\org(120,90)}t";
        var parsed = Parser().Parse(source);

        Assert.Equal("t", parsed.Line.Text);
        Assert.Equal(2, parsed.Drawings.Length);
        var first = parsed.Drawings[0];
        var second = parsed.Drawings[1];
        Assert.Equal("m 0 0 l 40 0 40 40 0 40", source.Substring(first.SourceStart, first.SourceLength));
        Assert.Equal(" m 10 10 l 20 10 20 20", source.Substring(second.SourceStart, second.SourceLength));
        Assert.Equal(2, first.DrawingScale);
        Assert.Equal(3, first.BaselineOffset);
        Assert.Equal(20, first.Style.FontSize);
        Assert.Equal(new ScenePoint(1.5, 1), first.Scale);
        Assert.Equal(20, first.Rotation);
        Assert.Equal(30, second.Style.FontSize);
        Assert.Equal(new ScenePoint(2, 1), second.Scale);
        Assert.Equal(new ScenePoint(100, 80), parsed.Line.Style.Position!.Offset);
        Assert.Equal(TextAlignment.TOP_LEFT, parsed.Line.Style.Alignment);
        Assert.Equal(new ScenePoint(20, 10), parsed.Transform.Position);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.Drawing" or "Ass.RotationOrigin");
    }

    [Fact]
    public void DrawingBaselineOffsetDoesNotBecomeOrdinaryTextPlacement()
    {
        var parsed = Parser().Parse(@"{\pbo50\p1}m 0 0 l 10 0 10 10{\p0}text");

        Assert.Equal(50, Assert.Single(parsed.Drawings).BaselineOffset);
        Assert.Equal("text", parsed.Line.Text);
        Assert.Null(parsed.Line.Style.Position);
        Assert.Equal(default, parsed.Transform.Position);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    private static AssTextParser Parser()
    {
        var line = new SubtitleLine { Start = MediaTime.Zero, End = new(2), Style = new() { FontSize = 20 } };
        return new(line, new Dictionary<string, AssStyleDefinition>(), SceneColor.White);
    }
}
