namespace AegiNext.Desktop.Diagnostics;

internal static class WorkspaceProbeProfile
{
    private static readonly string[] sourceFiles = ["preferences.json", "subtitle-styles.aegistyles"];

    internal static void Prepare(WorkspaceProbeOptions options)
    {
        if (Directory.Exists(options.ProfileDirectory))
        {
            throw new InvalidOperationException("诊断目录必须是尚未创建的独立目录。");
        }
        if (options.SourceProfileDirectory is { } sourceDirectory && !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException("诊断来源目录不存在。");
        }

        Directory.CreateDirectory(options.ProfileDirectory);
        if (options.SourceProfileDirectory is not { } source)
        {
            return;
        }
        foreach (var name in sourceFiles)
        {
            var path = Path.Combine(source, name);
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(options.ProfileDirectory, name));
            }
        }
        var effectLibrary = Path.Combine(source, "effect-library");
        if (Directory.Exists(effectLibrary))
        {
            CopyLibrary(effectLibrary, Path.Combine(options.ProfileDirectory, "effect-library"));
        }
    }

    private static void CopyLibrary(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                throw new InvalidDataException("效果库诊断副本不能包含目录链接。");
            }
            CopyLibrary(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
