using System.Security.Cryptography;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

/// <summary>从显式工程目录解析资源并校验可选 SHA-256。</summary>
public sealed class DirectoryProjectAssetResolver : IProjectAssetResolver
{
    private readonly string projectDirectory;

    /// <summary>绑定工程目录。</summary>
    public DirectoryProjectAssetResolver(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        this.projectDirectory = Path.GetFullPath(projectDirectory);
    }

    /// <inheritdoc />
    public Stream Open(ProjectAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string? path = null;
        if (asset.Kind == ProjectAssetKind.IMAGE && asset.Sha256 is { } version)
        {
            var cached = ImageContentSnapshotLocation.Resolve(version);
            if (File.Exists(cached))
            {
                path = cached;
            }
        }
        path ??= ProjectAssetLocation.Resolve(asset, projectDirectory);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length > 256 * 1024 * 1024)
            {
                throw new InvalidDataException("字体或图片资源超过 256 MiB。");
            }

            if (asset.Sha256 is { } expected && !Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("项目资源 SHA-256 校验失败。");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
