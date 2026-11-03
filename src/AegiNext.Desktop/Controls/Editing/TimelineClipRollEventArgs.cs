using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>同轨相邻片段共用交界的一次滚动调整：前一片段结束与后一片段开始同时移到新的交界。</summary>
public sealed class TimelineClipRollEventArgs(Guid leftId, Guid rightId, MediaTime boundary, ProjectDocument? expectedDocument)
    : EventArgs
{
    public Guid LeftId { get; } = leftId;
    public Guid RightId { get; } = rightId;
    public MediaTime Boundary { get; } = boundary;
    public ProjectDocument? ExpectedDocument { get; } = expectedDocument;
}
