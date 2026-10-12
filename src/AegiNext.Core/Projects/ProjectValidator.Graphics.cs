namespace AegiNext.Core.Projects;

public static partial class ProjectValidator
{
    /// <summary>验证可独立创建或转换的参数化形状及单轮廓路径。</summary>
    public static void ValidateLayerShape(LayerShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Require(Enum.IsDefined(shape.Kind), "Unknown shape kind.");
        Number(shape.Width, 0.001, 1e6, "Shape width");
        Number(shape.Height, 0.001, 1e6, "Shape height");
        Number(shape.CornerRadius, 0, 1e6, "Corner radius");
        if (shape.CornerRadii is { } radii)
        {
            Number(radii.TopLeft, 0, 1e6, "Top-left corner radius");
            Number(radii.TopRight, 0, 1e6, "Top-right corner radius");
            Number(radii.BottomRight, 0, 1e6, "Bottom-right corner radius");
            Number(radii.BottomLeft, 0, 1e6, "Bottom-left corner radius");
        }
        Require(double.IsFinite(shape.ArrowHeadRatio) && shape.ArrowHeadRatio is > 0 and < 1 &&
            double.IsFinite(shape.ArrowShaftRatio) && shape.ArrowShaftRatio is > 0 and <= 1, "The arrow proportions are invalid.");
        Require((shape.Kind == ShapeKind.PATH) == (shape.Path is not null), "A path shape is missing its path, or a non-path shape contains an unexpected path.");
        if (shape.Path is { } geometry)
        {
            Path(geometry);
        }
    }

    /// <summary>验证源图像归一化裁剪区域，不允许空矩形或超出源图像。</summary>
    public static void ValidateImageCrop(NormalizedImageCrop crop)
    {
        NotNull(crop, "The image crop region cannot be null.");
        Number(crop.X, 0, 1, "Image crop X");
        Number(crop.Y, 0, 1, "Image crop Y");
        Require(double.IsFinite(crop.Width) && double.IsFinite(crop.Height) && crop.Width > 0 && crop.Height > 0 &&
            crop.Width <= 1 - crop.X && crop.Height <= 1 - crop.Y, "The image crop region is empty or exceeds the source image bounds.");
    }

    private static void ValidateAssetLibrary(ProjectAssetLibrary library, Dictionary<Guid, ProjectAsset> assets)
    {
        NotNull(library, "The asset library cannot be null.");
        Require(!library.Folders.IsDefault && !library.Items.IsDefault && library.Folders.Length <= 10000 &&
            library.Items.Length <= 10000, "The asset library collections are invalid or too large.");
        var folders = new Dictionary<Guid, ProjectAssetFolder>();
        var ids = new HashSet<Guid>();
        foreach (var folder in library.Folders)
        {
            NotNull(folder, "An asset folder cannot be null.");
            Require(folder.Id != Guid.Empty && ids.Add(folder.Id), "An asset folder ID is empty or duplicated.");
            ValidateAssetLibraryName(folder.Name);
            folders.Add(folder.Id, folder);
        }
        foreach (var folder in library.Folders)
        {
            var visited = new HashSet<Guid> { folder.Id };
            var parent = folder.ParentId;
            while (parent is { } parentId)
            {
                Require(folders.TryGetValue(parentId, out var ancestor), "An asset folder references a missing parent.");
                Require(visited.Add(parentId) && visited.Count <= 32, "Asset folders form a cycle or exceed 32 levels of nesting.");
                parent = ancestor.ParentId;
            }
        }
        foreach (var item in library.Items)
        {
            NotNull(item, "An asset item cannot be null.");
            Require(item.Id != Guid.Empty && ids.Add(item.Id), "An asset item ID is empty or duplicated.");
            ValidateAssetLibraryName(item.Name);
            Require(item.FolderId is null || folders.ContainsKey(item.FolderId.Value), "An asset item references a missing folder.");
            Asset(assets, item.AssetId, ProjectAssetKind.IMAGE);
        }
    }

    private static void ValidateAssetLibraryName(string name)
    {
        Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl), "An asset name cannot be empty, too long, or contain control characters.");
        ValidateText(name);
    }
}
