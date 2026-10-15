using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>独立特效块完成移动或裁剪后的内容时段与冻结工程快照。</summary>
public sealed class TimelineEffectInstanceTimingEventArgs(TimelineEffectClipId id, MediaTime start,
    MediaTime end, ProjectDocument expectedDocument) : EventArgs
{
    public TimelineEffectClipId Id { get; } = id;
    public MediaTime Start { get; } = start;
    public MediaTime End { get; } = end;
    public ProjectDocument ExpectedDocument { get; } = expectedDocument;
}
