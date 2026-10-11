using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Styling;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly ListBox list;
    private readonly SubtitlesPanelViewModel viewModel;
    private readonly HashSet<TextBox> caretInputs = [];
    private readonly Avalonia.Data.BindingExpressionBase detailMenuBinding;
    private readonly Avalonia.Data.BindingExpressionBase moveMenuBinding;
    private readonly ContextMenu contextMenu;
    private bool suppressFocusCommit;
    private bool synchronizingSelection;
    private bool disposed;
    private int focusCommitRevision;
    private int errorFocusRevision;
    internal SubtitlesPanelView(SubtitlesPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        var tracks = this.FindControl<ComboBox>("SubtitleTrackCombo")!;
        tracks.SelectionChanged += (_, _) =>
        {
            if (tracks.SelectedItem is AegiNext.Core.Projects.ProjectTrack track)
            {
                viewModel.SelectTrack(track.Id);
            }
        };
        list = this.FindControl<ListBox>("SubtitleList")!;
        list.SelectionChanged += OnSelectionChanged;
        list.AddHandler(KeyDownEvent, OnSubtitleKeyDown, RoutingStrategies.Tunnel);
        var detailItem = new MenuItem { Command = viewModel.DetailsCommand };
        detailMenuBinding = detailItem.Bind(MenuItem.HeaderProperty,
            AegiNext.Desktop.I18n.Localization.Observe("Workbench.SubtitleDetails").ToBinding());
        var moveItem = new MenuItem { Name = "MoveSubtitleRowsMenuItem", Command = viewModel.MoveCommand };
        moveMenuBinding = moveItem.Bind(MenuItem.HeaderProperty,
            AegiNext.Desktop.I18n.Localization.Observe("Workbench.Move").ToBinding());
        contextMenu = new() { Items = { detailItem, moveItem } };
        contextMenu.Opening += OnContextMenuOpening;
        list.ContextMenu = contextMenu;
        InitializeColorTags();
        list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(list).Properties.IsRightButtonPressed && e.Source is Visual source)
            {
                var row = (source as Control)?.DataContext as SubtitleRow ?? source.GetVisualAncestors()
                    .OfType<Control>().Select(control => control.DataContext).OfType<SubtitleRow>().FirstOrDefault();
                if (row is not null)
                {
                    viewModel.FocusRow(row.Id);
                }
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        list.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox { DataContext: SubtitleRow row } box)
            {
                viewModel.FocusRow(row.Id);
                if (box.AcceptsReturn)
                {
                    viewModel.SetCaret(row.Id, box.CaretIndex);
                    if (caretInputs.Add(box))
                    {
                        box.PropertyChanged += OnTextBoxPropertyChanged;
                        box.DetachedFromVisualTree += OnCaretInputDetached;
                    }
                }
            }
        }, RoutingStrategies.Bubble);
        list.AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox { DataContext: SubtitleRow row } box)
            {
                if (box.AcceptsReturn)
                {
                    viewModel.SetCaret(row.Id, box.CaretIndex);
                }
                var root = TopLevel.GetTopLevel(this);
                var suppressed = suppressFocusCommit;
                var revision = focusCommitRevision;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && revision == focusCommitRevision && !suppressed && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                        this.IsAttachedToVisualTree())
                    {
                        viewModel.CommitRow(row);
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        session.SubtitleScrollRequested += OnScrollRequested;
        session.SelectionChanged += OnSessionSelectionChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        SynchronizeSelection();
    }

    public string PanelId => "subtitles";

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        viewModel.SetMoveContext();
        RefreshColorTagMenu();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || session.IsUpdating || disposed)
        {
            return;
        }

        var selected = list.Selection.SelectedItems.OfType<SubtitleRow>().Select(row => row.Id).ToArray();
        var primary = e.AddedItems.OfType<SubtitleRow>().LastOrDefault()?.Id ??
            (viewModel.SelectedRow is { } current && selected.Contains(current.Id) ? current.Id : (list.SelectedItem as SubtitleRow)?.Id);
        synchronizingSelection = true;
        try
        {
            if (!viewModel.SelectRows(primary, selected) && viewModel.InvalidRowId is not null)
            {
                FocusInvalidField(session.ViewModel.InvalidFieldKey);
            }
        }
        finally
        {
            synchronizingSelection = false;
        }

        SynchronizeSelection();
    }

    private void OnSessionSelectionChanged(object? sender, EventArgs e)
    {
        SynchronizeSelection();
        RefreshColorTagContainers();
    }

    private void SynchronizeSelection()
    {
        if (synchronizingSelection || disposed)
        {
            return;
        }

        var ids = viewModel.SelectedIds.ToHashSet();
        var rows = list.Items.OfType<SubtitleRow>().ToArray();
        var selected = list.Selection.SelectedItems.OfType<SubtitleRow>().Select(row => row.Id).ToHashSet();
        if (ids.SetEquals(selected))
        {
            return;
        }

        var anchorId = list.Selection.AnchorIndex >= 0 && list.Selection.AnchorIndex < rows.Length
            ? rows[list.Selection.AnchorIndex].Id : (Guid?)null;
        synchronizingSelection = true;
        try
        {
            using var update = list.Selection.BatchUpdate();
            list.Selection.Clear();
            var primaryIndex = Array.FindIndex(rows, row => row.Id == viewModel.SelectedRow?.Id && ids.Contains(row.Id));
            if (primaryIndex >= 0)
            {
                list.Selection.Select(primaryIndex);
            }

            for (var index = 0; index < rows.Length; index++)
            {
                if (ids.Contains(rows[index].Id) && index != primaryIndex)
                {
                    list.Selection.Select(index);
                }
            }

            var anchorIndex = Array.FindIndex(rows, row => row.Id == anchorId && ids.Contains(row.Id));
            list.Selection.AnchorIndex = anchorIndex >= 0 ? anchorIndex : primaryIndex;
        }
        finally
        {
            synchronizingSelection = false;
        }
    }
    public void CancelGestures()
    {
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
    public void FocusInvalidField(string? fieldKey)
    {
        var row = session.ViewModel.Subtitles.Rows.FirstOrDefault(value => value.Id == session.ViewModel.Subtitles.InvalidRowId);
        if (row is null)
        {
            list.Focus();
            return;
        }
        var revision = ++errorFocusRevision;
        var root = TopLevel.GetTopLevel(this);
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || revision != errorFocusRevision || viewModel.InvalidRowId != row.Id ||
                root is null || !ReferenceEquals(root, TopLevel.GetTopLevel(this)) || !this.IsAttachedToVisualTree())
            {
                return;
            }

            list.ScrollIntoView(row);
            if (fieldKey is "StartText" or "EndText")
            {
                viewModel.HideTimes = false;
            }
            root.UpdateLayout();
            var column = fieldKey switch { "StartText" => 1, "EndText" => 2, _ => 4 };
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => ReferenceEquals(box.DataContext, row) &&
                Grid.GetColumn(box) == column)?.Focus();
        }, DispatcherPriority.Background);
    }
    private void OnTextBoxPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty && sender is TextBox { DataContext: SubtitleRow row } box)
        {
            viewModel.SetCaret(row.Id, box.CaretIndex);
        }
    }
    private void OnCaretInputDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.DetachedFromVisualTree -= OnCaretInputDetached;
            caretInputs.Remove(box);
        }
    }
    private void OnScrollRequested(object? sender, EventArgs e)
    {
        if (list.SelectedItem is { } selected)
        {
            list.ScrollIntoView(selected);
        }
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        disposed = true;
        ReleaseKeyboardRoot();
        ReleaseColorTags();
        list.RemoveHandler(KeyDownEvent, OnSubtitleKeyDown);
        detailMenuBinding.Dispose();
        moveMenuBinding.Dispose();
        contextMenu.Opening -= OnContextMenuOpening;
        contextMenu.Close();
        list.SelectionChanged -= OnSelectionChanged;
        session.SubtitleScrollRequested -= OnScrollRequested;
        session.SelectionChanged -= OnSessionSelectionChanged;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        foreach (var box in caretInputs)
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.DetachedFromVisualTree -= OnCaretInputDetached;
        }
        caretInputs.Clear();
    }
}
