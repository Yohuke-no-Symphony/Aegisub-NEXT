using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>请求打开一个特效块的上下文菜单。</summary>
public sealed class TimelineEffectClipContextEventArgs(TimelineEffectClipId id, MediaTime? keyframeTime = null,
    Guid? operationId = null, bool isOperationStart = false) : EventArgs
{
    public TimelineEffectClipId Id { get; } = id;
    public MediaTime? KeyframeTime { get; } = keyframeTime;
    public Guid? OperationId { get; } = operationId;
    public bool IsOperationStart { get; } = isOperationStart;
}
