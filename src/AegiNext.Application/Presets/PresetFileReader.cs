namespace AegiNext.Application.Presets;

internal static class PresetFileReader
{
    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException("预设或字体文件超过容量预算。");
        }

        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("读取预设或字体时文件长度发生变化。", error);
        }
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("读取预设或字体时文件长度发生变化。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
