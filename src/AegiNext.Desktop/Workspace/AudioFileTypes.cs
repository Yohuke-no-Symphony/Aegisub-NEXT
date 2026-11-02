using System.Collections.Immutable;

namespace AegiNext.Desktop.Workspace;

/// <summary>“打开音频”对话框与拖放接受的纯音频扩展名；实际可用性仍由探测决定。</summary>
internal static class AudioFileTypes
{
    private static ImmutableArray<string> Extensions { get; } =
        [".wav", ".flac", ".mp3", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".alac", ".ac3", ".eac3", ".dts", ".mka", ".ape", ".wv", ".tta"];

    internal static string[] Patterns => [.. Extensions.Select(extension => "*" + extension)];

    internal static bool SupportsPath(string path)
    {
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
