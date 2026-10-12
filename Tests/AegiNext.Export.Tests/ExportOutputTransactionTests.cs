namespace AegiNext.Export.Tests;

public sealed class ExportOutputTransactionTests
{
    [Fact]
    public void CommitPublishesOnlyAfterTheCallbackAndDisposalRemovesTemporaryFiles()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "字幕 视频.MKV");
        string temporary;
        using (var transaction = new ExportOutputTransaction(output))
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrEmptyOutputCannotEnterTheCommitBoundary(bool empty)
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        using var transaction = new ExportOutputTransaction(output);
        if (empty)
        {
            File.WriteAllText(transaction.TemporaryOutputPath, string.Empty);
        }

        var invoked = false;
        Assert.Throws<InvalidDataException>(() => transaction.Commit(() => invoked = true, CancellationToken.None));

        Assert.False(invoked);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void CancellationBeforeTheCommitBoundaryCannotPublish()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        using var transaction = new ExportOutputTransaction(output);
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

    [Fact]
    public void CallbackFailurePropagatesAndDisposalCleansTheRejectedOutput()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        var failure = new InvalidOperationException("commit rejected");
        string temporary;
        using (var transaction = new ExportOutputTransaction(output))
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

    [Fact]
    public void AConcurrentOutputCreatedAtTheCommitBoundaryIsNeverOverwritten()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        string temporary;
        using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            File.WriteAllText(transaction.TemporaryOutputPath, "complete output");

            Assert.Throws<IOException>(() => transaction.Commit(() =>
                File.WriteAllText(output, "concurrent output"), CancellationToken.None));
        }

        Assert.Equal("concurrent output", File.ReadAllText(output));
        Assert.False(Directory.Exists(temporary));
    }

    [Fact]
    public void CancellationAfterEnteringTheCommitBoundaryDoesNotInterruptPublishing()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        using var transaction = new ExportOutputTransaction(output);
        File.WriteAllText(transaction.TemporaryOutputPath, "complete output");
        using var cancellation = new CancellationTokenSource();

        transaction.Commit(cancellation.Cancel, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal("complete output", File.ReadAllText(output));
    }
}
