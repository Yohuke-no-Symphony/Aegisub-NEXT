using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>按合并内容的实际依赖整批准备字体和图片，发布前不修改目标工程资源。</summary>
public static class ProjectMergeResources
{
    /// <summary>验证来源并将所需资源复制到暂存目录；租约接受前的失败会清理本次新建文件。</summary>
    public static async Task<PreparedProjectMergeResources> PrepareAsync(IReadOnlyList<ProjectMergeSource> sources,
        string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(destinationDirectory);
        RejectSymbolicLink(destination);
        RejectSymbolicLink(Path.Combine(destination, "assets"));
        RejectSymbolicLink(Path.Combine(destination, "sources"));
        var capturedSources = sources.ToArray();
        var dependencies = new List<HashSet<Guid>>(capturedSources.Length);
        foreach (var source in capturedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Directory);
            dependencies.Add(ProjectEditingOperations.GetMergeAssetIds(source.Document).ToHashSet());
        }

        var staging = Path.Combine(Path.GetTempPath(), $"aeginext-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var preparedSources = ImmutableArray.CreateBuilder<ProjectMergeSource>(capturedSources.Length);
            var files = new Dictionary<string, ProjectMergeResourceFile>(StringComparer.Ordinal);
            for (var index = 0; index < capturedSources.Length; index++)
            {
                var source = capturedSources[index];
                var required = dependencies[index];
                var assets = source.Document.Assets.ToBuilder();
                for (var assetIndex = 0; assetIndex < assets.Count; assetIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var asset = assets[assetIndex];
                    if (!required.Contains(asset.Id))
                    {
                        continue;
                    }

                    if (asset.Kind == ProjectAssetKind.IMAGE && asset.Storage == ProjectAssetStorage.LINKED)
                    {
                        assets[assetIndex] = ProjectResources.RebaseMedia(asset, source.Directory, destination);
                        continue;
                    }
                    var path = ProjectAssetLocation.Resolve(asset, source.Directory);
                    RejectSourceSymbolicLinks(asset, source.Directory);
                    var imported = await ProjectResources.ImportAsync(path, asset.Kind, staging, cancellationToken).ConfigureAwait(false);
                    if (asset.Kind == ProjectAssetKind.IMAGE && asset.RelativePath.StartsWith("sources/", StringComparison.Ordinal))
                    {
                        var relative = "sources/" + Path.GetFileName(imported.RelativePath);
                        var target = Path.Combine(staging, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (!File.Exists(target))
                        {
                            File.Copy(ProjectAssetLocation.Resolve(imported, staging), target);
                        }
                        imported = imported with { RelativePath = relative };
                    }
                    if (asset.Sha256 is { } expected &&
                        !string.Equals(expected, imported.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("合并资源内容与来源工程记录的 SHA-256 不一致。");
                    }

                    assets[assetIndex] = imported with { Id = asset.Id };
                    files.TryAdd(imported.RelativePath, new(imported.RelativePath, imported.Sha256!));
                }

                var document = source.Document with { Assets = assets.ToImmutable() };
                ProjectValidator.Validate(document);
                preparedSources.Add(source with { Document = document });
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(preparedSources.ToImmutable(), files.Values.ToImmutableArray(), destination, staging);
        }
        catch (Exception error)
        {
            try
            {
                Directory.Delete(staging, true);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("合并资源准备失败，清理暂存文件时也发生错误。", error, cleanupError);
            }

            throw;
        }
    }

    internal static void RejectSymbolicLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException("合并资源路径不能是符号链接。");
        }
    }

    private static void RejectSourceSymbolicLinks(ProjectAsset asset, string sourceDirectory)
    {
        var current = Path.GetFullPath(sourceDirectory);
        RejectSymbolicLink(current);
        foreach (var component in asset.RelativePath.Split('/'))
        {
            current = Path.Combine(current, component);
            RejectSymbolicLink(current);
        }
    }
}
