using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView
{
    private TopLevel? keyboardRoot;
    private bool advancingRow;
    private int rowNavigationRevision;
    private ProjectDocument? rowNavigationDocument;
    private SubtitleRowFocusBookmark? rowNavigationFocus;
    private bool rowNavigationFocusChanged;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!disposed)
        {
            keyboardRoot = TopLevel.GetTopLevel(this);
            keyboardRoot?.AddHandler(GotFocusEvent, OnRowNavigationFocusChanged, RoutingStrategies.Bubble);
            if (keyboardRoot is Window window)
            {
                window.Deactivated += OnKeyboardRootDeactivated;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseKeyboardRoot();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>在字幕列表焦点执行试听，在字幕内容输入执行换行和下一行命令，保留文本与输入法输入。</summary>
    public bool OwnsFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.MERGE_SUBTITLE && !disposed && focusedElement is Visual visual &&
               (ReferenceEquals(visual, this) || visual.GetVisualAncestors().Contains(this)) ||
               CanExecuteFocusCommand(command, focusedElement);
    }

    /// <summary>判断列表当前焦点对应的字幕操作能否执行。</summary>
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (disposed || session.IsClosing || session.IsProjectBusy || advancingRow || keyboardRoot is null ||
            list.ContextMenu?.IsOpen == true)
        {
            return false;
        }

        if (command == WorkbenchCommand.MERGE_SUBTITLE)
        {
            return focusedElement is Visual visual &&
                   (ReferenceEquals(visual, this) || visual.GetVisualAncestors().Contains(this)) &&
                   session.CanMergeVisibleSubtitleSelection;
        }

        if (IsSubtitleAuditionCommand(command))
        {
            return focusedElement is Visual focused && focused is not TextBox &&
                   !focused.GetVisualAncestors().OfType<TextBox>().Any() &&
                   (ReferenceEquals(focused, list) || focused.GetVisualAncestors().Contains(list)) &&
                   session.CanAuditionSubtitle && viewModel.SelectedRow is { } selected &&
                   selected.Id == session.SelectedCueId && viewModel.SelectedIds.Contains(selected.Id) &&
                   viewModel.VisibleRows.Any(value => value.Id == selected.Id);
        }

        return (command is WorkbenchCommand.ADVANCE_SUBTITLE_ROW or WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK) &&
               focusedElement is TextBox { AcceptsReturn: true, IsReadOnly: false, DataContext: SubtitleRow row } box &&
               box.GetVisualAncestors().Contains(list) && viewModel.VisibleRows.Any(value => value.Id == row.Id) &&
               !box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }

    /// <summary>试听主选字幕，或在当前内容输入插入换行、提交并跳转后续内容输入。</summary>
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (command == WorkbenchCommand.MERGE_SUBTITLE && OwnsFocusCommand(command, focusedElement))
        {
            if (CanExecuteFocusCommand(command, focusedElement))
            {
                _ = session.MergeVisibleSubtitleSelectionAsync();
            }
            return true;
        }

        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }

        if (IsSubtitleAuditionCommand(command))
        {
            _ = session.ExecuteCommandAsync(command);
            return true;
        }

        var box = (TextBox)focusedElement;
        if (command == WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)
        {
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = "\n" });
        }
        else
        {
            _ = AdvanceRowAndFocusAsync((SubtitleRow)box.DataContext!);
        }
        return true;
    }

    private static bool IsSubtitleAuditionCommand(WorkbenchCommand command) =>
        command is WorkbenchCommand.AUDITION_BEFORE_SUBTITLE or WorkbenchCommand.AUDITION_AFTER_SUBTITLE or
            WorkbenchCommand.AUDITION_SUBTITLE_BEGIN or WorkbenchCommand.AUDITION_SUBTITLE_END or
            WorkbenchCommand.AUDITION_SUBTITLE;

    private void OnSubtitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !disposed && !e.Handled &&
            e.Source is TextBox { AcceptsReturn: true, DataContext: SubtitleRow } box &&
            !box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
        {
            e.Handled = true;
        }
    }

    private async Task AdvanceRowAndFocusAsync(SubtitleRow row)
    {
        if (advancingRow || keyboardRoot is not { } root)
        {
            return;
        }

        var sourceId = row.Id;
        var trackId = session.ClipIndex.GetSubtitleTrackId(row.Id);
        var revision = ++rowNavigationRevision;
        advancingRow = true;
        rowNavigationDocument = session.DocumentSnapshot;
        rowNavigationFocus = null;
        rowNavigationFocusChanged = false;
        try
        {
            var targetId = await viewModel.AdvanceRowAsync(sourceId);
            if (targetId is not { } id || disposed || !this.IsAttachedToVisualTree() ||
                session.CurrentTrackId != trackId || RestoreRowNavigationFocus(id))
            {
                return;
            }

            if (revision != rowNavigationRevision || !ReferenceEquals(root, keyboardRoot) ||
                viewModel.SelectedRow?.Id != id)
            {
                return;
            }

            var focused = root.FocusManager?.GetFocusedElement();
            if (focused is not null && (focused is not TextBox { AcceptsReturn: true, DataContext: SubtitleRow focusedRow } ||
                focusedRow.Id != sourceId && focusedRow.Id != id))
            {
                return;
            }

            var target = viewModel.VisibleRows.FirstOrDefault(value => value.Id == id);
            if (target is not null)
            {
                list.ScrollIntoView(target);
                root.UpdateLayout();
                list.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(input =>
                    input.DataContext is SubtitleRow value && value.Id == id && input.AcceptsReturn)?.Focus();
            }
        }
        finally
        {
            rowNavigationDocument = null;
            rowNavigationFocus = null;
            rowNavigationFocusChanged = false;
            advancingRow = false;
        }
    }

    private void OnRowNavigationFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (!advancingRow || disposed || session.IsProjectBusy ||
            !ReferenceEquals(rowNavigationDocument, session.DocumentSnapshot) || keyboardRoot is not { } root)
        {
            return;
        }

        rowNavigationFocusChanged = true;
        rowNavigationFocus = e.Source is TextBox { DataContext: SubtitleRow row } box &&
            box.GetVisualAncestors().Contains(list)
            ? new(row.Id, Grid.GetColumn(box), root, rowNavigationRevision, box.CaretIndex, box.SelectionStart, box.SelectionEnd)
            : null;
    }

    private bool RestoreRowNavigationFocus(Guid targetId)
    {
        if (!rowNavigationFocusChanged)
        {
            return false;
        }

        if (rowNavigationFocus is not { } bookmark || keyboardRoot is not { } root ||
            !ReferenceEquals(bookmark.Root, root) || bookmark.Revision != rowNavigationRevision)
        {
            return true;
        }

        var focused = root.FocusManager.GetFocusedElement();
        if (focused is not null && (focused is not TextBox { AcceptsReturn: true, DataContext: SubtitleRow focusedRow } focusedBox ||
            focusedRow.Id != targetId || !focusedBox.GetVisualAncestors().Contains(list)))
        {
            return true;
        }

        var target = viewModel.VisibleRows.FirstOrDefault(value => value.Id == bookmark.RowId);
        if (target is not null)
        {
            list.ScrollIntoView(target);
            root.UpdateLayout();
            var input = list.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(value =>
                value.DataContext is SubtitleRow row && row.Id == bookmark.RowId && Grid.GetColumn(value) == bookmark.Column);
            if (input?.Focus() == true)
            {
                input.CaretIndex = bookmark.CaretIndex;
                input.SelectionStart = bookmark.SelectionStart;
                input.SelectionEnd = bookmark.SelectionEnd;
            }
        }

        return true;
    }

    private void OnKeyboardRootDeactivated(object? sender, EventArgs e)
    {
        rowNavigationRevision++;
    }

    private void ReleaseKeyboardRoot()
    {
        rowNavigationRevision++;
        if (keyboardRoot is { } root)
        {
            root.RemoveHandler(GotFocusEvent, OnRowNavigationFocusChanged);
            if (root is Window window)
            {
                window.Deactivated -= OnKeyboardRootDeactivated;
            }
            keyboardRoot = null;
        }
    }
}
