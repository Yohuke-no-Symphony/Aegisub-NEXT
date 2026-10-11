using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private BezierCanvasCursors? bezierCursors;
    private TopLevel? bezierCursorHost;
    private KeyModifiers bezierCursorModifiers;

    private void UpdateBezierCursor()
    {
        if ((!IsMaskMode && EditMode != CanvasEditMode.PATH && !IsGraphicMode && !IsCreationMode) || disposed)
        {
            Cursor = null;
            return;
        }
        bezierCursors ??= new();
        var action = EditMode == CanvasEditMode.MASK_RECTANGLE ? BezierEditAction.NONE : BezierEditingModifiers.GetAction(bezierCursorModifiers);
        Cursor = action switch
        {
            BezierEditAction.DELETE => bezierCursors.Delete,
            BezierEditAction.INSERT => bezierCursors.Insert,
            _ => bezierCursors.Cross
        };
    }

    private void ObserveBezierModifiers(KeyEventArgs e, bool pressed)
    {
        var modifier = e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            _ => KeyModifiers.None
        };
        bezierCursorModifiers = pressed ? e.KeyModifiers | modifier : e.KeyModifiers & ~modifier;
        UpdateBezierCursor();
    }

    private void OnBezierModifierPressed(object? sender, KeyEventArgs e) => ObserveBezierModifiers(e, true);
    private void OnBezierModifierReleased(object? sender, KeyEventArgs e) => ObserveBezierModifiers(e, false);

    private void DetachBezierCursorHost()
    {
        bezierCursorHost?.RemoveHandler(KeyDownEvent, OnBezierModifierPressed);
        bezierCursorHost?.RemoveHandler(KeyUpEvent, OnBezierModifierReleased);
        bezierCursorHost = null;
        bezierCursorModifiers = KeyModifiers.None;
        UpdateBezierCursor();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (disposed)
        {
            return;
        }
        bezierCursorHost = TopLevel.GetTopLevel(this);
        bezierCursorHost?.AddHandler(KeyDownEvent, OnBezierModifierPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        bezierCursorHost?.AddHandler(KeyUpEvent, OnBezierModifierReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        UpdateBezierCursor();
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        bezierCursorModifiers = e.KeyModifiers;
        UpdateBezierCursor();
    }
}
