namespace AegiNext.Application.Presets;

internal static class EffectLibraryPath
{
    private const int MAXIMUM_LINK_DEPTH = 40;

    internal static string ResolvePhysicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var links = 0;
        return ResolvePhysicalPath(Path.GetFullPath(path), ref links);
    }

    private static string ResolvePhysicalPath(string fullPath, ref int links)
    {
        var root = Path.GetPathRoot(fullPath)!;
        var resolved = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(resolved, segment);
            FileSystemInfo entry = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            if (entry.LinkTarget is not null)
            {
                links++;
                if (links > MAXIMUM_LINK_DEPTH)
                {
                    throw new InvalidDataException("效果库路径的符号链接层数超过限制。");
                }
                var target = entry.ResolveLinkTarget(false) ?? throw new InvalidDataException("效果库路径的符号链接目标无效。");
                candidate = ResolvePhysicalPath(target.FullName, ref links);
            }
            resolved = candidate;
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }
}
