using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>冻结同片段内特效行的完整身份、后继插入锚点及工程快照。</summary>
public sealed class TimelineEffectClipReorderEventArgs(TimelineEffectClipId id, TimelineEffectClipId? beforeId,
    ProjectDocument expectedDocument) : EventArgs
{
    public TimelineEffectClipId Id { get; } = id;
    public TimelineEffectClipId? BeforeId { get; } = beforeId;
    public ProjectDocument ExpectedDocument { get; } = expectedDocument;
}
