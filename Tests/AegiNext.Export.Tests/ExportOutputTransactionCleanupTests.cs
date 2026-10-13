using System.Collections.Concurrent;

namespace AegiNext.Export.Tests;

/// <summary>验证异步事务清理的并发幂等性和原始失败保留。</summary>
public sealed class ExportOutputTransactionCleanupTests
{
    private const int SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int LOCK_VIOLATION = unchecked((int)0x80070021);

    /// <summary>并发和重复释放共享同一个清理任务，清理结束前均保持未完成。</summary>
    [Fact]
    public async Task ConcurrentDisposalsWaitForTheSameCleanupTask()
    {
        using var directory = new ExportTestDirectory();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupPaths = new ConcurrentQueue<string>();
        var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "output.mkv"), async path =>
        {
            cleanupPaths.Enqueue(path);
            await release.Task;
            Directory.Delete(path, true);
        });
        try
        {
            await File.WriteAllTextAsync(transaction.TemporaryOutputPath, "complete output");
            var disposals = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(transaction.DisposeAsync)))
                .WaitAsync(TimeSpan.FromSeconds(5));
            var tasks = disposals.Select(disposal => disposal.AsTask()).ToArray();
            var first = tasks[0];

            Assert.All(tasks, task => Assert.Same(first, task));
            Assert.All(tasks, task => Assert.False(task.IsCompleted));
            Assert.Equal(transaction.TemporaryDirectory, Assert.Single(cleanupPaths));
            Assert.True(Directory.Exists(transaction.TemporaryDirectory));
            Assert.Throws<ObjectDisposedException>(() => transaction.Commit(null, CancellationToken.None));
            release.SetResult();
            await Task.WhenAll(tasks);

            var repeated = transaction.DisposeAsync().AsTask();
            Assert.Same(first, repeated);
            await repeated;
            Assert.Single(cleanupPaths);
            Assert.False(Directory.Exists(transaction.TemporaryDirectory));
        }
        finally
        {
            release.TrySetResult();
            await transaction.DisposeAsync();
        }
    }

    /// <summary>没有原始失败时，清理 IO 或权限异常附带完整目录路径和原始原因。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureIncludesTheCompleteDirectoryPath(bool permissionDenied)
    {
        using var directory = new ExportTestDirectory();
        Exception failure = permissionDenied
            ? new UnauthorizedAccessException("Directory access was denied.")
            : new IOException("The temporary video remains locked.", SHARING_VIOLATION);
        var cleanupCalls = 0;
        var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "字幕 视频.mkv"), path =>
        {
            Assert.True(Path.IsPathFullyQualified(path));
            cleanupCalls++;
            return ValueTask.FromException(failure);
        });

        var task = transaction.DisposeAsync().AsTask();
        var error = await Assert.ThrowsAsync<IOException>(() => task);

        Assert.Contains(transaction.TemporaryDirectory, error.Message, StringComparison.Ordinal);
        Assert.Same(failure, error.InnerException);
        Assert.True(Directory.Exists(transaction.TemporaryDirectory));
        Assert.False(File.Exists(transaction.OutputPath));
        Assert.Same(task, transaction.DisposeAsync().AsTask());
        Assert.Equal(1, cleanupCalls);
    }

    /// <summary>清理持续失败时同时保留第一次取消异常、调用方 token 和最终锁异常。</summary>
    [Theory]
    [InlineData(SHARING_VIOLATION)]
    [InlineData(LOCK_VIOLATION)]
    public async Task CancellationAndPersistentCleanupFailureRemainAvailable(int hResult)
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var original = new OperationCanceledException("Export was cancelled.", cancellation.Token);
        var cleanupFailure = new IOException("The temporary video remains locked.", hResult);
        var attempts = 0;
        var delays = 0;
        var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "output.mkv"), path =>
            ExportTemporaryDirectoryCleanup.DeleteAsync(path, _ =>
            {
                attempts++;
                throw cleanupFailure;
            }, _ =>
            {
                delays++;
                return Task.CompletedTask;
            }));
        transaction.RecordFailure(original);
        transaction.RecordFailure(new InvalidOperationException("A later failure must not replace cancellation."));
        var task = transaction.DisposeAsync().AsTask();
        var repeated = transaction.DisposeAsync().AsTask();

        var error = await Assert.ThrowsAsync<AggregateException>(() => task);

        Assert.Contains(transaction.TemporaryDirectory, error.Message, StringComparison.Ordinal);
        Assert.Collection(error.InnerExceptions,
            failure => Assert.Same(original, failure),
            failure => Assert.Same(cleanupFailure, failure));
        Assert.Equal(cancellation.Token, original.CancellationToken);
        Assert.Equal(8, attempts);
        Assert.Equal(7, delays);
        Assert.Same(task, repeated);
        Assert.Same(error, await Assert.ThrowsAsync<AggregateException>(() => repeated));
        Assert.False(File.Exists(transaction.OutputPath));
    }

    /// <summary>调用者抛出取消异常而清理成功时仍得到同一个取消异常。</summary>
    [Fact]
    public async Task SuccessfulCleanupPreservesTheOriginalCancellation()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var original = new OperationCanceledException("Export was cancelled.", cancellation.Token);
        string? temporary = null;

        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "output.mkv"));
            temporary = transaction.TemporaryDirectory;
            await File.WriteAllTextAsync(transaction.TemporaryOutputPath, "partial output");
            transaction.RecordFailure(original);
            throw original;
        });

        Assert.Same(original, error);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.NotNull(temporary);
        Assert.False(Directory.Exists(temporary));
        Assert.False(File.Exists(Path.Combine(directory.Path, "output.mkv")));
    }

    /// <summary>非文件系统异常按原类型传播，不被包装为目录删除错误。</summary>
    [Fact]
    public async Task NonFileSystemCleanupFailureKeepsItsOriginalType()
    {
        using var directory = new ExportTestDirectory();
        var failure = new InvalidOperationException("Unexpected cleanup failure.");
        var transaction = new ExportOutputTransaction(Path.Combine(directory.Path, "output.mkv"), _ => ValueTask.FromException(failure));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.DisposeAsync().AsTask());

        Assert.Same(failure, error);
    }
}
