namespace AegiNext.Export;

internal sealed class ExportOutputTransaction : IDisposable
{
    private bool disposed;

    internal ExportOutputTransaction(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException("The output file must use an absolute path.", nameof(outputPath));
        }

        OutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(OutputPath))
        {
            throw new IOException("The output file already exists; choose a new filename.");
        }

        var directory = Path.GetDirectoryName(OutputPath)!;
        Directory.CreateDirectory(directory);
        TemporaryDirectory = Path.Combine(directory, $".aeginext-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TemporaryDirectory);
        Extension = Path.GetExtension(OutputPath).ToLowerInvariant();
        TemporaryOutputPath = Path.Combine(TemporaryDirectory, "output" + Extension);
    }

    internal string OutputPath { get; }
    internal string TemporaryDirectory { get; }
    internal string TemporaryOutputPath { get; }
    internal string Extension { get; }

    internal void Commit(Action? beforeCommit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(TemporaryOutputPath) || new FileInfo(TemporaryOutputPath).Length == 0)
        {
            throw new InvalidDataException("The export worker did not produce a valid output video.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        beforeCommit?.Invoke();
        File.Move(TemporaryOutputPath, OutputPath, false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Directory.Delete(TemporaryDirectory, true);
        disposed = true;
    }
}
