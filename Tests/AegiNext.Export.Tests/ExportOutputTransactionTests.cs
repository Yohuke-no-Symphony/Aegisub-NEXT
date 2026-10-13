namespace AegiNext.Export.Tests;

/// <summary>验证成片提交边界及异步清理不会覆盖已有输出。</summary>
public sealed class ExportOutputTransactionTests
{
    /// <summary>提交回调先于发布执行，异步释放删除本次临时目录。</summary>
    [Fact]
    public async Task CommitPublishesOnlyAfterTheCallbackAndDisposalRemovesTemporaryFiles()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "字幕 视频.MKV");
        string temporary;
        await using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            var temporaryOutput = transaction.TemporaryOutputPath;
            Assert.Equal(".mkv", transaction.Extension);
            File.WriteAllText(transaction.TemporaryOutputPath, "complete output");
            transaction.Commit(() =>
            {
                Assert.False(File.Exists(output));
                Assert.True(File.Exists(temporaryOutput));
            }, CancellationToken.None);
            Assert.Equal("complete output", File.ReadAllText(output));
        }

        Assert.False(Directory.Exists(temporary));
        Assert.Equal("complete output", File.ReadAllText(output));
    }

    /// <summary>输出已经存在时不得建立临时目录。</summary>
    [Fact]
    public void ExistingOutputIsRejectedBeforeCreatingTemporaryFiles()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        File.WriteAllText(output, "existing output");

        Assert.Throws<IOException>(() => new ExportOutputTransaction(output));

        Assert.Equal("existing output", File.ReadAllText(output));
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    /// <summary>缺少有效成片时不得进入提交回调。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyOutputCannotEnterTheCommitBoundary(bool empty)
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        await using var transaction = new ExportOutputTransaction(output);
        if (empty)
        {
            File.WriteAllText(transaction.TemporaryOutputPath, string.Empty);
        }

        var invoked = false;
        Assert.Throws<InvalidDataException>(() => transaction.Commit(() => invoked = true, CancellationToken.None));

        Assert.False(invoked);
        Assert.False(File.Exists(output));
    }

    /// <summary>提交边界前取消时保持调用方 token，并阻止发布。</summary>
    [Fact]
    public async Task CancellationBeforeTheCommitBoundaryCannotPublish()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        await using var transaction = new ExportOutputTransaction(output);
        File.WriteAllText(transaction.TemporaryOutputPath, "complete output");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        var error = Assert.Throws<OperationCanceledException>(() =>
            transaction.Commit(() => invoked = true, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(invoked);
        Assert.False(File.Exists(output));
    }

    /// <summary>提交回调失败仍传播原始异常，异步释放清理被拒绝的输出。</summary>
    [Fact]
    public async Task CallbackFailurePropagatesAndDisposalCleansTheRejectedOutput()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        var failure = new InvalidOperationException("commit rejected");
        string temporary;
        await using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            File.WriteAllText(transaction.TemporaryOutputPath, "complete output");
            var error = Assert.Throws<InvalidOperationException>(() =>
                transaction.Commit(() => throw failure, CancellationToken.None));
            Assert.Same(failure, error);
        }

        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(temporary));
    }

    /// <summary>提交边界内并发产生的目标文件保持原样。</summary>
    [Fact]
    public async Task AConcurrentOutputCreatedAtTheCommitBoundaryIsNeverOverwritten()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        string temporary;
        await using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            File.WriteAllText(transaction.TemporaryOutputPath, "complete output");

            Assert.Throws<IOException>(() => transaction.Commit(() =>
                File.WriteAllText(output, "concurrent output"), CancellationToken.None));
        }

        Assert.Equal("concurrent output", File.ReadAllText(output));
        Assert.False(Directory.Exists(temporary));
    }

    /// <summary>进入提交边界后取消不会中断已获准的原子发布。</summary>
    [Fact]
    public async Task CancellationAfterEnteringTheCommitBoundaryDoesNotInterruptPublishing()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        await using var transaction = new ExportOutputTransaction(output);
        File.WriteAllText(transaction.TemporaryOutputPath, "complete output");
        using var cancellation = new CancellationTokenSource();

        transaction.Commit(cancellation.Cancel, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal("complete output", File.ReadAllText(output));
    }
}
