using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Panels.Effects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed partial class MaskPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly MaskPanelViewModel viewModel;
    private readonly ComboBox contourCombo;
    private readonly ListBox pointList;
    private readonly AnimationOptionsPopupHost animationOptions;
    private bool synchronizingSelection;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    private bool disposed;

    internal MaskPanelView(MaskPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        animationOptions = new(session, PanelId);
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        contourCombo = this.FindControl<ComboBox>("MaskContourCombo")!;
        pointList = this.FindControl<ListBox>("MaskPointList")!;
        DataContext = viewModel;
        contourCombo.SelectionChanged += OnContourSelectionChanged;
        pointList.SelectionChanged += OnPointSelectionChanged;
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnFieldGotFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnCommitKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        AddHandler(AnimationPropertyRowControl.AnimationOptionsRequestedEvent, OnAnimationOptionsRequested);
        BindNumericDrags();
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        viewModel.PropertyChanged += OnViewModelChanged;
    }

    public string PanelId => "masks";

    /// <inheritdoc />
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (command == WorkbenchCommand.DELETE_SUBTITLE)
        {
            return !animationOptions.IsOpen && TryGetAnimationActionRow(focusedElement) is { } row &&
                session.CanDeleteSelectedAnimationKeyframe(row.LayerId, row.Target);
        }
        return command == WorkbenchCommand.END_TEXT_INPUT && viewModel.IsMaskEditing &&
            focusedElement is Control control && !control.GetSelfAndVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown);
    }

    /// <inheritdoc />
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        if (command == WorkbenchCommand.DELETE_SUBTITLE && TryGetAnimationActionRow(focusedElement) is { } row)
        {
            _ = session.DeleteSelectedAnimationKeyframeAsync(row.LayerId, row.Target);
            return true;
        }
        session.MaskEditing.ExitEditing();
        return true;
    }

    /// <inheritdoc />
    public void CancelGestures()
    {
        CancelNumericDrags();
        suppressFocusCommit = true;
        var revision = ++focusCommitRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == focusCommitRevision)
            {
                suppressFocusCommit = false;
            }
        }, DispatcherPriority.Background);
    }

    /// <inheritdoc />
    public void FocusInvalidField(string? fieldKey)
    {
        if (animationOptions.FocusInvalidField(fieldKey))
        {
            return;
        }
        if (fieldKey is null)
        {
            Focus();
            return;
        }
        foreach (var row in this.GetVisualDescendants().OfType<AnimationPropertyRowControl>())
        {
            if (row.FocusField(ResolveFieldKey(row, fieldKey)))
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        animationOptions.Close();
        CancelGestures();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFieldGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault() is { } input)
        {
            BeginFieldEdit(input);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        suppressFocusCommit = false;
        if (e.Key == Key.Escape && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault() is { } input)
        {
            var row = source.GetSelfAndVisualAncestors().OfType<AnimationPropertyRowControl>().FirstOrDefault();
            var fieldKey = row?.GetInputField(source);
            if (fieldKey is not null && GetSharedRow(input) is { } sharedRow)
            {
                sharedRow.Restore(fieldKey);
            }
            else
            {
                viewModel.RestoreField(fieldKey ?? input.Name);
            }
            e.Handled = true;
        }
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
        {
            viewModel.CommitDrafts();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>()
            .Any(input => input.IsTitleDragging))
        {
            return;
        }
        if (e.Source is not (TextBox or NumericUpDown))
        {
            return;
        }
        var root = TopLevel.GetTopLevel(this);
        var suppressed = suppressFocusCommit;
        var revision = focusCommitRevision;
        var document = session.DocumentSnapshot;
        var layerId = session.SelectedLayerId;
        var nodeId = session.SceneEditing.MaskNodeId;
        var contourId = session.SceneEditing.MaskContourId;
        var target = session.SceneEditing.Target;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && !suppressed && revision == focusCommitRevision && layerId == session.SelectedLayerId &&
                nodeId == session.SceneEditing.MaskNodeId && contourId == session.SceneEditing.MaskContourId && target == session.SceneEditing.Target &&
                ReferenceEquals(document, session.DocumentSnapshot) && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                this.IsAttachedToVisualTree())
            {
                viewModel.CommitDrafts();
            }
        }, DispatcherPriority.Background);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaskPanelViewModel.SelectedContour) or nameof(MaskPanelViewModel.SelectedPoint) or
            nameof(MaskPanelViewModel.Contours) or nameof(MaskPanelViewModel.Points))
        {
            SynchronizeSelection();
        }
        if (e.PropertyName is nameof(MaskPanelViewModel.ValidationError) or nameof(MaskPanelViewModel.InvalidFieldKey))
        {
            foreach (var input in this.GetVisualDescendants().OfType<NumericDraftInput>())
            {
                if (GetSharedRow(input) is not null)
                {
                    continue;
                }
                var key = input.DataContext is MaskNumericField field ? field.Key : input.Name;
                DataValidationErrors.SetErrors(input, key == viewModel.InvalidFieldKey && viewModel.ValidationError is { } error ? new[] { error } : null);
            }
        }
    }

    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    private void OnContourSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || session.IsUpdating || contourCombo.SelectedItem is not MaskSelectionChoice selected ||
            selected.Id == viewModel.SelectedContour?.Id)
        {
            return;
        }
        RequestSelection(() => viewModel.SelectedContour = selected);
    }

    private void OnPointSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || session.IsUpdating || pointList.SelectedItem is not MaskPointListItem selected ||
            selected.Id == viewModel.SelectedPoint?.Id)
        {
            return;
        }
        RequestSelection(() => viewModel.SelectedPoint = selected);
    }

    private void RequestSelection(Action request)
    {
        synchronizingSelection = true;
        try
        {
            request();
        }
        finally
        {
            SynchronizeSelection();
            synchronizingSelection = false;
        }
    }

    private void SynchronizeSelection()
    {
        var previousSynchronization = synchronizingSelection;
        synchronizingSelection = true;
        try
        {
            contourCombo.SetCurrentValue(SelectingItemsControl.SelectedItemProperty, viewModel.SelectedContour);
            pointList.SetCurrentValue(SelectingItemsControl.SelectedItemProperty, viewModel.SelectedPoint);
        }
        finally
        {
            synchronizingSelection = previousSynchronization;
        }
    }

    private void BeginFieldEdit(Control input)
    {
        if (input.DataContext is MaskVectorField { Target: null })
        {
            session.MaskEditing.BeginPivotEdit(PanelId);
        }
        else
        {
            GetSharedRow(input)?.BeginEdit(PanelId);
        }
    }

    private static AnimationPropertyRowViewModel? GetSharedRow(Control control)
    {
        return control.DataContext switch
        {
            MaskNumericField field => field.Row,
            MaskVectorField field => field.Row,
            _ => null
        };
    }

    private static string? ResolveFieldKey(AnimationPropertyRowControl row, string fieldKey)
    {
        return row.DataContext switch
        {
            MaskNumericField field when field.Key == fieldKey => field.FieldKey,
            MaskVectorField field when field.X.Key == fieldKey => field.X.FieldKey,
            MaskVectorField field when field.Y.Key == fieldKey => field.Y.FieldKey,
            _ => fieldKey
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        animationOptions.Dispose();
        focusCommitRevision++;
        contourCombo.SelectionChanged -= OnContourSelectionChanged;
        pointList.SelectionChanged -= OnPointSelectionChanged;
        session.NumericGestureCancellationRequested -= OnNumericGestureCancellation;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        viewModel.PropertyChanged -= OnViewModelChanged;
    }
}
