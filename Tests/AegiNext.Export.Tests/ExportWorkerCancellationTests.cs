using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

/// <summary>以独立进程和独占文件验证导出 worker 的退出及取消握手。</summary>
public sealed class ExportWorkerCancellationTests
{
    /// <summary>正常完成后关闭输入，并等待 worker 清理文件及退出。</summary>
    [Fact]
    public async Task NormalCompletionWaitsForInputEofAndWorkerCleanup()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "work")).FullName;
        var markerPrefix = Path.Combine(directory.Path, "normal");
        var ready = new TaskCompletionSource<ExportWorkerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = RunWorkerAsync("normal", workingDirectory, markerPrefix,
            message => ready.TrySetResult(message), cancellation.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(task.IsCompleted);
            await File.WriteAllTextAsync(markerPrefix + ".continue", "complete");

            var result = await task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("complete", result.Type);
            Assert.True(File.Exists(markerPrefix + ".released"));
            AssertStopped(markerPrefix);
            Directory.Delete(workingDirectory, true);
            Assert.False(Directory.Exists(workingDirectory));
        }
        finally
        {
            await CleanupAsync(task, cancellation, markerPrefix);
        }
    }

    /// <summary>取消保持原始 token，等待独占句柄释放后允许在同一目录立即重新编码。</summary>
    [Theory]
    [InlineData("cooperative")]
    [InlineData("terminal-cancel")]
    public async Task CooperativeCancellationWaitsForReleaseAndAllowsImmediateRestart(string mode)
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "work")).FullName;
        var markerPrefix = Path.Combine(directory.Path, "cancelled");
        var ready = new TaskCompletionSource<ExportWorkerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressCount = 0;
        var task = RunWorkerAsync(mode, workingDirectory, markerPrefix, message =>
        {
            Interlocked.Increment(ref progressCount);
            if (mode == "terminal-cancel")
            {
                cancellation.Cancel();
            }

            ready.TrySetResult(message);
        }, cancellation.Token, TimeSpan.FromSeconds(5));
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cancellation.CancelAsync();
            await WaitForMarkerAsync(markerPrefix + ".received");
            Assert.False(task.IsCompleted);
            Assert.False(File.Exists(markerPrefix + ".released"));
            await File.WriteAllTextAsync(markerPrefix + ".continue", "release");

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(1, Volatile.Read(ref progressCount));
            Assert.True(File.Exists(markerPrefix + ".released"));
            AssertStopped(markerPrefix);
            Directory.Delete(workingDirectory, true);
            Directory.CreateDirectory(workingDirectory);
            await RestartAsync(workingDirectory, Path.Combine(directory.Path, "restarted"));
        }
        finally
        {
            await CleanupAsync(task, cancellation, markerPrefix);
        }
    }

    /// <summary>无响应 worker 先收到取消命令，然后由超时兜底终止并释放文件。</summary>
    [Fact]
    public async Task UnresponsiveCancellationSendsCommandBeforeTerminatingTheWorker()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "work")).FullName;
        var markerPrefix = Path.Combine(directory.Path, "unresponsive");
        var ready = new TaskCompletionSource<ExportWorkerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = RunWorkerAsync("hard-kill", workingDirectory, markerPrefix,
            message => ready.TrySetResult(message), cancellation.Token, TimeSpan.FromSeconds(1));
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var elapsed = Stopwatch.StartNew();
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(900), "The worker was terminated before its cancellation grace period expired.");
            Assert.True(File.Exists(markerPrefix + ".received"));
            Assert.False(File.Exists(markerPrefix + ".released"));
            AssertStopped(markerPrefix);
            Directory.Delete(workingDirectory, true);
            Assert.False(Directory.Exists(workingDirectory));
        }
        finally
        {
            await CleanupAsync(task, cancellation, markerPrefix);
        }
    }

    private static Task<ExportWorkerMessage> RunWorkerAsync(string mode, string workingDirectory, string markerPrefix,
        Action<ExportWorkerMessage> reportProgress, CancellationToken cancellationToken, TimeSpan? cancellationGracePeriod = null)
    {
        var job = JsonSerializer.Serialize(new { mode, markerPrefix });
        return ExportWorkerClient.RunAsync(job, GetHostPath(), workingDirectory, reportProgress, cancellationToken, cancellationGracePeriod);
    }

    /// <summary>请求传输被满管道阻塞时，取消先终止进程再关闭输入，避免缓冲区 flush 悬挂。</summary>
    [Fact]
    public async Task CancelledBlockedRequestTransmissionTerminatesTheWorker()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "work")).FullName;
        var markerPrefix = Path.Combine(workingDirectory, "request");
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "block-request"), string.Empty);
        var task = ExportWorkerClient.RunAsync(new string('x', 1_000_000), GetHostPath(), workingDirectory,
            null, cancellation.Token);
        try
        {
            await WaitForMarkerAsync(markerPrefix + ".started");
            Assert.False(task.IsCompleted);
            await cancellation.CancelAsync();

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            AssertStopped(markerPrefix);
            Directory.Delete(workingDirectory, true);
            Assert.False(Directory.Exists(workingDirectory));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                await CleanupAsync(task, cancellation, markerPrefix);
            }
        }
    }

    private static string GetHostPath()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "ExportTestHost",
            "AegiNext.Export.TestHost" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(host), $"The export cancellation test host is missing: {host}");
        return host;
    }

    private static async Task RestartAsync(string workingDirectory, string markerPrefix)
    {
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource<ExportWorkerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = RunWorkerAsync("normal", workingDirectory, markerPrefix,
            message => ready.TrySetResult(message), cancellation.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(markerPrefix + ".continue", "complete");
            var result = await task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("complete", result.Type);
            Assert.True(File.Exists(markerPrefix + ".released"));
            AssertStopped(markerPrefix);
            Directory.Delete(workingDirectory, true);
            Assert.False(Directory.Exists(workingDirectory));
        }
        finally
        {
            await CleanupAsync(task, cancellation, markerPrefix);
        }
    }

    private static async Task WaitForMarkerAsync(string markerPath)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(markerPath))
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private static void AssertStopped(string markerPrefix)
    {
        var pid = int.Parse(File.ReadAllText(markerPrefix + ".started"), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task CleanupAsync(Task task, CancellationTokenSource cancellation, string markerPrefix)
    {
        await File.WriteAllTextAsync(markerPrefix + ".continue", "release");
        await cancellation.CancelAsync();
        await task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!File.Exists(markerPrefix + ".started"))
        {
            return;
        }

        var pid = int.Parse(File.ReadAllText(markerPrefix + ".started"), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (ArgumentException)
        {
        }
    }
}
