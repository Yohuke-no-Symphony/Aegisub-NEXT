using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class ExportWorkerClientTests
{
    [Fact]
    public async Task AlreadyCancelledRequestsDoNotAttemptToLocateTheWorker()
    {
        using var directory = new ExportTestDirectory();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportWorkerClient.RunAsync(
            "{}", Path.Combine(directory.Path, "missing-worker"), directory.Path, null, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task MissingWorkerDoesNotPublishAndTheTransactionCleansItsDirectory()
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        string temporary;
        await using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            var error = await Assert.ThrowsAsync<FileNotFoundException>(() => ExportWorkerClient.RunAsync(
                "{}", Path.Combine(directory.Path, "missing-worker"), temporary, null, CancellationToken.None));
            Assert.Contains("aegn-exporter", error.Message, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(temporary));
    }

    [ExportTheory]
    [InlineData("{}", "protocol")]
    [InlineData("null", "The export request is missing.")]
    public async Task WorkerProtocolFailuresPropagateWithoutPublishing(string serializedJob, string expectedError)
    {
        using var directory = new ExportTestDirectory();
        var output = Path.Combine(directory.Path, "output.mkv");
        string temporary;
        await using (var transaction = new ExportOutputTransaction(output))
        {
            temporary = transaction.TemporaryDirectory;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ExportWorkerClient.RunAsync(
                serializedJob, Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"), temporary,
                _ => Assert.Fail("A rejected request cannot report progress."), CancellationToken.None));
            Assert.Contains(expectedError, error.Message, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(temporary));
    }
}
