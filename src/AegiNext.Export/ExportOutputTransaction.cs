namespace AegiNext.Export;

internal sealed class ExportOutputTransaction : IDisposable
{
    private bool disposed;

    internal ExportOutputTransaction(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException("输出文件必须使用完整路径。", nameof(outputPath));
        }

        OutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(OutputPath))
        {
            throw new IOException("输出文件已存在，请选择新文件名。");
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
            throw new InvalidDataException("导出 worker 未产生有效成片。");
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
