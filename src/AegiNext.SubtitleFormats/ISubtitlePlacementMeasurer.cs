using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>为字幕格式转换提供与工程渲染共用的静态排版测量，不包含图层动画变换。</summary>
public interface ISubtitlePlacementMeasurer
{
    /// <summary>在已验证的原工程中测量完整字幕，包括行内字体、装饰和自动换行；字幕可以是独立且未保存的画布单位投影。</summary>
    SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line);
}
