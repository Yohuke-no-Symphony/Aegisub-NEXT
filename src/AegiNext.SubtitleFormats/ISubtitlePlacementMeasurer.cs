using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>为字幕格式转换提供与工程渲染共用的静态排版测量，不包含图层动画变换。</summary>
public interface ISubtitlePlacementMeasurer
{
    /// <summary>测量指定工程中的完整字幕，包括行内字体、装饰和自动换行。</summary>
    SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line);
}
