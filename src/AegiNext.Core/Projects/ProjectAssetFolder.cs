namespace AegiNext.Core.Projects;

/// <summary>逻辑素材文件夹；空父标识表示素材库根目录。</summary>
public sealed record ProjectAssetFolder(Guid Id, string Name, Guid? ParentId = null);
