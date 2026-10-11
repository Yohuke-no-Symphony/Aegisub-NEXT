using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed partial class AssetsPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly AssetsPanelViewModel viewModel;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PointerPressedEventArgs? dragStart;
    private Point dragPosition;
    private AssetDragPayload? dragPayload;
    private bool disposed;

    internal AssetsPanelView(AssetsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(PointerPressedEvent, OnPointerPressed);
        AddHandler(PointerMovedEvent, OnPointerMoved);
        AddHandler(PointerReleasedEvent, (_, _) => CancelGestures());
        this.FindControl<ListBox>("AssetList")!.DoubleTapped += (_, _) => viewModel.PlaceCommand.Execute(null);
        timer.Tick += OnTimer;
    }

    public string PanelId => "assets";
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement) =>
        command == WorkbenchCommand.PASTE_CLIPS && focusedElement is Control and not TextBox &&
        !session.IsProjectBusy && TopLevel.GetTopLevel(this)?.Clipboard is not null;

    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        _ = session.RunCommandAsync(() => session.Assets.PasteAsync(TopLevel.GetTopLevel(this)!.Clipboard!,
            false, viewModel.SelectedFolder?.Id));
        return true;
    }
    public void CancelGestures()
    {
        dragStart = null;
        dragPayload = null;
    }
    public void FocusInvalidField(string? fieldKey) => this.FindControl<ListBox>("AssetList")!.Focus();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer.Start();
        _ = session.RunCommandAsync(viewModel.CheckAvailabilityAsync);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        CancelGestures();
        base.OnDetachedFromVisualTree(e);
    }
    private void OnTimer(object? sender, EventArgs e) => _ = session.RunCommandAsync(viewModel.CheckAvailabilityAsync);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Control source)
        {
            return;
        }
        var contexts = source.GetSelfAndVisualAncestors().OfType<Control>().Select(control => control.DataContext);
        var row = contexts.OfType<AssetLibraryRow>().FirstOrDefault();
        var folder = contexts.OfType<AssetFolderNode>().FirstOrDefault();
        dragPayload = row is not null ? new(session.Editor.Snapshot.Id, row.Id, false) :
            folder?.Id is { } id ? new(session.Editor.Snapshot.Id, id, true) : null;
        dragStart = e;
        dragPosition = e.GetPosition(this);
    }
    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragStart is not { } start || dragPayload is not { } payload ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ((Vector)(e.GetPosition(this) - dragPosition)).Length < 5)
        {
            return;
        }
        CancelGestures();
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(AssetDragPayload.Format, payload));
        await DragDrop.DoDragDropAsync(start, data, payload.IsFolder ? DragDropEffects.Move : DragDropEffects.Copy | DragDropEffects.Move);
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var payload = e.DataTransfer.TryGetValue(AssetDragPayload.Format);
        var supported = payload is not null && payload.ProjectId == session.Editor.Snapshot.Id || GetFiles(e).Length > 0;
        e.DragEffects = supported ? payload is null ? DragDropEffects.Copy : DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var target = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(control => control.DataContext).OfType<AssetFolderNode>().FirstOrDefault();
        var folderId = target is null ? viewModel.SelectedFolder?.Id : target.Id;
        var payload = e.DataTransfer.TryGetValue(AssetDragPayload.Format);
        if (payload is not null && payload.ProjectId == session.Editor.Snapshot.Id)
        {
            if (payload.IsFolder)
            {
                await viewModel.MoveFolderAsync(payload.Id, folderId);
            }
            else
            {
                await viewModel.MoveItemAsync(payload.Id, folderId);
            }
            return;
        }
        var paths = GetFiles(e);
        if (paths.Length > 0)
        {
            await session.RunCommandAsync(() => session.Assets.ImportAsync(paths, false, folderId));
        }
    }
    private static string[] GetFiles(DragEventArgs e) => e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
        .Select(file => file.TryGetLocalPath()).Where(path => path is not null && ImageFileTypes.SupportsPath(path)).Cast<string>().ToArray() ?? [];

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            timer.Stop();
            timer.Tick -= OnTimer;
            CancelGestures();
        }
    }
}
