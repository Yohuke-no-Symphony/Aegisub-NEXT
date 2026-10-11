using Avalonia;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private readonly HashSet<Guid> collapsedTrackIds = [];

    /// <summary>提交轨道的稳定身份及请求的整体折叠状态。</summary>
    public event EventHandler<TimelineTrackCollapseEventArgs>? TrackCollapseRequested;

    /// <summary>取得与整体轨道头绘制和鼠标命中共用的折叠按钮几何。</summary>
    public Rect? GetTrackExpanderRectangle(Guid id) => rows.FirstOrDefault(row => row.TrackId == id) is { } row
        ? TimelineRow.ExpanderRectangle(TrackHeaderContentY(row)) : null;

    internal bool TryRequestTrackCollapse(Point point)
    {
        var row = RowAt(point.Y);
        if (point.Y < RulerHeight || row is null || !TimelineRow.ExpanderRectangle(TrackHeaderContentY(row)).Contains(point))
        {
            return false;
        }

        RequestTrackCollapse(row.TrackId);
        return true;
    }

    private void RequestTrackCollapse(Guid id)
    {
        CancelDrag();
        var isCollapsed = !collapsedTrackIds.Contains(id);
        if (TrackCollapseRequested is { } handler)
        {
            handler(this, new(id, isCollapsed));
            return;
        }

        var ids = TimelineViewState.CollapsedTrackIds;
        TimelineViewState = TimelineViewState with
        {
            CollapsedTrackIds = [.. (isCollapsed ? ids.Add(id) : ids.Remove(id)).Order()]
        };
    }
}
