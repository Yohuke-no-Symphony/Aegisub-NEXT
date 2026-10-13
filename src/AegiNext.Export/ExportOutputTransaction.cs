namespace AegiNext.Export;

internal sealed class ExportOutputTransaction : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Func<string, ValueTask> cleanupDirectory;
    private Task? disposal;
    private Exception? failure;

    internal ExportOutputTransaction(string outputPath, Func<string, ValueTask>? cleanupDirectory = null)
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
        this.cleanupDirectory = cleanupDirectory ?? ExportTemporaryDirectoryCleanup.DeleteAsync;
    }

    internal string OutputPath { get; }
    internal string TemporaryDirectory { get; }
    internal string TemporaryOutputPath { get; }
    internal string Extension { get; }

    internal void Commit(Action? beforeCommit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposal is not null, this);
        if (!File.Exists(TemporaryOutputPath) || new FileInfo(TemporaryOutputPath).Length == 0)
        {
            throw new InvalidDataException("The export worker did not produce a valid output video.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        beforeCommit?.Invoke();
        File.Move(TemporaryOutputPath, OutputPath, false);
    }

    internal void RecordFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        failure ??= error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposal ??= CleanupAsync();
            return new(disposal);
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            await cleanupDirectory(TemporaryDirectory).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var message = $"The export temporary directory could not be cleaned: {TemporaryDirectory}";
            if (failure is not null)
            {
                throw new AggregateException(message, failure, error);
            }

            throw new IOException(message, error);
        }
    }
}
