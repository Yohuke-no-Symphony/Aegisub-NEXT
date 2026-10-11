namespace AegiNext.Core.Projects;

public static partial class ProjectValidator
{
    /// <summary>验证可独立创建或转换的参数化形状及单轮廓路径。</summary>
    public static void ValidateLayerShape(LayerShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Require(Enum.IsDefined(shape.Kind), "未知形状。");
        Number(shape.Width, 0.001, 1e6, "形状宽度");
        Number(shape.Height, 0.001, 1e6, "形状高度");
        Number(shape.CornerRadius, 0, 1e6, "圆角半径");
        if (shape.CornerRadii is { } radii)
        {
            Number(radii.TopLeft, 0, 1e6, "左上圆角半径");
            Number(radii.TopRight, 0, 1e6, "右上圆角半径");
            Number(radii.BottomRight, 0, 1e6, "右下圆角半径");
            Number(radii.BottomLeft, 0, 1e6, "左下圆角半径");
        }
        Require(double.IsFinite(shape.ArrowHeadRatio) && shape.ArrowHeadRatio is > 0 and < 1 &&
            double.IsFinite(shape.ArrowShaftRatio) && shape.ArrowShaftRatio is > 0 and <= 1, "箭头比例无效。");
        Require((shape.Kind == ShapeKind.PATH) == (shape.Path is not null), "路径形状缺少路径或其他形状含多余路径。");
        if (shape.Path is { } geometry)
        {
            Path(geometry);
        }
    }

    /// <summary>验证源图像归一化裁剪区域，不允许空矩形或超出源图像。</summary>
    public static void ValidateImageCrop(NormalizedImageCrop crop)
    {
        NotNull(crop, "图片裁剪区域不能为空。");
        Number(crop.X, 0, 1, "图片裁剪 X");
        Number(crop.Y, 0, 1, "图片裁剪 Y");
        Require(double.IsFinite(crop.Width) && double.IsFinite(crop.Height) && crop.Width > 0 && crop.Height > 0 &&
            crop.Width <= 1 - crop.X && crop.Height <= 1 - crop.Y, "图片裁剪区域为空或超出源图像。");
    }

    private static void ValidateAssetLibrary(ProjectAssetLibrary library, Dictionary<Guid, ProjectAsset> assets)
    {
        NotNull(library, "素材库不能为空。");
        Require(!library.Folders.IsDefault && !library.Items.IsDefault && library.Folders.Length <= 10000 &&
            library.Items.Length <= 10000, "素材库集合无效或过大。");
        var folders = new Dictionary<Guid, ProjectAssetFolder>();
        var ids = new HashSet<Guid>();
        foreach (var folder in library.Folders)
        {
            NotNull(folder, "素材文件夹不能为空。");
            Require(folder.Id != Guid.Empty && ids.Add(folder.Id), "素材文件夹标识为空或重复。");
            ValidateAssetLibraryName(folder.Name);
            folders.Add(folder.Id, folder);
        }
        foreach (var folder in library.Folders)
        {
            var visited = new HashSet<Guid> { folder.Id };
            var parent = folder.ParentId;
            while (parent is { } parentId)
            {
                Require(folders.TryGetValue(parentId, out var ancestor), "素材文件夹引用不存在的父级。");
                Require(visited.Add(parentId) && visited.Count <= 32, "素材文件夹存在循环或超过 32 级。");
                parent = ancestor.ParentId;
            }
        }
        foreach (var item in library.Items)
        {
            NotNull(item, "素材项不能为空。");
            Require(item.Id != Guid.Empty && ids.Add(item.Id), "素材项标识为空或重复。");
            ValidateAssetLibraryName(item.Name);
            Require(item.FolderId is null || folders.ContainsKey(item.FolderId.Value), "素材项引用不存在的文件夹。");
            Asset(assets, item.AssetId, ProjectAssetKind.IMAGE);
        }
    }

    private static void ValidateAssetLibraryName(string name)
    {
        Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl), "素材名称不能为空、过长或包含控制字符。");
        ValidateText(name);
    }
}
