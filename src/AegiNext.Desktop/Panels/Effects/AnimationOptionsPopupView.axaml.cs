using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class AnimationOptionsPopupView : UserControl, IDisposable
{
    private readonly WorkbenchSession session;
    private readonly AnimationOptionsPopupViewModel viewModel;
    private readonly ComboBox interpolation;
    private readonly ComboBox components;
    private readonly ComboBox operationInterpolation;
    private bool active;
    private bool refreshing;
    private bool disposed;
    private int focusRevision;
    private NumericDragEditSnapshot? dragTarget;

    internal AnimationOptionsPopupView(WorkbenchSession session, AnimationOptionsPopupViewModel viewModel)
    {
        this.session = session;
        this.viewModel = viewModel;
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        interpolation = this.FindControl<ComboBox>("InterpolationCombo")!;
        components = this.FindControl<ComboBox>("KeyframeCurveComponentCombo")!;
        operationInterpolation = this.FindControl<ComboBox>("OperationCurveInterpolationCombo")!;
        DataContext = viewModel;
        interpolation.SelectionChanged += (_, _) =>
        {
            if (active && !refreshing && !session.IsUpdating && viewModel.CanEditKeyframes)
            {
                viewModel.CommitInterpolation(interpolation.SelectedIndex);
            }
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnCommitKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragStartedEvent, (_, _) =>
        {
            focusRevision++;
            dragTarget = new(session);
        });
        AddHandler(NumericDragLabel.DragCompletedEvent, (_, e) =>
        {
            focusRevision++;
            var target = dragTarget;
            dragTarget = null;
            if (active && e.Changed && target?.Matches(session) == true)
            {
                viewModel.TryCommitDrafts(false);
            }
        });
        AddHandler(NumericDragLabel.DragCanceledEvent, (_, _) =>
        {
            focusRevision++;
            dragTarget = null;
            session.RefreshMaskPreview();
        });
        viewModel.ChoicesRefreshing += OnChoicesRefreshing;
        viewModel.ChoicesRefreshed += OnChoicesRefreshed;
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.OperationColorDraft.Committed += OnColorCommitted;
        session.NumericGestureCancellationRequested += OnGestureCancellation;
    }

    internal void SetActive(bool value)
    {
        active = false;
        viewModel.SetActive(false);
        DataContext = value ? viewModel : null;
        active = value;
        viewModel.SetActive(value);
        if (!value)
        {
            CancelGestures();
        }
    }

    internal void CancelGestures()
    {
        focusRevision++;
        dragTarget = null;
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
    }

    internal bool FocusField(string? fieldKey)
    {
        foreach (var vector in this.GetVisualDescendants().OfType<VectorDraftInput>())
        {
            if (fieldKey is not null && vector.FocusField(fieldKey))
            {
                return true;
            }
        }
        var control = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == fieldKey);
        if (control is NumericDraftInput numeric)
        {
            return numeric.FocusInput();
        }
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return true;
        }
        return (control ?? interpolation).Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Source is not Control source ||
            source.GetSelfAndVisualAncestors().OfType<ColorDraftInput>().Any())
        {
            return;
        }
        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input is null)
        {
            return;
        }
        focusRevision++;
        var vector = input.GetVisualAncestors().OfType<VectorDraftInput>().FirstOrDefault();
        var key = vector?.GetInputField(input) ?? input.Name;
        if (!viewModel.RestoreOperationField(key) && key is not null)
        {
            viewModel.RestoreField(key);
        }
        e.Handled = true;
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (active && e.Key == Key.Enter && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
        {
            viewModel.TryCommitDrafts();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!active || e.Source is not (TextBox or NumericUpDown) || dragTarget is not null)
        {
            return;
        }
        var revision = focusRevision;
        var document = session.DocumentSnapshot;
        var target = session.AnimationOptionsTarget;
        var root = TopLevel.GetTopLevel(this);
        Dispatcher.UIThread.Post(() =>
        {
            if (active && !disposed && revision == focusRevision && target == session.AnimationOptionsTarget &&
                ReferenceEquals(document, session.DocumentSnapshot) && root is not null &&
                ReferenceEquals(root, TopLevel.GetTopLevel(this)) && this.IsAttachedToVisualTree())
            {
                viewModel.TryCommitDrafts(false);
            }
        }, DispatcherPriority.Background);
    }

    private void OnChoicesRefreshing(object? sender, EventArgs e)
    {
        refreshing = true;
        foreach (var choice in new[] { interpolation, components, operationInterpolation })
        {
            var selectedIndex = choice.SelectedIndex;
            choice.BeginInit();
            choice.SelectedIndex = selectedIndex;
        }
    }

    private void OnChoicesRefreshed(object? sender, EventArgs e)
    {
        foreach (var choice in new[] { interpolation, components, operationInterpolation })
        {
            choice.EndInit();
        }
        interpolation.SelectedIndex = viewModel.Interpolation;
        components.SelectedItem = viewModel.SelectedKeyframeCurveComponent;
        operationInterpolation.SelectedIndex = viewModel.OperationCurveInterpolation;
        refreshing = false;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(EffectsPanelViewModel.ValidationError) or nameof(EffectsPanelViewModel.InvalidFieldKey)))
        {
            return;
        }
        foreach (var input in this.GetVisualDescendants().OfType<NumericDraftInput>())
        {
            var vector = input.GetVisualAncestors().OfType<VectorDraftInput>().FirstOrDefault();
            var key = vector?.GetInputField(input) ?? input.Name;
            DataValidationErrors.SetErrors(input, key == viewModel.InvalidFieldKey && viewModel.ValidationError is { } message ? new[] { message } : null);
        }
    }

    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs e)
    {
        if (active)
        {
            viewModel.TryCommitDrafts(false);
        }
    }
    private void OnGestureCancellation(object? sender, EventArgs e) => CancelGestures();

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        SetActive(false);
        viewModel.ChoicesRefreshing -= OnChoicesRefreshing;
        viewModel.ChoicesRefreshed -= OnChoicesRefreshed;
        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.OperationColorDraft.Committed -= OnColorCommitted;
        session.NumericGestureCancellationRequested -= OnGestureCancellation;
    }
}
