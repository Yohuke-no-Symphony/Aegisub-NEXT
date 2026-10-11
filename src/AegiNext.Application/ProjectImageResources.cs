using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>按显式导入策略准备图片；逻辑分类与文件路径独立，已接受版本使用不可变缓存。</summary>
public static class ProjectImageResources
{
    /// <summary>准备一批托管或链接图片，目标工程在 CommitAsync 前保持不变。</summary>
    public static Task<PreparedProjectImages> PrepareAsync(IReadOnlyList<ProjectImageImport> imports,
        string projectDirectory, CancellationToken cancellationToken = default) => PrepareAsync(imports, projectDirectory, [], cancellationToken);

    /// <summary>复用工程内已知内容的存储路径，避免不同文件名或扩展名造成重复文件。</summary>
    public static async Task<PreparedProjectImages> PrepareAsync(IReadOnlyList<ProjectImageImport> imports,
        string projectDirectory, IReadOnlyList<ProjectAsset> knownAssets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(knownAssets);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(projectDirectory);
        var staging = Path.Combine(Path.GetTempPath(), $"aeginext-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var assets = ImmutableArray.CreateBuilder<ProjectAsset>(imports.Count);
            var files = new Dictionary<string, ProjectMergeResourceFile>(StringComparer.Ordinal);
            var contentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in knownAssets)
            {
                if (existing.Kind == ProjectAssetKind.IMAGE && existing.Storage == ProjectAssetStorage.MANAGED &&
                    existing.Sha256 is { } digest && existing.RelativePath.StartsWith("sources/", StringComparison.Ordinal))
                {
                    ProjectValidator.ValidateRelativePath(existing.RelativePath);
                    contentPaths.TryAdd(digest, existing.RelativePath);
                }
            }
            foreach (var request in imports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ProjectAssetLocation.ResolveInputPath(request.SourcePath, destination);
                var imported = await ProjectResources.ImportAsync(source, ProjectAssetKind.IMAGE, staging,
                    cancellationToken).ConfigureAwait(false);
                if (request.ExpectedSha256 is { } expected &&
                    !string.Equals(expected, imported.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("图片内容与已接受版本不同，请先刷新素材。");
                }
                var stagedPath = ProjectAssetLocation.Resolve(imported, staging);
                await CacheSnapshotAsync(stagedPath, imported.Sha256!, cancellationToken).ConfigureAwait(false);
                var asset = imported with { Id = request.AssetId ?? imported.Id };
                if (request.CopyToProject)
                {
                    var extension = Path.GetExtension(request.FileName ?? source).ToLowerInvariant();
                    if (extension.Length > 16 || extension.Any(character => character != '.' && !char.IsAsciiLetterOrDigit(character)))
                    {
                        extension = ".bin";
                    }
                    var relative = contentPaths.GetValueOrDefault(imported.Sha256!) ?? $"sources/{imported.Sha256}{extension}";
                    contentPaths.TryAdd(imported.Sha256!, relative);
                    var target = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (!File.Exists(target))
                    {
                        File.Copy(stagedPath, target);
                    }
                    asset = asset with { RelativePath = relative, ExternalPath = null, Storage = ProjectAssetStorage.MANAGED };
                    files.TryAdd(relative, new(relative, imported.Sha256!));
                }
                else
                {
                    asset = ProjectAssetLocation.TryGetRelativePath(source, destination, out var relative)
                        ? asset with { RelativePath = relative, ExternalPath = null, Storage = ProjectAssetStorage.LINKED }
                        : asset with { RelativePath = string.Empty, ExternalPath = source, Storage = ProjectAssetStorage.LINKED };
                }
                assets.Add(asset);
            }
            return new(assets.ToImmutable(), new(files.Values.ToArray(), destination, staging));
        }
        catch
        {
            Directory.Delete(staging, true);
            throw;
        }
    }

    /// <summary>比较链接源与已接受版本，不更改工程或缓存。</summary>
    public static async Task<bool> HasChangedAsync(ProjectAsset asset, string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = ProjectAssetLocation.Resolve(asset, projectDirectory);
        if (!File.Exists(path))
        {
            return true;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return !string.Equals(asset.Sha256, hash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CacheSnapshotAsync(string source, string hash, CancellationToken cancellationToken)
    {
        var target = ImageContentSnapshotLocation.Resolve(hash);
        ProjectMergeResources.RejectSymbolicLink(ImageContentSnapshotLocation.DirectoryPath);
        Directory.CreateDirectory(ImageContentSnapshotLocation.DirectoryPath);
        ProjectMergeResources.RejectSymbolicLink(target);
        if (File.Exists(target))
        {
            await VerifySnapshotAsync(target, hash, cancellationToken).ConfigureAwait(false);
            return;
        }
        var temporary = Path.Combine(ImageContentSnapshotLocation.DirectoryPath, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporary, target);
            }
            catch (IOException) when (File.Exists(target))
            {
                await VerifySnapshotAsync(target, hash, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task VerifySnapshotAsync(string path, string hash, CancellationToken cancellationToken)
    {
        ProjectMergeResources.RejectSymbolicLink(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("图片版本缓存的内容指纹不匹配。");
        }
    }
}
