namespace AegiNext.Core.Projects;

/// <summary>工程资源位置及内容指纹；图片显式区分托管副本和相对或绝对文件链接。</summary>
public sealed record ProjectAsset(Guid Id, ProjectAssetKind Kind, string RelativePath, string? Sha256 = null,
    string? ExternalPath = null, ProjectAssetStorage Storage = ProjectAssetStorage.MANAGED);
