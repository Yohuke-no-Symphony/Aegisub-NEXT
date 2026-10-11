using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>请求改变一个特效块的独立展开状态。</summary>
public sealed class TimelineEffectClipCollapseEventArgs(TimelineEffectClipId id, bool isCollapsed) : EventArgs
{
    public TimelineEffectClipId Id { get; } = id;
    public bool IsCollapsed { get; } = isCollapsed;
}
