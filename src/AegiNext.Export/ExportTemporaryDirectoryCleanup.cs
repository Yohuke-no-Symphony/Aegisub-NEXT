namespace AegiNext.Export;

internal static class ExportTemporaryDirectoryCleanup
{
    private const int SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int LOCK_VIOLATION = unchecked((int)0x80070021);
    private const int MAX_ATTEMPTS = 8;
    private const int INITIAL_DELAY_MILLISECONDS = 25;
    private const int MAX_DELAY_MILLISECONDS = 500;

    internal static ValueTask DeleteAsync(string directory)
    {
        return DeleteAsync(directory, static path => Directory.Delete(path, true), static delay => Task.Delay(delay));
    }

    internal static async ValueTask DeleteAsync(string directory, Action<string> deleteDirectory,
        Func<TimeSpan, Task> delay)
    {
        var delayMilliseconds = INITIAL_DELAY_MILLISECONDS;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                deleteDirectory(directory);
                return;
            }
            catch (DirectoryNotFoundException) when (!Directory.Exists(directory))
            {
                return;
            }
            catch (IOException error) when (error.HResult is SHARING_VIOLATION or LOCK_VIOLATION && attempt < MAX_ATTEMPTS - 1)
            {
                await delay(TimeSpan.FromMilliseconds(delayMilliseconds)).ConfigureAwait(false);
                delayMilliseconds = Math.Min(delayMilliseconds * 2, MAX_DELAY_MILLISECONDS);
            }
        }
    }
}
