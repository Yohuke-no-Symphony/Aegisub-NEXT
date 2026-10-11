namespace AegiNext.Desktop.Workspace;

internal static class ImageFileTypes
{
    internal static string[] Patterns { get; } = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"];

    internal static bool SupportsPath(string path) => Patterns.Any(pattern =>
        string.Equals(Path.GetExtension(path), pattern[1..], StringComparison.OrdinalIgnoreCase));
}
