using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>将外部格式的静态矢量及矩形裁切组合为原生蒙版，不持久化格式专属状态。</summary>
public interface ISubtitleMaskGeometryCombiner
{
    /// <summary>保留两个蒙版各自的反相语义；失败时返回可供导入诊断的原因。</summary>
    bool TryCombine(VectorClipMask vector, RectangleClipMask rectangle, out ClipMask result, out string? reason);
}
