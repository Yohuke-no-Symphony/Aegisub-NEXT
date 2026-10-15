using AegiNext.Core.Projects;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private static readonly Cursor effectReorderCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor effectMoveCursor = new(StandardCursorType.SizeAll);
    private const double EFFECT_DRAG_AXIS_RATIO = 1.25;
    private ProjectDocument? effectReorderDocument;
    private TimelineEffectClipId? effectReorderId;
    private TimelineRow? effectReorderRow;
    private TimelineEffectReorderRow[] effectReorderRows = [];
    private Point effectReorderOrigin;
    private int effectReorderSourceIndex;
    private int effectReorderIndex;
    private TimelineEffectClipId? effectReorderBeforeId;
    private double? effectInsertionY;
    private bool effectReorderStarted;
    private bool effectReorderHorizontal;

    /// <summary>释放竖向拖动的特效块后，提交同片段内的一次行重排。</summary>
    public event EventHandler<TimelineEffectClipReorderEventArgs>? EffectClipReorderCompleted;

    internal double? EffectInsertionY => effectInsertionY;

    private void BeginEffectClipDrag(TimelineEffectClipId id, Point point, IPointer pointer)
    {
        if (!rowsByLayer.TryGetValue(id.LayerId, out var row))
        {
            return;
        }
        effectReorderRows = row.Effects.ForLayer(id.LayerId)
            .Select(block => new TimelineEffectReorderRow(block.Id, block.Top, block.Height))
            .OrderBy(item => item.Top).ToArray();
        effectReorderSourceIndex = Array.FindIndex(effectReorderRows, item => item.Id == id);
        if (effectReorderSourceIndex < 0)
        {
            return;
        }
        effectReorderIndex = effectReorderSourceIndex;
        effectReorderId = id;
        effectReorderRow = row;
        effectReorderDocument = document;
        effectReorderOrigin = point;
        trackReorderPointer = point;
        dragId = id.LayerId;
        dragMode = TimelineDragMode.EFFECT_REORDER;
        capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void UpdateEffectReorder(Point point, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (!ReferenceEquals(document, effectReorderDocument) || effectReorderRow is not { } row)
        {
            CancelDrag();
            return;
        }
        trackReorderPointer = point;
        if (effectReorderHorizontal)
        {
            return;
        }
        if (!effectReorderStarted)
        {
            var displacement = point - effectReorderOrigin;
            var horizontal = Math.Abs(displacement.X);
            var vertical = Math.Abs(displacement.Y);
            if (horizontal >= TRACK_REORDER_THRESHOLD && horizontal > vertical * EFFECT_DRAG_AXIS_RATIO)
            {
                effectReorderHorizontal = true;
                if (effectReorderId is { InstanceId: not null } id)
                {
                    BeginEffectInstanceTimingDrag(id, effectReorderOrigin);
                    UpdateEffectInstanceDrag(ClipDeltaFromSeconds(displacement.X / PixelsPerSecond, modifiers), modifiers);
                }
                return;
            }
            if (vertical < TRACK_REORDER_THRESHOLD || vertical <= horizontal * EFFECT_DRAG_AXIS_RATIO)
            {
                return;
            }
            effectReorderStarted = true;
        }
        var parent = ClipRectangle(layersById[dragId], row);
        var left = Math.Max(HeaderWidth, parent.Left);
        var right = Math.Min(Bounds.Width, parent.Right);
        var y = Math.Clamp(point.Y, RulerHeight + 1, Math.Max(RulerHeight + 1, Bounds.Height - 1));
        validDrop = right > left && point.X >= left && point.X <= right &&
            y >= RowY(row) + effectReorderRows[0].Top &&
            y <= RowY(row) + effectReorderRows[^1].Top + effectReorderRows[^1].Height;
        if (validDrop)
        {
            var slot = effectReorderRows.Count(item => y >= RowY(row) + item.Top + item.Height / 2);
            effectReorderIndex = slot > effectReorderSourceIndex ? slot - 1 : slot;
            var remaining = effectReorderRows.Where((_, index) => index != effectReorderSourceIndex).ToArray();
            effectReorderBeforeId = effectReorderIndex < remaining.Length ? remaining[effectReorderIndex].Id : null;
            effectInsertionY = RowY(row) + (slot == effectReorderRows.Length
                ? effectReorderRows[^1].Top + effectReorderRows[^1].Height : effectReorderRows[slot].Top);
        }
        else
        {
            effectInsertionY = null;
        }
        RefreshTrackReorderScroll();
        InvalidateVisual();
    }

    private TimelineEffectClipReorderEventArgs? CompleteEffectReorder(Point point, KeyModifiers modifiers)
    {
        UpdateEffectReorder(point, modifiers);
        return effectReorderStarted && validDrop && effectReorderIndex != effectReorderSourceIndex &&
            effectReorderId is { } id && effectReorderDocument is { } expected && ReferenceEquals(document, expected)
                ? new(id, effectReorderBeforeId, expected) : null;
    }

    private void ClearEffectReorder()
    {
        effectReorderDocument = null;
        effectReorderId = null;
        effectReorderRow = null;
        effectReorderRows = [];
        effectInsertionY = null;
        effectReorderStarted = false;
        effectReorderHorizontal = false;
    }

    private void ScrollEffectReorder()
    {
        if (!ReferenceEquals(document, effectReorderDocument) || effectReorderRow is not { } row)
        {
            CancelDrag();
            return;
        }
        var step = TrackReorderScrollStep();
        var next = viewport.Pan(0, step, duration, ContentHeight);
        var first = Math.Max(0, row.Top + effectReorderRows[0].Top);
        var last = Math.Max(0, row.Top + effectReorderRows[^1].Top + effectReorderRows[^1].Height - viewport.Height);
        next = next with
        {
            VerticalOffset = step < 0 ? Math.Max(next.VerticalOffset, Math.Min(first, viewport.VerticalOffset)) :
                Math.Min(next.VerticalOffset, Math.Max(last, viewport.VerticalOffset))
        };
        if (next == viewport)
        {
            trackReorderScrollTimer.Stop();
            return;
        }
        PublishViewport(next);
        if (dragMode == TimelineDragMode.EFFECT_REORDER)
        {
            UpdateEffectReorder(trackReorderPointer);
        }
    }

    private void DrawEffectInsertion(DrawingContext context)
    {
        if (dragMode != TimelineDragMode.EFFECT_REORDER || effectInsertionY is not { } y || effectReorderRow is not { } row)
        {
            return;
        }
        var parent = ClipRectangle(layersById[dragId], row);
        var left = Math.Max(HeaderWidth, parent.Left);
        var right = Math.Min(Bounds.Width, parent.Right);
        if (right <= left)
        {
            return;
        }
        y = Math.Clamp(y, RulerHeight + 1, Math.Max(RulerHeight + 1, Bounds.Height - 1));
        using var bounds = context.PushClip(BodyRectangle());
        context.DrawLine(new Pen(drawingPalette.ActiveClipBorder, 3), new(left, y), new(right, y));
        context.DrawEllipse(drawingPalette.ActiveClipBorder, null, new(left + 4, y), 3, 3);
    }
}
