using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private const double TRACK_SOLO_TOGGLE_SIZE = 20;
    private const double TRACK_SOLO_TOGGLE_INSET = 4;
    private Guid? soloTrackId;
    private Guid? soloDocumentId;
    private Guid? hoveredSoloTrack;
    private double? verticalOffsetBeforeSolo;

    /// <summary>请求切换稳定轨道的 Solo，仅改变时间线投影。</summary>
    public event EventHandler<TimelineTrackSoloEventArgs>? TrackSoloRequested;

    public Guid? SoloTrackId
    {
        get => soloTrackId;
        set
        {
            var next = value is { } id && document.Tracks.Any(track => track.Id == id) ? value : null;
            if (soloTrackId == next)
            {
                return;
            }

            CancelDrag();
            if (soloTrackId is null && next.HasValue)
            {
                verticalOffsetBeforeSolo = viewport.VerticalOffset;
            }
            soloTrackId = next;
            soloDocumentId = next.HasValue ? document.Id : null;
            RebuildRows();
            var offset = next is null ? verticalOffsetBeforeSolo ?? viewport.VerticalOffset : viewport.VerticalOffset;
            if (next is null)
            {
                verticalOffsetBeforeSolo = null;
            }
            SetViewport(viewport with { VerticalOffset = offset }, duration);
            InvalidateVisual();
        }
    }

    /// <summary>取得与头部绘制和点击命中共用的 Solo 按钮矩形；隐藏或过窄的头部返回 null。</summary>
    public Rect? GetTrackSoloToggleRectangle(Guid trackId)
    {
        return rows.FirstOrDefault(row => row.TrackId == trackId) is { } row
            ? GetTrackSoloToggleRectangle(row, TrackHeaderContentY(row)) : null;
    }

    private void InitializeTrackSolo()
    {
        PointerExited += OnTrackSoloPointerExited;
    }

    private void DisposeTrackSolo()
    {
        PointerExited -= OnTrackSoloPointerExited;
        hoveredSoloTrack = null;
        ToolTip.SetTip(this, null);
    }

    private void RefreshTrackSoloTooltip()
    {
        if (hoveredSoloTrack.HasValue)
        {
            ToolTip.SetTip(this, Localization.Get("Workbench.SoloSubtitleTrack"));
        }
    }

    private void UpdateTrackSoloHover(Point point)
    {
        var row = RowAt(point.Y);
        var id = row?.TrackId is { } track && GetTrackSoloToggleRectangle(row, TrackHeaderContentY(row)) is { } toggle &&
            toggle.Contains(point) ? track : (Guid?)null;
        var previous = hoveredSoloTrack;
        if (previous != id)
        {
            ToolTip.SetIsOpen(this, false);
        }
        hoveredSoloTrack = id;
        if (id.HasValue)
        {
            RefreshTrackSoloTooltip();
        }
        else if (previous.HasValue && !hoveredMaskClipId.HasValue)
        {
            ToolTip.SetTip(this, null);
        }
    }

    private void OnTrackSoloPointerExited(object? sender, PointerEventArgs e)
    {
        if (hoveredSoloTrack.HasValue)
        {
            hoveredSoloTrack = null;
            ToolTip.SetTip(this, null);
        }
    }

    internal bool TryRequestTrackSolo(Point point)
    {
        var row = RowAt(point.Y);
        if (point.Y < RulerHeight || row?.TrackId is not { } track ||
            GetTrackSoloToggleRectangle(row, TrackHeaderContentY(row)) is not { } toggle || !toggle.Contains(point))
        {
            return false;
        }

        CancelDrag();
        TrackSoloRequested?.Invoke(this, new(track));
        return true;
    }

    private bool IsTrackVisible(Guid trackId) => SoloTrackId is null || SoloTrackId == trackId;

    private void ValidateTrackSoloDocument(ProjectDocument value)
    {
        if (soloTrackId is { } solo && (soloDocumentId != value.Id || !value.Tracks.Any(track => track.Id == solo)))
        {
            soloTrackId = null;
            soloDocumentId = null;
            if (verticalOffsetBeforeSolo is { } offset)
            {
                viewport = viewport with { VerticalOffset = offset };
            }
            verticalOffsetBeforeSolo = null;
        }
    }

    private Rect? GetTrackSoloToggleRectangle(TimelineRow row, double rowY)
    {
        return HeaderWidth >= 26 + TRACK_SOLO_TOGGLE_SIZE + TRACK_SOLO_TOGGLE_INSET * 2
            ? new(HeaderWidth - TRACK_SOLO_TOGGLE_INSET - TRACK_SOLO_TOGGLE_SIZE,
                rowY + TRACK_SOLO_TOGGLE_INSET, TRACK_SOLO_TOGGLE_SIZE, TRACK_SOLO_TOGGLE_SIZE)
            : null;
    }

    private Rect GetTrackHeaderNameRectangle(TimelineRow row, double rowY)
    {
        var left = 26;
        var right = GetTrackSoloToggleRectangle(row, rowY)?.Left - TRACK_SOLO_TOGGLE_INSET ?? HeaderWidth - 2;
        return new(left, rowY + 4, Math.Max(0, right - left), 20);
    }

    private void DrawTrackSoloToggle(DrawingContext context, TimelineRow row, double rowY)
    {
        if (GetTrackSoloToggleRectangle(row, rowY) is not { } rectangle)
        {
            return;
        }

        var active = SoloTrackId == row.TrackId;
        context.DrawRectangle(active ? drawingPalette.StyleBadge : drawingPalette.Track,
            new Pen(active ? drawingPalette.ActiveClipBorder : drawingPalette.Grid.Brush, active ? 1.5 : 1), rectangle, 3, 3);
        TextLayoutBuildCount++;
        using var layout = WorkbenchTextFormatting.CreateLayout(this, "S", 11, drawingPalette.Foreground, rectangle.Height);
        var origin = WorkbenchTextFormatting.CenteredOrigin(layout, rectangle);
        layout.Draw(context, new(origin.X + (rectangle.Width - layout.Width) / 2, origin.Y));
    }
}
