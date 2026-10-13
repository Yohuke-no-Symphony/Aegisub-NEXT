using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

/// <summary>验证独立 worker 的控制输入及取消回调生命周期。</summary>
public sealed class ExportWorkerControlTests
{
    /// <summary>取消命令触发正在运行的导出取消。</summary>
    [Fact]
    public async Task CancelCommandCancelsTheWorkerToken()
    {
        using var input = new StringReader(ExportWire.CANCEL_COMMAND);
        await using var cancellation = new ExportWorkerCancellation(input, CancellationToken.None);

        await cancellation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Null(cancellation.Failure);
        Assert.Throws<OperationCanceledException>(cancellation.ThrowIfCancellationRequested);
    }

    /// <summary>输入结束兼容单请求宿主，并且不会自动取消导出。</summary>
    [Fact]
    public async Task InputEofDoesNotCancelTheWorker()
    {
        using var input = new StringReader(string.Empty);
        await using var cancellation = new ExportWorkerCancellation(input, CancellationToken.None);

        await cancellation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(cancellation.Token.IsCancellationRequested);
        cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>未知控制命令保留协议错误，并取消运行中的导出。</summary>
    [Fact]
    public async Task UnknownCommandPreservesProtocolFailure()
    {
        using var input = new StringReader("unknown");
        await using var cancellation = new ExportWorkerCancellation(input, CancellationToken.None);

        await cancellation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancellation.Token.IsCancellationRequested);
        var error = Assert.Throws<InvalidDataException>(cancellation.ThrowIfCancellationRequested);
        Assert.Same(cancellation.Failure, error);
        Assert.Contains("unknown control command", error.Message, StringComparison.Ordinal);
    }

    /// <summary>控制输入读取失败保留原异常，而非降级为成功结束。</summary>
    [Fact]
    public async Task InputFailureCancelsAndPreservesTheOriginalError()
    {
        using var input = new ExportControlTestReader();
        await using var cancellation = new ExportWorkerCancellation(input, CancellationToken.None);
        var failure = new IOException("control pipe failed");
        input.Fail(failure);

        await cancellation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Same(failure, Assert.Throws<IOException>(cancellation.ThrowIfCancellationRequested));
    }

    /// <summary>宿主传入的取消信号仍可中断导出。</summary>
    [Fact]
    public async Task ExternalCancellationReachesTheWorkerToken()
    {
        using var external = new CancellationTokenSource();
        using var input = new ExportControlTestReader();
        await using var cancellation = new ExportWorkerCancellation(input, external.Token);

        await external.CancelAsync();
        await cancellation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Null(cancellation.Failure);
        Assert.Throws<OperationCanceledException>(cancellation.ThrowIfCancellationRequested);
    }

    /// <summary>释放控制器前等待取消回调完成，避免原生回调访问已释放的 token source。</summary>
    [Fact]
    public async Task DisposalWaitsForTheCancellationCallbackToFinish()
    {
        using var input = new ExportControlTestReader();
        var cancellation = new ExportWorkerCancellation(input, CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        try
        {
            input.Send(ExportWire.CANCEL_COMMAND);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = cancellation.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);
            Assert.False(cancellation.Completion.IsCompleted);
            release.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
            input.Send(null);
            await cancellation.DisposeAsync();
        }
    }
}
