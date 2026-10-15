using AegiNext.Core.Projects;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private const double TRACK_REORDER_THRESHOLD = 4;
    private const double TRACK_REORDER_SCROLL_EDGE = 24;
    private readonly DispatcherTimer trackReorderScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private ProjectDocument? trackReorderDocument;
    private List<TimelineRow> trackReorderRows = [];
    private Point trackReorderOrigin;
    private Point trackReorderPointer;
    private int trackReorderSourceIndex;
    private int trackReorderIndex;
    private double? trackInsertionY;
    private bool trackReorderStarted;

    /// <summary>释放轨道头后请求一次叠覆顺序编辑；移动期间只绘制插入位置。</summary>
    public event EventHandler<TimelineTrackReorderEventArgs>? TrackReorderCompleted;

    internal double? TrackInsertionY => trackInsertionY;

    private void InitializeTrackReorder() => trackReorderScrollTimer.Tick += OnTrackReorderScroll;

    private void DisposeTrackReorder()
    {
        trackReorderScrollTimer.Stop();
        trackReorderScrollTimer.Tick -= OnTrackReorderScroll;
    }

    private void BeginTrackReorder(Guid trackId, Point point, IPointer pointer)
    {
        if (SoloTrackId.HasValue || rows.Count < 2 || !document.Tracks.Any(track => track.Id == trackId))
        {
            return;
        }

        trackReorderDocument = document;
        trackReorderRows = rows;
        trackReorderOrigin = point;
        trackReorderSourceIndex = document.Tracks.IndexOf(document.Tracks.Single(track => track.Id == trackId));
        trackReorderIndex = trackReorderSourceIndex;
        dragId = trackId;
        dragMode = TimelineDragMode.TRACK_REORDER;
        capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void UpdateTrackReorder(Point point)
    {
        if (!ReferenceEquals(document, trackReorderDocument))
        {
            CancelDrag();
            return;
        }
        var displacement = point - trackReorderOrigin;
        if (!trackReorderStarted && displacement.X * displacement.X + displacement.Y * displacement.Y <
            TRACK_REORDER_THRESHOLD * TRACK_REORDER_THRESHOLD)
        {
            return;
        }

        trackReorderPointer = point;
        trackReorderStarted = true;
        var slot = trackReorderRows.Count(row => point.Y >= RowY(row) + row.Height / 2);
        trackReorderIndex = slot > trackReorderSourceIndex ? slot - 1 : slot;
        trackInsertionY = slot == trackReorderRows.Count
            ? RowY(trackReorderRows[^1]) + trackReorderRows[^1].Height
            : RowY(trackReorderRows[slot]);
        RefreshTrackReorderScroll();
        InvalidateVisual();
    }

    private TimelineTrackReorderEventArgs? CompleteTrackReorder(Point point)
    {
        UpdateTrackReorder(point);
        return trackReorderStarted && trackReorderIndex != trackReorderSourceIndex &&
               trackReorderDocument is { } expected && ReferenceEquals(document, expected)
            ? new(dragId, trackReorderIndex, expected) : null;
    }

    private void ClearTrackReorder()
    {
        trackReorderScrollTimer.Stop();
        trackReorderDocument = null;
        trackReorderRows = [];
        trackInsertionY = null;
        trackReorderStarted = false;
    }

    private double TrackReorderScrollStep()
    {
        if (trackReorderPointer.Y < RulerHeight + TRACK_REORDER_SCROLL_EDGE)
        {
            return -8 * Math.Clamp((RulerHeight + TRACK_REORDER_SCROLL_EDGE - trackReorderPointer.Y) /
                TRACK_REORDER_SCROLL_EDGE, 0, 1);
        }
        if (trackReorderPointer.Y > Bounds.Height - TRACK_REORDER_SCROLL_EDGE)
        {
            return 8 * Math.Clamp((trackReorderPointer.Y - Bounds.Height + TRACK_REORDER_SCROLL_EDGE) /
                TRACK_REORDER_SCROLL_EDGE, 0, 1);
        }
        return 0;
    }

    private void RefreshTrackReorderScroll()
    {
        var step = TrackReorderScrollStep();
        if (step == 0 || viewport.Pan(0, step, duration, ContentHeight) == viewport)
        {
            trackReorderScrollTimer.Stop();
        }
        else
        {
            trackReorderScrollTimer.Start();
        }
    }

    private void OnTrackReorderScroll(object? sender, EventArgs e)
    {
        if (dragMode == TimelineDragMode.EFFECT_REORDER)
        {
            ScrollEffectReorder();
            return;
        }
        if (dragMode != TimelineDragMode.TRACK_REORDER || !ReferenceEquals(document, trackReorderDocument))
        {
            CancelDrag();
            return;
        }

        PublishViewport(viewport.Pan(0, TrackReorderScrollStep(), duration, ContentHeight));
        if (dragMode == TimelineDragMode.TRACK_REORDER)
        {
            UpdateTrackReorder(trackReorderPointer);
        }
    }

    private void DrawTrackInsertion(DrawingContext context)
    {
        if (dragMode != TimelineDragMode.TRACK_REORDER || trackInsertionY is not { } insertionY)
        {
            return;
        }

        var y = Math.Clamp(insertionY, RulerHeight + 1, Math.Max(RulerHeight + 1, Bounds.Height - 1));
        context.DrawLine(new Pen(drawingPalette.ActiveClipBorder, 3), new(0, y), new(Bounds.Width, y));
        context.DrawEllipse(drawingPalette.ActiveClipBorder, null, new(5, y), 4, 4);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && HasActiveDrag)
        {
            CancelGesture();
            e.Handled = true;
        }
    }
}
