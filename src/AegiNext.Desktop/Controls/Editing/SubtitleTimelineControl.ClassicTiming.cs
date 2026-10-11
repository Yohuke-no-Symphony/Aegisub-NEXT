using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private TimelineTimingPreview? timingPreview;

    public bool IsClassicTimingEnabled { get; set; }
    public event EventHandler<TimelineClassicTimingEventArgs>? ClassicTimingRequested;

    /// <summary>仅更新新字幕的显示范围，保留轨道行、选择和导航手势。</summary>
    public void SetTimingPreview(TimelineTimingPreview? value)
    {
        if (timingPreview == value)
        {
            return;
        }

        var cueChanged = timingPreview?.CueId != value?.CueId;
        timingPreview = value;
        previewDrawing.Dispose();
        previewMarkersSnapshot = null;
        if (cueChanged)
        {
            InvalidateSceneDrawing();
        }
        InvalidateVisual();
    }

    internal bool TryRequestClassicTiming(PointerPressedEventArgs e, bool preserveFocus = false)
    {
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (!IsClassicTimingEnabled || point.X < HeaderWidth || point.Y < RulerHeight ||
            (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed))
        {
            return false;
        }

        if (FindEffectClip(point) is not null || FindKeyframe(point) is not null)
        {
            return false;
        }

        clipPastePointer = point;
        CancelDrag();
        if (!preserveFocus)
        {
            Focus();
        }
        if (selectedCue is { } cueId && cuesById.ContainsKey(cueId))
        {
            CacheSnapBoundaries(selectedLayer?.Id ?? Guid.Empty);
            var time = Max(MediaTime.Zero, TimeFromSeconds(ViewStart + (point.X - HeaderWidth) / PixelsPerSecond,
                e.KeyModifiers | KeyModifiers.Alt));
            time = QuantizeEdit(time, e.KeyModifiers);
            var snapping = (IsSnapEnabled != ((e.KeyModifiers & KeyModifiers.Shift) != 0)) &&
                (e.KeyModifiers & KeyModifiers.Alt) == 0;
            if (snapping)
            {
                time = TimelineQuantization.Snap(time, snapBoundaries, PixelsPerSecond);
            }

            ClassicTimingRequested?.Invoke(this, new(cueId, time, properties.IsLeftButtonPressed));
        }

        e.Handled = true;
        return true;
    }

    private ProjectLayer ApplyTimingPreview(ProjectLayer layer) =>
        timingPreview is { } preview && layer.SubtitleId == preview.CueId
            ? LayerAnimationTiming.Retime(layer, preview.Start, preview.End, TimelineEditMode.CROP) : layer;
}
