using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed class AssetLibraryRow(ProjectAssetLibraryItem item, ProjectAsset asset, string directory, int usages)
    : ObservableObject
{
    private string status = string.Empty;
    public Guid Id => item.Id;
    public string Name => item.Name;
    public ProjectAsset Asset { get; } = asset;
    public string Directory { get; } = directory;
    public string UsageLabel => Localization.Format("Workbench.AssetUsages", usages);
    public string StorageLabel => Localization.Get(Asset.Storage == ProjectAssetStorage.LINKED
        ? "Workbench.AssetLinked" : "Workbench.AssetManaged");
    public string Status
    {
        get => status;
        internal set => SetProperty(ref status, value);
    }
}
