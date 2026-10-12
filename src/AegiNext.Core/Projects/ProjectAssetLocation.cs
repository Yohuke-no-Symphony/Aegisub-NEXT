namespace AegiNext.Core.Projects;

/// <summary>统一解释工程资源位置；相对路径不离开工程目录，外部媒体可在异机重新绑定。</summary>
public static class ProjectAssetLocation
{
    /// <summary>以工程目录为基准解析本机输入路径；拒绝把异平台绝对引用误当成相对路径。</summary>
    public static string ResolveInputPath(string path, string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        if (IsAbsoluteReference(path) && !Path.IsPathFullyQualified(path))
        {
            throw new NotSupportedException("The external media path belongs to another platform; relink the media.");
        }

        return Path.GetFullPath(path, Path.GetFullPath(projectDirectory));
    }

    /// <summary>尝试生成工程内规范相对路径；工程外或无法跨平台表示的本机路径返回 false。</summary>
    public static bool TryGetRelativePath(string path, string projectDirectory, out string relativePath)
    {
        var source = ResolveInputPath(path, projectDirectory);
        var relative = Path.GetRelativePath(Path.GetFullPath(projectDirectory), source)
            .Replace(Path.DirectorySeparatorChar, '/');
        try
        {
            ProjectValidator.ValidateRelativePath(relative);
            relativePath = relative;
            return true;
        }
        catch (InvalidDataException)
        {
            relativePath = string.Empty;
            return false;
        }
    }

    /// <summary>解析本机可使用的路径；异平台绝对路径保留在工程中，但不能在当前平台解析。</summary>
    public static string Resolve(ProjectAsset asset, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        if (asset.ExternalPath is { } external)
        {
            if (!(asset.Kind == ProjectAssetKind.MEDIA || asset.Kind == ProjectAssetKind.IMAGE && asset.Storage == ProjectAssetStorage.LINKED) ||
                asset.RelativePath != string.Empty || !IsAbsoluteReference(external))
            {
                throw new InvalidDataException("Invalid external asset reference.");
            }

            if (!Path.IsPathFullyQualified(external))
            {
                throw new NotSupportedException("The external asset path belongs to another platform; relocate the asset.");
            }

            return Path.GetFullPath(external);
        }

        ProjectValidator.ValidateRelativePath(asset.RelativePath);
        return Path.GetFullPath(Path.Combine(projectDirectory, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>识别 Unix、Windows 盘符与 UNC 绝对引用，不访问文件系统。</summary>
    public static bool IsAbsoluteReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.StartsWith('/') || value.StartsWith("\\\\", StringComparison.Ordinal) ||
            value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\';
    }
}
