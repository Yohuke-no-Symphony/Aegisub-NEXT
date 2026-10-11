using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class StyleEditorInputController : IDisposable
{
    private readonly UserControl view;
    private readonly StyleEditorViewModel editor;
    private readonly WorkbenchSession session;
    private NumericDragEditSnapshot? drag;
    private Action? restoreLinkedFields;
    private int revision;
    private bool disposed;

    internal StyleEditorInputController(UserControl view, StyleEditorViewModel editor, WorkbenchSession session)
    {
        this.view = view;
        this.editor = editor;
        this.session = session;
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        view.AddHandler(NumericDragLabel.DragStartedEvent, OnDragStarted, RoutingStrategies.Bubble);
        view.AddHandler(NumericDragLabel.DragCompletedEvent, OnDragCompleted, RoutingStrategies.Bubble);
        view.AddHandler(NumericDragLabel.DragCanceledEvent, OnDragCanceled, RoutingStrategies.Bubble);
        session.NumericGestureCancellationRequested += OnGesturesCancelled;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Control source || !editor.IsActive)
        {
            return;
        }
        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input is null)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            ++revision;
            editor.CommitDrafts();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && input.Name is { } name && editor.RestoreField(name))
        {
            ++revision;
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (drag is not null || e.Source is not (TextBox or NumericUpDown))
        {
            return;
        }
        var currentRevision = revision;
        var source = session.DocumentSnapshot;
        var layerId = editor.LayerId;
        var root = TopLevel.GetTopLevel(view);
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && currentRevision == revision && drag is null && editor.IsActive && editor.LayerId == layerId &&
                ReferenceEquals(source, session.DocumentSnapshot) && root is not null &&
                ReferenceEquals(root, TopLevel.GetTopLevel(view)) && view.IsAttachedToVisualTree())
            {
                editor.CommitDrafts();
            }
        }, DispatcherPriority.Background);
    }

    private void OnDragStarted(object? sender, NumericDragEventArgs e)
    {
        ++revision;
        drag = new(session);
        restoreLinkedFields = editor.CaptureNumericGesture(e.Input.Name);
    }

    private void OnDragCompleted(object? sender, NumericDragEventArgs e)
    {
        var snapshot = drag;
        drag = null;
        restoreLinkedFields = null;
        ++revision;
        if (e.Changed && snapshot?.Matches(session) == true && editor.IsActive)
        {
            editor.CommitDrafts();
        }
    }

    private void OnDragCanceled(object? sender, NumericDragEventArgs e)
    {
        if (drag?.Matches(session) == true && editor.IsActive)
        {
            restoreLinkedFields?.Invoke();
        }
        restoreLinkedFields = null;
        drag = null;
        ++revision;
    }

    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    internal void CancelGestures()
    {
        ++revision;
        foreach (var label in view.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
        drag = null;
        restoreLinkedFields = null;
    }

    internal void FocusInvalidField(string? fieldKey)
    {
        if (fieldKey is null)
        {
            return;
        }
        foreach (var vector in view.GetLogicalDescendants().OfType<VectorDraftInput>().Concat(view.GetVisualDescendants().OfType<VectorDraftInput>()).Distinct())
        {
            if (vector.XFieldKey == fieldKey || vector.YFieldKey == fieldKey)
            {
                ExpandAncestors(vector);
                view.UpdateLayout();
            }
            if (vector.FocusField(fieldKey))
            {
                return;
            }
        }
        var control = view.FindControl<Control>(fieldKey);
        if (control is not null)
        {
            ExpandAncestors(control);
        }
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return;
        }
        if (control is NumericDraftInput numeric && numeric.FocusInput())
        {
            return;
        }
        control?.Focus();
    }

    private static void ExpandAncestors(Control control)
    {
        foreach (var expander in control.GetLogicalAncestors().OfType<Expander>().Concat(control.GetVisualAncestors().OfType<Expander>()).Distinct())
        {
            expander.IsExpanded = true;
        }
    }

    public void Dispose()
    {
        CancelGestures();
        disposed = true;
        view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        view.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        view.RemoveHandler(NumericDragLabel.DragStartedEvent, OnDragStarted);
        view.RemoveHandler(NumericDragLabel.DragCompletedEvent, OnDragCompleted);
        view.RemoveHandler(NumericDragLabel.DragCanceledEvent, OnDragCanceled);
        session.NumericGestureCancellationRequested -= OnGesturesCancelled;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
    }
}
