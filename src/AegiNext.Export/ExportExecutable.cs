namespace AegiNext.Export;

internal static class ExportExecutable
{
    internal static string Resolve(string name, string? explicitPath = null)
    {
        if (name == "ffmpeg")
        {
            return AegiNext.Media.Probing.MediaToolchain.ResolveFfmpeg(explicitPath);
        }

        if (explicitPath is not null)
        {
            if (!Path.IsPathFullyQualified(explicitPath) || !File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"需要存在的 {name} 完整路径。", explicitPath);
            }

            return Path.GetFullPath(explicitPath);
        }

        var executable = name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        foreach (var directory in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools") }
                     .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)))
        {
            var path = Path.Combine(directory, executable);
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        throw new FileNotFoundException($"未找到 {name}，请配置其完整路径。");
    }
}
