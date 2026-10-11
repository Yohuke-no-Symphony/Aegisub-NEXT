using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>请求选择特效块，由接收方完成草稿校验后接受。</summary>
public sealed class TimelineEffectClipSelectionEventArgs(TimelineEffectClipId id) : EventArgs
{
    public TimelineEffectClipId Id { get; } = id;
    public bool SelectionAccepted { get; set; }
}
