using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Startup;

internal sealed class PersonalLibraryPaths
{
    internal PersonalLibraryPaths(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
    }

    internal string DirectoryPath { get; }
    internal string EffectLibraryDirectory => Path.Combine(DirectoryPath, "effect-library");
    internal string FontsDirectory => Path.Combine(DirectoryPath, "fonts");
    internal string EffectLibraryManifestFile => Path.Combine(EffectLibraryDirectory, "manifest.json");
    internal string LegacyStyleFile => Path.Combine(DirectoryPath, "subtitle-styles.aegistyles");
    internal string LegacyEffectFile => Path.Combine(DirectoryPath, "effect-scripts.json");
    internal string ExportPresetFile => Path.Combine(DirectoryPath, "export-presets.aegiexports");
    internal string ColorTagFile => Path.Combine(DirectoryPath, "subtitle-color-tags.json");

    internal void RequireExternalExportPath(string path)
    {
        var destination = UserSettingsTransferFiles.ResolvePhysicalPath(path);
        var root = UserSettingsTransferFiles.ResolvePhysicalPath(EffectLibraryDirectory);
        var fontRoot = UserSettingsTransferFiles.ResolvePhysicalPath(FontsDirectory);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(destination, root, comparison) ||
            string.Equals(destination, fontRoot, comparison) ||
            destination.StartsWith(fontRoot + Path.DirectorySeparatorChar, comparison) ||
            destination.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
            string.Equals(destination, UserSettingsTransferFiles.ResolvePhysicalPath(LegacyStyleFile), comparison) ||
            string.Equals(destination, UserSettingsTransferFiles.ResolvePhysicalPath(LegacyEffectFile), comparison))
        {
            throw new InvalidDataException("导出位置不能位于正在使用的效果库目录中。");
        }
    }

    internal string GetLibraryPath(PersonalLibraryKind kind)
    {
        return kind switch
        {
            PersonalLibraryKind.STYLE or PersonalLibraryKind.EFFECT or PersonalLibraryKind.EFFECT_LIBRARY => EffectLibraryDirectory,
            PersonalLibraryKind.EXPORT => ExportPresetFile,
            PersonalLibraryKind.COLOR_TAG => ColorTagFile,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
