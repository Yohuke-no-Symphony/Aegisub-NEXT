using Avalonia;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private Point? clipPastePointer;
    private bool clipPasteDisposed;

    /// <summary>按当前视口将时间线内的鼠标位置投影为粘贴轨道和时间；无有效落点时返回空。</summary>
    public TimelineClipContextEventArgs? GetClipPasteTarget(KeyModifiers modifiers = KeyModifiers.None)
    {
        return !clipPasteDisposed && VisualRoot is not null && IsEffectivelyVisible && !HasActiveDrag && clipPastePointer is { } point
            ? ClipPasteTargetAt(point, modifiers) : null;
    }

    /// <summary>将素材拖放位置投影到实际轨道与量化时间。</summary>
    public TimelineClipContextEventArgs? GetClipPlacementTarget(Point point, KeyModifiers modifiers = KeyModifiers.None)
    {
        return !clipPasteDisposed && VisualRoot is not null && IsEffectivelyVisible && !HasActiveDrag
            ? ClipPasteTargetAt(point, modifiers) : null;
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        clipPastePointer = e.GetPosition(this);
    }

    private TimelineClipContextEventArgs? ClipPasteTargetAt(Point point, KeyModifiers modifiers)
    {
        if (!BodyRectangle().Contains(point) || RowAt(point.Y) is not { } row)
        {
            return null;
        }

        return new(row.TrackId, ContextClipAt(point, row)?.Id, QuantizeEdit(TimeAt(point.X, modifiers), modifiers));
    }
}
