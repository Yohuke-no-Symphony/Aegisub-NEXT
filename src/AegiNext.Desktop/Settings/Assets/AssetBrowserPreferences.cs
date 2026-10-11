namespace AegiNext.Desktop.Settings.Assets;

/// <summary>素材浏览器的个人显示偏好，不写入项目或撤销历史。</summary>
public sealed record AssetBrowserPreferences
{
    internal const double MINIMUM_THUMBNAIL_SIZE = 48;
    internal const double MAXIMUM_THUMBNAIL_SIZE = 192;
    internal const double MINIMUM_FOLDER_WIDTH = 80;
    internal const double MAXIMUM_FOLDER_WIDTH = 640;
    public AssetBrowserViewMode ViewMode { get; init; }
    public double ThumbnailSize { get; init; } = 64;
    public double FolderPaneWidth { get; init; } = 140;

    /// <summary>检查显示模式、缩略图尺寸和文件夹栏宽度。</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(ViewMode) || !double.IsFinite(ThumbnailSize) ||
            ThumbnailSize is < MINIMUM_THUMBNAIL_SIZE or > MAXIMUM_THUMBNAIL_SIZE ||
            !double.IsFinite(FolderPaneWidth) || FolderPaneWidth is < MINIMUM_FOLDER_WIDTH or > MAXIMUM_FOLDER_WIDTH)
        {
            throw new InvalidDataException("素材浏览器显示偏好无效。");
        }
    }
}
