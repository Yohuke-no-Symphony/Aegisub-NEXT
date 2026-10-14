namespace AegiNext.Application.Presets;

internal static class EffectLibraryTransferFiles
{
    private const int BUFFER_SIZE = 65536;

    internal static async Task WriteAtomicAsync(string path, byte[] bytes, Action? beforeCommit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteNewAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(temporary, destination, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            BUFFER_SIZE, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    internal static byte[] Read(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[BUFFER_SIZE];
        var read = stream.Read(chunk, 0, chunk.Length);
        while (read > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("效果库包条目超过容量预算。");
            }
            buffer.Write(chunk, 0, read);
            read = stream.Read(chunk, 0, chunk.Length);
        }
        return buffer.ToArray();
    }
}
