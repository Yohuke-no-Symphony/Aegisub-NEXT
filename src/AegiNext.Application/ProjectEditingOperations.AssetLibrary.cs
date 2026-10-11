using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>修改后续图片导入的默认复制策略，不迁移既有素材。</summary>
    public static ProjectDocument SetAssetAutoCopy(ProjectDocument document, bool enabled)
    {
        ProjectValidator.Validate(document);
        return document.AssetLibrary.AutoCopyToSources == enabled ? document :
            Verified(document with { AssetLibrary = document.AssetLibrary with { AutoCopyToSources = enabled } });
    }

    /// <summary>新增逻辑素材文件夹，不访问或创建磁盘目录。</summary>
    public static ProjectDocument AddAssetFolder(ProjectDocument document, ProjectAssetFolder folder)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(folder);
        return Verified(document with { AssetLibrary = document.AssetLibrary with { Folders = document.AssetLibrary.Folders.Add(folder) } });
    }

    /// <summary>重命名逻辑文件夹，保持内容与磁盘资源位置。</summary>
    public static ProjectDocument RenameAssetFolder(ProjectDocument document, Guid folderId, string name)
    {
        return EditAssetFolder(document, folderId, folder => folder with { Name = name });
    }

    /// <summary>移动逻辑文件夹及其完整子树；非法父级或循环整次拒绝。</summary>
    public static ProjectDocument MoveAssetFolder(ProjectDocument document, Guid folderId, Guid? parentId)
    {
        return EditAssetFolder(document, folderId, folder => folder with { ParentId = parentId });
    }

    /// <summary>删除单个逻辑文件夹，将直接子文件夹及素材项移到它的父级。</summary>
    public static ProjectDocument RemoveAssetFolder(ProjectDocument document, Guid folderId)
    {
        ProjectValidator.Validate(document);
        var folder = document.AssetLibrary.Folders.FirstOrDefault(value => value.Id == folderId)
            ?? throw new KeyNotFoundException("素材文件夹不存在。");
        return Verified(document with
        {
            AssetLibrary = document.AssetLibrary with
            {
                Folders = document.AssetLibrary.Folders.Where(value => value.Id != folderId)
                    .Select(value => value.ParentId == folderId ? value with { ParentId = folder.ParentId } : value).ToImmutableArray(),
                Items = document.AssetLibrary.Items.Select(value => value.FolderId == folderId
                    ? value with { FolderId = folder.ParentId } : value).ToImmutableArray()
            }
        });
    }

    /// <summary>登记引用已有图片资源的素材项，允许不同素材项共享同一物理资源。</summary>
    public static ProjectDocument AddAssetLibraryItem(ProjectDocument document, ProjectAssetLibraryItem item)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(item);
        return Verified(document with { AssetLibrary = document.AssetLibrary with { Items = document.AssetLibrary.Items.Add(item) } });
    }

    /// <summary>重命名素材项，不修改源文件名。</summary>
    public static ProjectDocument RenameAssetLibraryItem(ProjectDocument document, Guid itemId, string name)
    {
        return EditAssetLibraryItem(document, itemId, item => item with { Name = name });
    }

    /// <summary>移动素材项到指定逻辑文件夹；空标识表示根目录。</summary>
    public static ProjectDocument MoveAssetLibraryItem(ProjectDocument document, Guid itemId, Guid? folderId)
    {
        return EditAssetLibraryItem(document, itemId, item => item with { FolderId = folderId });
    }

    /// <summary>移除未被片段使用的素材项及失去最后引用的资源记录；不删除文件，撤销和共享引用仍然可用。</summary>
    public static ProjectDocument RemoveUnusedAssetLibraryItems(ProjectDocument document, IReadOnlyCollection<Guid> itemIds)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(itemIds);
        var ids = itemIds.ToHashSet();
        var selected = document.AssetLibrary.Items.Where(item => ids.Contains(item.Id)).ToArray();
        if (selected.Length != ids.Count)
        {
            throw new KeyNotFoundException("素材项不存在。");
        }
        var usedAssets = document.Layers.Where(layer => layer.Image is not null).Select(layer => layer.Image!.AssetId).ToHashSet();
        if (selected.Any(item => usedAssets.Contains(item.AssetId)))
        {
            throw new InvalidOperationException("仍被图片片段使用的素材不能移除。");
        }
        if (ids.Count == 0)
        {
            return document;
        }
        var remaining = document.AssetLibrary.Items.Where(item => !ids.Contains(item.Id)).ToImmutableArray();
        var removableAssets = selected.Select(item => item.AssetId).Except(remaining.Select(item => item.AssetId)).ToHashSet();
        return Verified(document with
        {
            Assets = document.Assets.Where(asset => !removableAssets.Contains(asset.Id)).ToImmutableArray(),
            AssetLibrary = document.AssetLibrary with { Items = remaining }
        });
    }

    private static ProjectDocument EditAssetFolder(ProjectDocument document, Guid id, Func<ProjectAssetFolder, ProjectAssetFolder> edit)
    {
        ProjectValidator.Validate(document);
        var folder = document.AssetLibrary.Folders.FirstOrDefault(value => value.Id == id)
            ?? throw new KeyNotFoundException("素材文件夹不存在。");
        var changed = edit(folder);
        return changed == folder ? document : Verified(document with
        {
            AssetLibrary = document.AssetLibrary with { Folders = document.AssetLibrary.Folders.Replace(folder, changed) }
        });
    }

    private static ProjectDocument EditAssetLibraryItem(ProjectDocument document, Guid id, Func<ProjectAssetLibraryItem, ProjectAssetLibraryItem> edit)
    {
        ProjectValidator.Validate(document);
        var item = document.AssetLibrary.Items.FirstOrDefault(value => value.Id == id)
            ?? throw new KeyNotFoundException("素材项不存在。");
        var changed = edit(item);
        return changed == item ? document : Verified(document with
        {
            AssetLibrary = document.AssetLibrary with { Items = document.AssetLibrary.Items.Replace(item, changed) }
        });
    }
}
