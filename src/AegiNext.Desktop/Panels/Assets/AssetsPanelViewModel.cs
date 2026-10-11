using System.Collections.ObjectModel;
using System.Windows.Input;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
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
    private string nameDraft = string.Empty;
    private string folderNameDraft = string.Empty;
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
        RefreshCommand = Command(() => SelectedItem is { } item ? session.Assets.RefreshAssetAsync(item.Asset.Id) : Task.CompletedTask);
        RelocateCommand = Command(() => SelectedItem is { } item ? session.Assets.RefreshAssetAsync(item.Asset.Id, true) : Task.CompletedTask);
        CollectCommand = Command(() => session.Assets.CollectAsync());
        AddFolderCommand = Command(() => session.EditAsync(() =>
        {
            var id = Guid.NewGuid();
            session.Editor.Apply("Add asset folder", document => ProjectEditingOperations.AddAssetFolder(document,
                new(id, Localization.Get("Workbench.NewAssetFolder"), SelectedFolder?.Id)));
            SelectedFolder = FindFolder(Folders[0], id);
        }));
        RenameFolderCommand = EditCommand(document => SelectedFolder?.Id is { } id
            ? ProjectEditingOperations.RenameAssetFolder(document, id, FolderNameDraft.Trim()) : document);
        RemoveFolderCommand = EditCommand(document => SelectedFolder?.Id is { } id
            ? ProjectEditingOperations.RemoveAssetFolder(document, id) : document);
        RenameItemCommand = EditCommand(document => SelectedItem is { } item
            ? ProjectEditingOperations.RenameAssetLibraryItem(document, item.Id, NameDraft.Trim()) : document);
        RemoveItemCommand = EditCommand(document => SelectedItem is { } item
            ? ProjectEditingOperations.RemoveUnusedAssetLibraryItems(document, [item.Id]) : document);
        session.Editor.StateChanged += OnDocumentChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    public ObservableCollection<AssetFolderNode> Folders { get; } = [];
    public ObservableCollection<AssetLibraryRow> Items { get; } = [];
    public ICommand ImportCommand { get; }
    public ICommand PlaceCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RelocateCommand { get; }
    public ICommand CollectCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RenameFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand RenameItemCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public bool HasSelectedItem => SelectedItem is not null;
    public bool HasSelectedFolder => SelectedFolder?.Id is not null;

    public AssetFolderNode? SelectedFolder
    {
        get => selectedFolder;
        set
        {
            if (SetProperty(ref selectedFolder, value))
            {
                OnPropertyChanged(nameof(HasSelectedFolder));
                FolderNameDraft = value?.Id is null ? string.Empty : value.Name;
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
                NameDraft = value?.Name ?? string.Empty;
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
    public string NameDraft { get => nameDraft; set => SetProperty(ref nameDraft, value); }
    public string FolderNameDraft { get => folderNameDraft; set => SetProperty(ref folderNameDraft, value); }
    public bool AutoCopyToSources
    {
        get => session.Editor.Snapshot.AssetLibrary.AutoCopyToSources;
        set
        {
            if (!refreshing && value != AutoCopyToSources)
            {
                _ = session.RunCommandAsync(() => session.EditAsync(() => session.Editor.Apply("Set image import policy",
                    document => document with { AssetLibrary = document.AssetLibrary with { AutoCopyToSources = value } })));
            }
        }
    }

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
    private AsyncRelayCommand EditCommand(Func<ProjectDocument, ProjectDocument> update) => Command(() => session.EditAsync(() =>
        session.Editor.Apply("Edit asset library", update)));

    private void OnDocumentChanged(object? sender, ProjectEditorChangedEventArgs e) => Refresh();
    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var folderId = SelectedFolder?.Id;
        refreshing = true;
        try
        {
            Folders.Clear();
            Folders.Add(BuildFolder(null));
            SelectedFolder = FindFolder(Folders[0], folderId) ?? Folders[0];
            OnPropertyChanged(nameof(AutoCopyToSources));
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
            Items.Add(new(item, asset, session.ProjectDirectory, document.Layers.Count(layer => layer.Image?.AssetId == asset.Id)));
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
        Localization.LanguageChanged -= OnLanguageChanged;
    }
}
