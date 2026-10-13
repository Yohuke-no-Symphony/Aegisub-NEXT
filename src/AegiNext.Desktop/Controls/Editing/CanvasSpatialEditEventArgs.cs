using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>三维手柄在冻结目标上的完整向量或透视距离结果，仅在手势完成时发布。</summary>
public sealed class CanvasSpatialEditEventArgs(Guid layerId, AnimationTrackTarget target, AnimationValue value) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public AnimationTrackTarget Target { get; } = target;
    public AnimationValue Value { get; } = value;
}
