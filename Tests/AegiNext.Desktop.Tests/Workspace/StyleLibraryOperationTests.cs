namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleLibraryOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AwaitedOperationPropagatesItsOwnFailureOrCancellationAndReleasesBusyState(bool cancelled)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var styles = context.Session.Styles;
        var notifications = new List<bool>();
        styles.BusyChanged += (_, _) => notifications.Add(styles.IsBusy);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = styles.QueueAsync(async () =>
        {
            await release.Task;
            if (cancelled)
            {
                throw new OperationCanceledException();
            }
            throw new InvalidOperationException("This capture failed.");
        });
        var drain = styles.Completion;
        Assert.True(styles.IsBusy);
        Assert.False(drain.IsCompleted);

        release.SetResult();
        if (cancelled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => drain));
        }

        Assert.False(styles.IsBusy);
        Assert.False(notifications.Last());
        Assert.Null(context.Session.LastError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task QueuedOperationNotifiesAvailabilityAfterSuccessFailureOrCancellation(int outcome)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var styles = context.Session.Styles;
        var notifications = new List<bool>();
        styles.BusyChanged += (_, _) => notifications.Add(styles.IsBusy);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        styles.Queue(async () =>
        {
            await release.Task;
            if (outcome == 1)
            {
                throw new InvalidOperationException("Style operation failed.");
            }
            if (outcome == 2)
            {
                throw new OperationCanceledException();
            }
        });

        Assert.True(styles.IsBusy);
        Assert.True(Assert.Single(notifications));
        release.SetResult();
        await styles.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(styles.IsBusy);
        Assert.False(notifications.Last());
        if (outcome == 1)
        {
            Assert.IsType<InvalidOperationException>(context.Session.LastError);
        }
        else
        {
            Assert.Null(context.Session.LastError);
        }
    }

    [Fact]
    public async Task PageRemainsBusyUntilEveryQueuedOperationCompletes()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var styles = context.Session.Styles;
        var notifications = new List<bool>();
        styles.BusyChanged += (_, _) => notifications.Add(styles.IsBusy);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        styles.Queue(() => first.Task);
        var firstCompletion = styles.Completion;
        styles.Queue(() =>
        {
            started.SetResult();
            return second.Task;
        });

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second.SetResult();
            Assert.False(styles.Completion.IsCompleted);
            Assert.True(styles.IsBusy);
            Assert.All(notifications, value => Assert.True(value));
            first.SetResult();
            await firstCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await styles.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(styles.IsBusy);
            Assert.False(notifications.Last());
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
        }
    }
}
