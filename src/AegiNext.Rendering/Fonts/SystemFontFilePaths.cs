using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AegiNext.Rendering.Fonts;

internal static class SystemFontFilePaths
{
    private static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc", ".otc" };

    internal static IEnumerable<string> Get(IEnumerable<SystemFontFace> faces)
    {
        if (OperatingSystem.IsMacOS())
        {
            return faces.Select(face => MacFontFileLocator.GetPath(face.Variant.PostScriptName ?? face.SourceFamilyName))
                .Where(path => path is not null && extensions.Contains(Path.GetExtension(path))).Select(path => path!);
        }
        if (OperatingSystem.IsWindows())
        {
            return WindowsPaths();
        }
        var directories = new[]
        {
            "/usr/share/fonts", "/usr/local/share/fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")
        };
        return directories.Where(Directory.Exists).SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)));
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> WindowsPaths()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var userDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistryPaths(Registry.LocalMachine, systemDirectory, result);
        AddRegistryPaths(Registry.CurrentUser, userDirectory, result);
        foreach (var directory in new[] { systemDirectory, userDirectory }.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (extensions.Contains(Path.GetExtension(path)))
                {
                    result.Add(path);
                }
            }
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryPaths(RegistryKey root, string directory, HashSet<string> result)
    {
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        if (key is null)
        {
            return;
        }
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string value)
            {
                continue;
            }
            var path = Path.IsPathFullyQualified(value) ? value : Path.Combine(directory, value);
            if (extensions.Contains(Path.GetExtension(path)) && File.Exists(path))
            {
                result.Add(path);
            }
        }
    }
}
