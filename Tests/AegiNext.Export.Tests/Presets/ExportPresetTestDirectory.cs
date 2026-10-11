namespace AegiNext.Export.Tests.Presets;

internal sealed class ExportPresetTestDirectory : IDisposable
{
    internal ExportPresetTestDirectory()
    {
        Path = Directory.CreateTempSubdirectory("aeginext-export-presets-").FullName;
    }

    internal string Path { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
