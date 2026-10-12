namespace AegiNext.Export.Tests;

internal sealed class ExportTestDirectory : IDisposable
{
    internal ExportTestDirectory()
    {
        Path = Directory.CreateTempSubdirectory("aeginext-export-test-").FullName;
    }

    internal string Path { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
