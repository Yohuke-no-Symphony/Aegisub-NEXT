using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly EffectsPanelViewModel viewModel;
    private readonly ComboBox blend;
    private readonly ComboBox effectInstances;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    private NumericDragEditSnapshot? numericDragTarget;
    private bool disposed;
    internal EffectsPanelView(EffectsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        effectInstances = this.FindControl<ComboBox>("EffectInstancesCombo")!;
        viewModel.InstanceChoicesRefreshing += OnInstanceChoicesRefreshing;
        viewModel.InstanceChoicesRefreshed += OnInstanceChoicesRefreshed;
        this.FindControl<EffectScriptParameterEditor>("InstanceParameterEditor")!.CommitRequested += (_, _) => viewModel.CommitDrafts();
        this.FindControl<CheckBox>("EffectInstanceEnabledCheck")!.IsCheckedChanged += async (sender, _) =>
        {
            if (!session.IsUpdating && sender is CheckBox input)
            {
                await viewModel.CommitInstanceEnabledAsync(input.IsChecked == true);
            }
        };
        blend = this.FindControl<ComboBox>("BlendCombo")!;
        blend.SelectionChanged += (_, _) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitBlend(blend.SelectedIndex);
            }
        };
        animationOptions = new(session, PanelId);
        AddHandler(AnimationPropertyRowControl.AnimationOptionsRequestedEvent, OnAnimationOptionsRequested);
        viewModel.ChoicesRefreshing += OnChoicesRefreshing;
        viewModel.ChoicesRefreshed += OnChoicesRefreshed;
        var orientPath = this.FindControl<CheckBox>("OrientPathCheck")!;
        orientPath.IsCheckedChanged += (_, _) => viewModel.CommitOrientPath(orientPath.IsChecked == true);
        AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
        {
            if (e.Source is Control source)
            {
                foreach (var category in source.GetVisualAncestors().OfType<Expander>())
                {
                    if (source.Name == "PART_HeaderSite" || source.GetVisualAncestors().OfType<Control>().Any(control =>
                            control.Name == "PART_HeaderSite" && ReferenceEquals(control.TemplatedParent, category)))
                    {
                        continue;
                    }
                    category.IsExpanded = true;
                }
            }
        });
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<Control>().Any(control =>
                    control.Name == "PART_HeaderSite" && control.TemplatedParent is Expander))
            {
                CancelGestures();
                return;
            }
            suppressFocusCommit = false;
            if (e.Source is Control editSource)
            {
                BeginFieldEdit(editSource);
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control source)
            {
                BeginFieldEdit(source);
            }
        }, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragStartedEvent, (_, e) =>
        {
            ++focusCommitRevision;
            suppressFocusCommit = true;
            numericDragTarget = new(session);
            if (e.Source is Control source)
            {
                BeginFieldEdit(source);
            }
        });
        AddHandler(NumericDragLabel.DragCompletedEvent, (_, e) =>
        {
            ++focusCommitRevision;
            var target = numericDragTarget;
            numericDragTarget = null;
            if (e.Changed && target?.Matches(session) == true)
            {
                viewModel.CommitDrafts();
            }
            suppressFocusCommit = false;
        });
        AddHandler(NumericDragLabel.DragCanceledEvent, (_, _) =>
        {
            ++focusCommitRevision;
            numericDragTarget = null;
            session.RefreshMaskPreview();
        });
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnCommitKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown)
            {
                if (this.GetVisualDescendants().OfType<NumericDraftInput>().Any(input => input.IsTitleDragging))
                {
                    return;
                }
                var root = TopLevel.GetTopLevel(this);
                var suppressed = suppressFocusCommit;
                var revision = focusCommitRevision;
                var document = session.DocumentSnapshot;
                var layerId = session.SelectedLayerId;
                var target = session.SceneEditing.Target;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && revision == focusCommitRevision && layerId == session.SelectedLayerId && target == session.SceneEditing.Target &&
                        ReferenceEquals(document, session.DocumentSnapshot) && !suppressed && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                        this.IsAttachedToVisualTree())
                    {
                        viewModel.CommitDrafts();
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        foreach (var category in this.GetLogicalDescendants().OfType<Expander>())
        {
            category.PropertyChanged += (_, change) =>
            {
                if (change.Property == Expander.IsExpandedProperty)
                {
                    CancelGestures();
                }
            };
        }
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        session.NumericGestureCancellationRequested += OnNumericGestureCancellationRequested;
    }
    public string PanelId => "effects";
    public void CancelGestures()
    {
        suppressFocusCommit = true;
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
        var revision = ++focusCommitRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == focusCommitRevision)
            {
                suppressFocusCommit = false;
            }
        }, DispatcherPriority.Background);
    }
    public void FocusInvalidField(string? fieldKey)
    {
        if (viewModel.HasSelectedEffectInstance && this.FindControl<EffectScriptParameterEditor>("InstanceParameterEditor")!.FocusInvalidField())
        {
            return;
        }
        if (animationOptions.FocusInvalidField(fieldKey))
        {
            return;
        }
        var propertyField = viewModel.ExpandPropertyField(fieldKey);
        var propertyRow = this.GetVisualDescendants().OfType<AnimationPropertyRowControl>().FirstOrDefault(row => row.OwnsField(fieldKey));
        if (propertyRow is not null)
        {
            foreach (var category in propertyRow.GetVisualAncestors().OfType<Expander>())
            {
                category.IsExpanded = true;
            }
            propertyRow.BringIntoView();
            propertyRow.FocusField(fieldKey);
            return;
        }
        if (propertyField)
        {
            var revision = focusCommitRevision;
            var document = session.DocumentSnapshot;
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && revision == focusCommitRevision && ReferenceEquals(document, session.DocumentSnapshot))
                {
                    this.GetVisualDescendants().OfType<AnimationPropertyRowControl>().FirstOrDefault(row => row.OwnsField(fieldKey))?.FocusField(fieldKey);
                }
            }, DispatcherPriority.Loaded);
            return;
        }
        var control = fieldKey is { } key
            ? this.FindControl<Control>(key) ?? this.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == key)
            : null;
        if (control is not null)
        {
            foreach (var category in control.GetVisualAncestors().OfType<Expander>())
            {
                category.IsExpanded = true;
            }
            control.BringIntoView();
        }
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return;
        }
        (control ?? this).Focus();
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        suppressFocusCommit = false;
        if (e.Key == Key.Escape && e.Source is Control source &&
            !source.GetSelfAndVisualAncestors().OfType<ColorDraftInput>().Any() &&
            !source.GetSelfAndVisualAncestors().OfType<EffectScriptParameterEditor>().Any())
        {
            var propertyRow = source.GetSelfAndVisualAncestors().OfType<EffectPropertyRowView>().FirstOrDefault();
            if (propertyRow?.RestoreInput(source) == true)
            {
                e.Handled = true;
                return;
            }
            var maskInput = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
            if (viewModel.RestorePropertyField(maskInput?.Name) || viewModel.RestoreOperationField(maskInput?.Name))
            {
                e.Handled = true;
                return;
            }
            var field = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault() as Control ??
                source.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault(control => control.Name is not null && !control.Name.StartsWith("PART_", StringComparison.Ordinal));
            if (field?.Name is { } name)
            {
                viewModel.RestoreField(name);
                e.Handled = true;
            }
        }
    }

    private void BeginFieldEdit(Control source)
    {
        if (source.GetSelfAndVisualAncestors().OfType<EffectPropertyRowView>().FirstOrDefault()?.DataContext is AnimationPropertyRowViewModel row)
        {
            row.BeginEdit(PanelId);
        }
    }

    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs e) => viewModel.CommitDrafts();

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
        {
            viewModel.CommitDrafts();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EffectsPanelViewModel.Target))
        {
            CancelGestures();
        }
        if (e.PropertyName is nameof(EffectsPanelViewModel.ValidationError) or nameof(EffectsPanelViewModel.InvalidFieldKey))
        {
            foreach (var input in this.GetVisualDescendants().OfType<NumericDraftInput>())
            {
                if (input.GetVisualAncestors().OfType<EffectPropertyRowView>().Any())
                {
                    continue;
                }
                var fieldKey = input.Name;
                DataValidationErrors.SetErrors(input, fieldKey == viewModel.InvalidFieldKey && viewModel.ValidationError is { } message ? new[] { message } : null);
            }
        }
    }

    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    private void OnNumericGestureCancellationRequested(object? sender, EventArgs e)
    {
        ++focusCommitRevision;
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
    }

    private void OnChoicesRefreshing(object? sender, EventArgs e)
    {
        CancelGestures();
        BeginChoiceRefresh(blend);
    }

    private void OnChoicesRefreshed(object? sender, EventArgs e)
    {
        blend.EndInit();
    }

    private void OnInstanceChoicesRefreshing(object? sender, EventArgs e) => effectInstances.BeginInit();

    private void OnInstanceChoicesRefreshed(object? sender, EventArgs e)
    {
        effectInstances.EndInit();
        effectInstances.SetCurrentValue(SelectingItemsControl.SelectedItemProperty, viewModel.SelectedEffectInstance);
    }

    private static void BeginChoiceRefresh(ComboBox choice)
    {
        var selectedIndex = choice.SelectedIndex;
        choice.BeginInit();
        choice.SelectedIndex = selectedIndex;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            animationOptions.Dispose();
            viewModel.ChoicesRefreshing -= OnChoicesRefreshing;
            viewModel.ChoicesRefreshed -= OnChoicesRefreshed;
            viewModel.InstanceChoicesRefreshing -= OnInstanceChoicesRefreshing;
            viewModel.InstanceChoicesRefreshed -= OnInstanceChoicesRefreshed;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            session.NumericGestureCancellationRequested -= OnNumericGestureCancellationRequested;
        }
    }
}
