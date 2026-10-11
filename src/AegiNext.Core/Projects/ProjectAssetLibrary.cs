using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>工程素材库的逻辑分类及新图片导入策略，不表示磁盘目录结构。</summary>
public sealed record ProjectAssetLibrary
{
    public bool AutoCopyToSources { get; init; } = true;
    public ImmutableArray<ProjectAssetFolder> Folders { get; init; } = [];
    public ImmutableArray<ProjectAssetLibraryItem> Items { get; init; } = [];
}
