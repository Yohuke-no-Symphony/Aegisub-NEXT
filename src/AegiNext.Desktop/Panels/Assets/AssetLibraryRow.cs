using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed class AssetLibraryRow(ProjectAssetLibraryItem item, ProjectAsset asset, string directory, int usages)
    : ObservableObject
{
    private string status = string.Empty;
    private double thumbnailSize = 64;
    public Guid Id => item.Id;
    public string Name => item.Name;
    public ProjectAsset Asset { get; } = asset;
    public string Directory { get; } = directory;
    public string UsageLabel => Localization.Format("Workbench.AssetUsages", usages);
    public string StorageLabel => Localization.Get(Asset.Storage == ProjectAssetStorage.LINKED
        ? "Workbench.AssetLinked" : "Workbench.AssetManaged");
    public double ThumbnailSize
    {
        get => thumbnailSize;
        internal set
        {
            if (SetProperty(ref thumbnailSize, value))
            {
                OnPropertyChanged(nameof(IconItemWidth));
                OnPropertyChanged(nameof(ThumbnailPixelSize));
            }
        }
    }
    public double IconItemWidth => Math.Max(88, ThumbnailSize + 24);
    public int ThumbnailPixelSize => (int)Math.Ceiling(ThumbnailSize * 2);
    public string Status
    {
        get => status;
        internal set => SetProperty(ref status, value);
    }
}
