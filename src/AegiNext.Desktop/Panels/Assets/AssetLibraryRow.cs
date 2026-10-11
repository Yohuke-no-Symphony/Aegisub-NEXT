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
                OnPropertyChanged(nameof(ListThumbnailSize));
                OnPropertyChanged(nameof(ListThumbnailPixelSize));
            }
        }
    }
    public double IconItemWidth => Math.Max(72, ThumbnailSize + 16);
    public int ThumbnailPixelSize => (int)Math.Ceiling(ThumbnailSize * 2);
    public double ListThumbnailSize => Math.Clamp(ThumbnailSize / 4, 16, 24);
    public int ListThumbnailPixelSize => (int)Math.Ceiling(ListThumbnailSize * 2);
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public string DetailsTooltip => string.Join(Environment.NewLine, new[] { Name, UsageLabel, StorageLabel, Status }
        .Where(value => !string.IsNullOrEmpty(value)));
    public string Status
    {
        get => status;
        internal set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
                OnPropertyChanged(nameof(DetailsTooltip));
            }
        }
    }
}
