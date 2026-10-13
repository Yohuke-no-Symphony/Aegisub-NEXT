namespace AegiNext.Export.Tests;

/// <summary>验证临时目录只对共享锁冲突执行有界异步重试。</summary>
public sealed class ExportTemporaryDirectoryCleanupTests
{
    private const int SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int LOCK_VIOLATION = unchecked((int)0x80070021);
    private static readonly TimeSpan[] retryDelays =
    [
        TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(500)
    ];

    /// <summary>共享锁和字节锁冲突均等待后重试，释放后删除完整目录。</summary>
    [Theory]
    [InlineData(SHARING_VIOLATION)]
    [InlineData(LOCK_VIOLATION)]
    public async Task TransientLockFailuresRetryUntilTheDirectoryIsDeleted(int hResult)
    {
        using var directory = new ExportTestDirectory();
        var temporary = Directory.CreateDirectory(Path.Combine(directory.Path, "temporary")).FullName;
        var attempts = 0;
        var delays = new List<TimeSpan>();

        await ExportTemporaryDirectoryCleanup.DeleteAsync(temporary, path =>
        {
            Assert.Equal(temporary, path);
            if (++attempts < 3)
            {
                throw new IOException("The temporary video is locked.", hResult);
            }

            Directory.Delete(path, true);
        }, delay =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        Assert.Equal(3, attempts);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50) }, delays);
        Assert.False(Directory.Exists(temporary));
    }

    /// <summary>异步等待完成前不得重新删除目录。</summary>
    [Fact]
    public async Task RetryWaitsForTheInjectedDelayBeforeDeletingAgain()
    {
        using var directory = new ExportTestDirectory();
        var temporary = Directory.CreateDirectory(Path.Combine(directory.Path, "temporary")).FullName;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var task = ExportTemporaryDirectoryCleanup.DeleteAsync(temporary, path =>
        {
            if (++attempts == 1)
            {
                throw new IOException("The temporary video is locked.", SHARING_VIOLATION);
            }

            Directory.Delete(path, true);
        }, _ => release.Task).AsTask();
        try
        {
            Assert.Equal(1, attempts);
            Assert.False(task.IsCompleted);
            Assert.True(Directory.Exists(temporary));
            release.SetResult();
            await task;

            Assert.Equal(2, attempts);
            Assert.False(Directory.Exists(temporary));
        }
        finally
        {
            release.TrySetResult();
            await task;
        }
    }

    /// <summary>持续锁冲突到达八次尝试上限后传播原始异常，等待时间最高为 500ms。</summary>
    [Theory]
    [InlineData(SHARING_VIOLATION)]
    [InlineData(LOCK_VIOLATION)]
    public async Task PersistentLockFailureStopsAtTheRetryLimit(int hResult)
    {
        using var directory = new ExportTestDirectory();
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var failure = new IOException("The temporary video remains locked.", hResult);

        var error = await Assert.ThrowsAsync<IOException>(() => ExportTemporaryDirectoryCleanup.DeleteAsync(directory.Path, _ =>
        {
            attempts++;
            throw failure;
        }, delay =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        }).AsTask());

        Assert.Same(failure, error);
        Assert.Equal(8, attempts);
        Assert.Equal(retryDelays, delays);
    }

    /// <summary>文件缺失、磁盘已满和一般 IO 错误均直接传播，避免重试不可恢复失败。</summary>
    [Theory]
    [InlineData(unchecked((int)0x80070002))]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80131620))]
    public async Task OtherIoFailuresAreNotRetried(int hResult)
    {
        using var directory = new ExportTestDirectory();
        var attempts = 0;
        var delays = 0;
        var failure = new IOException("The directory cannot be deleted.", hResult);

        var error = await Assert.ThrowsAsync<IOException>(() => ExportTemporaryDirectoryCleanup.DeleteAsync(directory.Path, _ =>
        {
            attempts++;
            throw failure;
        }, _ =>
        {
            delays++;
            return Task.CompletedTask;
        }).AsTask());

        Assert.Same(failure, error);
        Assert.Equal(1, attempts);
        Assert.Equal(0, delays);
    }

    /// <summary>权限拒绝不属于共享锁冲突，必须立即传播。</summary>
    [Fact]
    public async Task PermissionFailuresAreNotRetried()
    {
        using var directory = new ExportTestDirectory();
        var attempts = 0;
        var delays = 0;
        var failure = new UnauthorizedAccessException("The directory cannot be accessed.");

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExportTemporaryDirectoryCleanup.DeleteAsync(directory.Path, _ =>
        {
            attempts++;
            throw failure;
        }, _ =>
        {
            delays++;
            return Task.CompletedTask;
        }).AsTask());

        Assert.Same(failure, error);
        Assert.Equal(1, attempts);
        Assert.Equal(0, delays);
    }

    /// <summary>根目录已经不存在时直接视为清理完成。</summary>
    [Fact]
    public async Task MissingRootDirectoryIsAlreadyCleaned()
    {
        using var directory = new ExportTestDirectory();
        var temporary = Path.Combine(directory.Path, "missing");
        var attempts = 0;
        var delays = 0;

        await ExportTemporaryDirectoryCleanup.DeleteAsync(temporary, _ =>
        {
            attempts++;
            throw new DirectoryNotFoundException("The root directory is missing.");
        }, _ =>
        {
            delays++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, attempts);
        Assert.Equal(0, delays);
        Assert.False(Directory.Exists(temporary));
    }

    /// <summary>子路径缺失而根目录仍存在时保留异常，不误报全部清理成功。</summary>
    [Fact]
    public async Task MissingChildDirectoryDoesNotHideAnExistingRoot()
    {
        using var directory = new ExportTestDirectory();
        var attempts = 0;
        var delays = 0;
        var failure = new DirectoryNotFoundException(Path.Combine(directory.Path, "missing-child"));

        var error = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => ExportTemporaryDirectoryCleanup.DeleteAsync(directory.Path, _ =>
        {
            attempts++;
            throw failure;
        }, _ =>
        {
            delays++;
            return Task.CompletedTask;
        }).AsTask());

        Assert.Same(failure, error);
        Assert.Equal(1, attempts);
        Assert.Equal(0, delays);
        Assert.True(Directory.Exists(directory.Path));
    }

    /// <summary>等待锁释放期间被其他调用删除的根目录仍可正常结束清理。</summary>
    [Fact]
    public async Task ADirectoryRemovedDuringRetryIsAlreadyCleaned()
    {
        using var directory = new ExportTestDirectory();
        var temporary = Directory.CreateDirectory(Path.Combine(directory.Path, "temporary")).FullName;
        var attempts = 0;
        var delays = new List<TimeSpan>();

        await ExportTemporaryDirectoryCleanup.DeleteAsync(temporary, _ =>
        {
            if (++attempts == 1)
            {
                throw new IOException("The temporary video is locked.", SHARING_VIOLATION);
            }

            throw new DirectoryNotFoundException("The root directory was removed concurrently.");
        }, delay =>
        {
            delays.Add(delay);
            Directory.Delete(temporary, true);
            return Task.CompletedTask;
        });

        Assert.Equal(2, attempts);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(25) }, delays);
        Assert.False(Directory.Exists(temporary));
    }
}
