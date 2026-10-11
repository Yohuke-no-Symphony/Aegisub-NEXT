using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleFormatLossAnalysisTests
{
    [Fact]
    public void SrtGraphicLossesRetainEachAffectedClipIdentity()
    {
        var first = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10), End = new(2), Blur = 1 };
        var second = first with { Id = Guid.NewGuid(), Start = new(2), End = new(4) };

        var diagnostics = SubtitleFormatLossAnalysis.ForSrt(new() { Layers = [first, second] });

        Assert.Equal([first.Id, second.Id], diagnostics.Where(item => item.Code == "Subtitle.Composition").Select(item => item.LayerId!.Value));
        Assert.Equal([first.Id, second.Id], diagnostics.Where(item => item.Code == "Srt.Blur").Select(item => item.LayerId!.Value));
        Assert.All(diagnostics, item => Assert.Null(item.SubtitleId));
    }
}
