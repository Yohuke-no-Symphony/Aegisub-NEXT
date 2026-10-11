namespace AegiNext.Core.Projects;

/// <summary>引用物理资源的独立素材项；名称和文件夹变更不修改资源文件。</summary>
public sealed record ProjectAssetLibraryItem(Guid Id, Guid AssetId, string Name, Guid? FolderId = null);
