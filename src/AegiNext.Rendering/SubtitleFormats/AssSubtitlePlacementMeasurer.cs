using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.SubtitleFormats;

/// <summary>通过共享字幕渲染器提供 ASS 导出所需的实际排版度量。</summary>
public sealed class AssSubtitlePlacementMeasurer : ISubtitlePlacementMeasurer, IDisposable
{
    private readonly ProjectSceneRenderer renderer;

    /// <summary>使用指定工程资源目录和可选字体目录创建本次导出的排版测量器。</summary>
    public AssSubtitlePlacementMeasurer(string projectDirectory, SystemFontCatalog? fontCatalog = null)
    {
        renderer = new(new DirectoryProjectAssetResolver(projectDirectory), fontCatalog);
    }

    /// <summary>从实际字幕排版获取 ASS 转换所需的定位、枢轴和文字边界。</summary>
    public SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line)
    {
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        return new(new(layout.BasePosition.X, layout.BasePosition.Y), new(layout.Pivot.X, layout.Pivot.Y),
            new(layout.Bounds.Left, layout.Bounds.Top), new(layout.Bounds.Width, layout.Bounds.Height));
    }

    /// <summary>释放本次字幕导出所用的排版与字体资源。</summary>
    public void Dispose()
    {
        renderer.Dispose();
    }
}
