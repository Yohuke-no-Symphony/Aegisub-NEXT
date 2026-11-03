namespace AegiNext.Desktop.Controls;

/// <summary>时间线字幕选择。</summary>
public sealed class TimelineSelectionEventArgs : EventArgs
{
    /// <summary>创建选择请求。</summary>
    public TimelineSelectionEventArgs(Guid id, IReadOnlyList<Guid>? selectedIds = null)
    {
        Id = id;
        SelectedIds = selectedIds ?? [id];
    }

    public Guid Id { get; }
    public IReadOnlyList<Guid> SelectedIds { get; }
    public bool SelectionAccepted { get; set; } = true;

    /// <summary>选择由即将开始的拖曳手势触发；自动跳转应推迟到确认没有拖动之后。</summary>
    public bool DeferAutoSeek { get; init; }
}
