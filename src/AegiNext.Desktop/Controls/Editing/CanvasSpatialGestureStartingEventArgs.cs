using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>冻结三维画布手势的原生属性和文字范围，由宿主先提交已有草稿。</summary>
public sealed class CanvasSpatialGestureStartingEventArgs(Guid layerId, AnimationTrackTarget target) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public AnimationTrackTarget Target { get; } = target;
    public bool Cancel { get; set; }
    internal MediaTime? LocalTime { get; set; }
    internal bool IsKeyframe { get; set; }
}
