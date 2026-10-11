using System.ComponentModel;
using AegiNext.Desktop.Settings.Assets;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private readonly ListBox assetList;
    private readonly TreeView folderTree;
    private readonly ColumnDefinition folderColumn;
    private readonly Grid browserColumns;
    private AssetFolderEditTarget? folderMenuTarget;
    private bool synchronizingWidth;
    private bool? showingIcons;
    private WrapPanel? iconPanel;
    private ScrollViewer? assetScrollViewer;
    private bool disposed;

    internal AssetsPanelView(AssetsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        assetList = this.FindControl<ListBox>("AssetList")!;
        folderTree = this.FindControl<TreeView>("FolderTree")!;
        browserColumns = this.FindControl<Grid>("AssetBrowserColumns")!;
        folderColumn = browserColumns.ColumnDefinitions[0];
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, OnPointerMoved);
        AddHandler(PointerReleasedEvent, (_, _) => CancelGestures());
        assetList.DoubleTapped += OnAssetDoubleTapped;
        assetList.PropertyChanged += OnAssetListPropertyChanged;
        assetList.TemplateApplied += OnAssetListTemplateApplied;
        assetList.AddHandler(PointerWheelChangedEvent, OnAssetWheel, RoutingStrategies.Tunnel);
        folderColumn.PropertyChanged += OnFolderColumnChanged;
        browserColumns.PropertyChanged += OnBrowserColumnsPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        timer.Tick += OnTimer;
        UpdatePresentation();
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
        if (e.Source is not Control source)
        {
            return;
        }
        var contexts = source.GetSelfAndVisualAncestors().OfType<Control>().Select(control => control.DataContext).ToArray();
        var row = contexts.OfType<AssetLibraryRow>().FirstOrDefault();
        var folder = contexts.OfType<AssetFolderNode>().FirstOrDefault();
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            CancelGestures();
            if (source.GetSelfAndVisualAncestors().Contains(folderTree))
            {
                viewModel.SelectedFolder = folder;
            }
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        dragPayload = row is not null ? new(session.Editor.Snapshot.Id, row.Id, false) :
            folder?.Id is { } id ? new(session.Editor.Snapshot.Id, id, true) : null;
        dragStart = e;
        dragPosition = e.GetPosition(this);
    }

    private void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        var row = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(control => control.DataContext).OfType<AssetLibraryRow>().FirstOrDefault();
        if (row is not null)
        {
            viewModel.SelectedItem = row;
            viewModel.PlaceCommand.Execute(null);
        }
    }

    private void OnAssetWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Delta.Y == 0)
        {
            return;
        }
        CancelGestures();
        viewModel.ResizeThumbnails(e.Delta.Y);
        e.Handled = true;
    }

    private void OnFolderMenuOpening(object? sender, CancelEventArgs e)
    {
        CancelGestures();
        folderMenuTarget = viewModel.SelectedFolder?.Id is { } id ? viewModel.CaptureFolderTarget(id) : null;
        e.Cancel = folderMenuTarget is null;
    }

    private void OnFolderMenuClosed(object? sender, RoutedEventArgs e) => folderMenuTarget = null;

    private async void OnRenameFolder(object? sender, RoutedEventArgs e)
    {
        if (folderMenuTarget is { } target)
        {
            await viewModel.RenameFolderAsync(target);
        }
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (folderMenuTarget is { } target)
        {
            await viewModel.RemoveFolderAsync(target);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetsPanelViewModel.IsIconView) or nameof(AssetsPanelViewModel.FolderPaneWidth) or nameof(AssetsPanelViewModel.ThumbnailSize))
        {
            UpdatePresentation();
        }
    }

    private void UpdatePresentation()
    {
        if (showingIcons != viewModel.IsIconView)
        {
            showingIcons = viewModel.IsIconView;
            var selected = viewModel.SelectedItem;
            assetList.ItemTemplate = (IDataTemplate)Resources[showingIcons.Value ? "AssetIconTemplate" : "AssetListTemplate"]!;
            iconPanel = null;
            assetList.ItemsPanel = showingIcons.Value
                ? new FuncTemplate<Panel?>(() => iconPanel = new WrapPanel { Width = IconPanelWidth })
                : new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            ScrollViewer.SetHorizontalScrollBarVisibility(assetList, showingIcons.Value
                ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            assetList.SelectedItem = selected;
        }
        UpdateIconPanelWidth();
        synchronizingWidth = true;
        try
        {
            var maximumWidth = Math.Max(AssetBrowserPreferences.MINIMUM_FOLDER_WIDTH,
                Math.Min(AssetBrowserPreferences.MAXIMUM_FOLDER_WIDTH, browserColumns.Bounds.Width -
                browserColumns.ColumnDefinitions[1].Width.Value - browserColumns.ColumnDefinitions[2].MinWidth));
            folderColumn.MaxWidth = maximumWidth;
            folderColumn.Width = new(Math.Clamp(viewModel.FolderPaneWidth, AssetBrowserPreferences.MINIMUM_FOLDER_WIDTH, maximumWidth));
        }
        finally
        {
            synchronizingWidth = false;
        }
    }

    private void OnBrowserColumnsPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            UpdatePresentation();
        }
    }

    private double IconPanelWidth => Math.Max(Math.Max(88, viewModel.ThumbnailSize + 24) + 12,
        assetScrollViewer?.Viewport.Width ?? assetList.Bounds.Width - assetList.Padding.Left - assetList.Padding.Right -
        assetList.BorderThickness.Left - assetList.BorderThickness.Right);

    private void OnAssetListTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (assetScrollViewer is not null)
        {
            assetScrollViewer.PropertyChanged -= OnAssetScrollViewerPropertyChanged;
        }
        assetScrollViewer = assetList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (assetScrollViewer is not null)
        {
            assetScrollViewer.PropertyChanged += OnAssetScrollViewerPropertyChanged;
        }
        UpdateIconPanelWidth();
    }

    private void OnAssetScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty)
        {
            UpdateIconPanelWidth();
        }
    }

    private void OnAssetListPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            UpdateIconPanelWidth();
        }
    }

    private void UpdateIconPanelWidth()
    {
        if (iconPanel is not null)
        {
            iconPanel.Width = IconPanelWidth;
        }
    }

    private void OnFolderColumnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!synchronizingWidth && e.Property == ColumnDefinition.WidthProperty && folderColumn.Width.IsAbsolute)
        {
            viewModel.SetFolderPaneWidth(folderColumn.Width.Value);
        }
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
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            folderColumn.PropertyChanged -= OnFolderColumnChanged;
            browserColumns.PropertyChanged -= OnBrowserColumnsPropertyChanged;
            assetList.DoubleTapped -= OnAssetDoubleTapped;
            assetList.PropertyChanged -= OnAssetListPropertyChanged;
            assetList.TemplateApplied -= OnAssetListTemplateApplied;
            if (assetScrollViewer is not null)
            {
                assetScrollViewer.PropertyChanged -= OnAssetScrollViewerPropertyChanged;
                assetScrollViewer = null;
            }
            assetList.RemoveHandler(PointerWheelChangedEvent, OnAssetWheel);
            folderTree.ContextMenu?.Close();
            folderMenuTarget = null;
            CancelGestures();
        }
    }
}
