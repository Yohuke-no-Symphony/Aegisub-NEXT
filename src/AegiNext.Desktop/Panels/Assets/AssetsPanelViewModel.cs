using System.Collections.ObjectModel;
using System.Windows.Input;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Assets;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed class AssetsPanelViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchSession session;
    private AssetFolderNode? selectedFolder;
    private AssetLibraryRow? selectedItem;
    private string search = string.Empty;
    private bool refreshing;
    private bool checking;
    private bool disposed;
    private readonly CancellationTokenSource availabilityCancellation = new();
    private long itemsRevision;

    internal AssetsPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        ImportCommand = Command(() => session.Assets.PickImportAsync(false, SelectedFolder?.Id));
        PlaceCommand = Command(() => SelectedItem is { } item ? session.Assets.PlaceAsync(item.Id) : Task.CompletedTask);
        CollectCommand = Command(() => session.Assets.CollectAsync());
        AddFolderCommand = Command(() => session.EditAsync(() =>
        {
            var id = Guid.NewGuid();
            session.Editor.Apply("Add asset folder", document => ProjectEditingOperations.AddAssetFolder(document,
                new(id, Localization.Get("Workbench.NewAssetFolder"), SelectedFolder?.Id)));
            SelectedFolder = FindFolder(Folders[0], id);
        }));
        ListViewCommand = new RelayCommand(() => SetViewMode(AssetBrowserViewMode.LIST));
        IconViewCommand = new RelayCommand(() => SetViewMode(AssetBrowserViewMode.ICONS));
        session.Editor.StateChanged += OnDocumentChanged;
        session.PreferencesChanged += OnPreferencesChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    public ObservableCollection<AssetFolderNode> Folders { get; } = [];
    public ObservableCollection<AssetLibraryRow> Items { get; } = [];
    public ICommand ImportCommand { get; }
    public ICommand PlaceCommand { get; }
    public ICommand CollectCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ListViewCommand { get; }
    public ICommand IconViewCommand { get; }
    public bool HasSelectedItem => SelectedItem is not null;
    public bool HasSelectedFolder => SelectedFolder?.Id is not null;
    public bool IsListView => session.Preferences.AssetBrowser.ViewMode == AssetBrowserViewMode.LIST;
    public bool IsIconView => !IsListView;
    public double ThumbnailSize => session.Preferences.AssetBrowser.ThumbnailSize;
    public double FolderPaneWidth => session.Preferences.AssetBrowser.FolderPaneWidth;

    public AssetFolderNode? SelectedFolder
    {
        get => selectedFolder;
        set
        {
            if (SetProperty(ref selectedFolder, value))
            {
                OnPropertyChanged(nameof(HasSelectedFolder));
                if (!refreshing)
                {
                    RefreshItems();
                }
            }
        }
    }
    public AssetLibraryRow? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
            }
        }
    }
    public string Search
    {
        get => search;
        set
        {
            if (SetProperty(ref search, value))
            {
                RefreshItems();
            }
        }
    }
    internal void SetViewMode(AssetBrowserViewMode mode)
    {
        if (disposed)
        {
            return;
        }
        if (session.Preferences.AssetBrowser.ViewMode != mode)
        {
            session.UpdatePreferences(current => current with { AssetBrowser = current.AssetBrowser with { ViewMode = mode } });
        }
        else
        {
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsIconView));
        }
    }

    internal void ResizeThumbnails(double delta)
    {
        if (disposed || !double.IsFinite(delta))
        {
            return;
        }
        var size = Math.Clamp(ThumbnailSize + delta * 8, AssetBrowserPreferences.MINIMUM_THUMBNAIL_SIZE,
            AssetBrowserPreferences.MAXIMUM_THUMBNAIL_SIZE);
        session.UpdatePreferences(current => current with { AssetBrowser = current.AssetBrowser with { ThumbnailSize = size } });
    }

    internal void SetFolderPaneWidth(double width)
    {
        if (disposed || !double.IsFinite(width))
        {
            return;
        }
        width = Math.Clamp(width, AssetBrowserPreferences.MINIMUM_FOLDER_WIDTH, AssetBrowserPreferences.MAXIMUM_FOLDER_WIDTH);
        session.UpdatePreferences(current => current with { AssetBrowser = current.AssetBrowser with { FolderPaneWidth = width } });
    }

    internal AssetFolderEditTarget? CaptureFolderTarget(Guid id) => session.CaptureAssetFolderTarget(id);
    internal Task RenameFolderAsync(AssetFolderEditTarget target) => session.RenameAssetFolderAsync(target);
    internal Task RemoveFolderAsync(AssetFolderEditTarget target) => session.RemoveAssetFolderAsync(target);

    internal AssetItemEditTarget? CaptureItemTarget(Guid id) => session.CaptureAssetItemTarget(id);
    internal bool CanRemoveItem(AssetItemEditTarget target) => session.CanRemoveAssetItem(target);
    internal Task PlaceItemAsync(AssetItemEditTarget target) => session.PlaceAssetItemAsync(target);
    internal Task RenameItemAsync(AssetItemEditTarget target) => session.RenameAssetItemAsync(target);
    internal Task RemoveItemAsync(AssetItemEditTarget target) => session.RemoveAssetItemAsync(target);
    internal Task RefreshItemAsync(AssetItemEditTarget target, bool relocate = false) => session.RefreshAssetItemAsync(target, relocate);

    internal Task MoveItemAsync(Guid itemId, Guid? folderId) => session.RunCommandAsync(() => session.EditAsync(() =>
        session.Editor.Apply("Move asset into folder", document => ProjectEditingOperations.MoveAssetLibraryItem(document, itemId, folderId))));

    internal Task MoveFolderAsync(Guid folderId, Guid? parentId) => session.RunCommandAsync(() => session.EditAsync(() =>
        session.Editor.Apply("Move asset folder", document => ProjectEditingOperations.MoveAssetFolder(document, folderId, parentId))));

    internal async Task CheckAvailabilityAsync()
    {
        if (checking || disposed || session.IsClosing)
        {
            return;
        }
        checking = true;
        var revision = itemsRevision;
        var rows = Items.ToArray();
        try
        {
            foreach (var row in rows)
            {
                if (disposed || session.IsClosing || revision != itemsRevision)
                {
                    break;
                }
                var state = await session.Assets.GetAvailabilityAsync(row.Asset, availabilityCancellation.Token);
                if (disposed || session.IsClosing || revision != itemsRevision)
                {
                    break;
                }
                row.Status = state is null ? string.Empty : Localization.Get(state);
            }
        }
        finally
        {
            checking = false;
        }
    }

    private AsyncRelayCommand Command(Func<Task> execute) => new(() => session.RunCommandAsync(execute));

    private void OnDocumentChanged(object? sender, ProjectEditorChangedEventArgs e) => Refresh();
    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsIconView));
        OnPropertyChanged(nameof(ThumbnailSize));
        OnPropertyChanged(nameof(FolderPaneWidth));
        foreach (var item in Items)
        {
            item.ThumbnailSize = ThumbnailSize;
        }
    }

    private void Refresh()
    {
        var folderId = SelectedFolder?.Id;
        refreshing = true;
        try
        {
            Folders.Clear();
            Folders.Add(BuildFolder(null));
            SelectedFolder = FindFolder(Folders[0], folderId) ?? Folders[0];
        }
        finally
        {
            refreshing = false;
        }
        RefreshItems();
    }

    private AssetFolderNode BuildFolder(Guid? id)
    {
        var library = session.Editor.Snapshot.AssetLibrary;
        return new(id, id is { } folderId ? library.Folders.Single(folder => folder.Id == folderId).Name : Localization.Get("Workbench.AssetRoot"),
            library.Folders.Where(folder => folder.ParentId == id).OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(folder => BuildFolder(folder.Id)).ToArray());
    }

    private static AssetFolderNode? FindFolder(AssetFolderNode node, Guid? id) => node.Id == id ? node :
        node.Children.Select(child => FindFolder(child, id)).FirstOrDefault(child => child is not null);

    private void RefreshItems()
    {
        itemsRevision++;
        var id = SelectedItem?.Id;
        var document = session.Editor.Snapshot;
        Items.Clear();
        foreach (var item in document.AssetLibrary.Items.Where(item =>
                     (string.IsNullOrWhiteSpace(Search) ? item.FolderId == SelectedFolder?.Id : item.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase)))
                     .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var asset = document.Assets.Single(asset => asset.Id == item.AssetId);
            Items.Add(new(item, asset, session.ProjectDirectory, document.Layers.Count(layer => layer.Image?.AssetId == asset.Id))
            {
                ThumbnailSize = ThumbnailSize
            });
        }
        SelectedItem = Items.FirstOrDefault(item => item.Id == id);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        availabilityCancellation.Cancel();
        availabilityCancellation.Dispose();
        session.Editor.StateChanged -= OnDocumentChanged;
        session.PreferencesChanged -= OnPreferencesChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
    }
}
