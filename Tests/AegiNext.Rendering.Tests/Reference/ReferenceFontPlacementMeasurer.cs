using AegiNext.Core.Projects;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

internal sealed class ReferenceFontPlacementMeasurer : ISubtitlePlacementMeasurer, IDisposable
{
    private readonly AssSubtitlePlacementMeasurer measurer = new(AppContext.BaseDirectory);
    internal ProjectAsset Font { get; } = new(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");

    /// <summary>使用与独立 libass 相同的测试字体测量完整字幕。</summary>
    public SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line)
    {
        var measured = line with { Style = line.Style with { FontAssetId = Font.Id } };
        var assets = document.Assets.Any(asset => asset.Id == Font.Id) ? document.Assets : document.Assets.Add(Font);
        return measurer.Measure(document with { Assets = assets, Subtitles = [measured] }, measured);
    }

    /// <summary>释放测试字体和排版资源。</summary>
    public void Dispose()
    {
        measurer.Dispose();
    }
}
