namespace AegiNext.Export.Tests;

/// <summary>验证 Windows 真实共享锁释放后，取消导出的临时文件能够完成清理。</summary>
public sealed class ExportWindowsFileLockTests
{
    /// <summary>已经取消的导出仍等待独占文件句柄释放，并异步重试目录删除。</summary>
    [WindowsExportFact]
    public async Task CancellationCleanupWaitsForTheExclusiveVideoHandleToBeReleased()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "output.mkv"));
        using var video = new FileStream(Path.Combine(transaction.TemporaryDirectory, "video.nut"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await cancellation.CancelAsync();
        transaction.RecordFailure(new OperationCanceledException(cancellation.Token));
        try
        {
            var disposal = transaction.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);
            Assert.True(Directory.Exists(transaction.TemporaryDirectory));
            video.Dispose();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(Directory.Exists(transaction.TemporaryDirectory));
            Assert.False(File.Exists(transaction.OutputPath));
        }
        finally
        {
            video.Dispose();
            await transaction.DisposeAsync();
        }
    }
}
