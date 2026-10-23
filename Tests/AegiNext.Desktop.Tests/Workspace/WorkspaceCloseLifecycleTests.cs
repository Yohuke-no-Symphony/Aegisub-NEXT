using AegiNext.Desktop.Settings.Koumori;
using AegiNext.Core.Projects;
using AegiNext.Media.Playback;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkspaceCloseLifecycleTests
{
    [Fact]
    public async Task CancelledCloseDoesNotCancelRunningTaskOrReleaseItsEditingLease()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved", document => document with { Name = "Pending" });
        context.Dialogs.UnsavedChoice = 0;
        var task = new WorkspaceLifecycleTask(context.Session.TaskScope);
        var handle = context.Session.ApplicationContext.Tasks.Submit(task);
        try
        {
            await task.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await context.Session.RequestCloseAsync());
            Assert.Equal(AegiTaskState.Running, handle.Snapshot.State);
            Assert.False(task.CancellationObserved.Task.IsCompleted);
            Assert.True(context.Session.IsProjectBusy);
            task.Finish.TrySetResult();
            task.Cleanup.TrySetResult();
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(context.Session.IsProjectBusy);
        }
        finally
        {
            task.Finish.TrySetResult();
            task.Cleanup.TrySetResult();
        }
    }

    [Fact]
    public async Task ConfirmedCloseWaitsForActualCleanupWhileAnotherProjectContinues()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new WorkbenchPreferencesStore(directory.Path));
        await application.Initialization;
        await using var first = new WorkspaceSessionTestContext(applicationContext: application);
        await using var second = new WorkspaceSessionTestContext(applicationContext: application);
        await first.InitializeAsync();
        await second.InitializeAsync();
        var closingTask = new WorkspaceLifecycleTask(first.Session.TaskScope);
        var otherTask = new WorkspaceLifecycleTask(second.Session.TaskScope);
        var closingHandle = application.Tasks.Submit(closingTask);
        var otherHandle = application.Tasks.Submit(otherTask);
        Task<bool>? close = null;
        try
        {
            await Task.WhenAll(closingTask.Started.Task, otherTask.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
            close = first.Session.RequestCloseAsync();
            await closingTask.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(close.IsCompleted);
            Assert.Equal(AegiTaskState.Cancelling, closingHandle.Snapshot.State);
            Assert.True(application.Tasks.IsEditingRestricted(first.Session.TaskScope));
            Assert.Equal(AegiTaskState.Running, otherHandle.Snapshot.State);
            closingTask.Cleanup.TrySetResult();
            Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(AegiTaskState.Cancelled, closingHandle.Snapshot.State);
            Assert.False(application.Tasks.IsEditingRestricted(first.Session.TaskScope));
            Assert.False(second.Session.IsClosing);
            Assert.False(otherTask.CancellationObserved.Task.IsCompleted);
            otherTask.Finish.TrySetResult();
            otherTask.Cleanup.TrySetResult();
            await otherHandle.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            closingTask.Cleanup.TrySetResult();
            otherTask.Finish.TrySetResult();
            otherTask.Cleanup.TrySetResult();
            if (close is not null)
            {
                await close;
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CancelledUnsavedPromptOrSaveDialogLeavesSessionAndPreviewAlive(int choice)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = choice;
        context.Dialogs.SavePath = null;
        var beforeDisposeCalls = 0;

        Assert.False(await context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalls++;
            return Task.CompletedTask;
        }));

        Assert.False(context.Session.IsClosing);
        Assert.Equal(0, beforeDisposeCalls);
        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.Equal(choice == 1 ? 1 : 0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.Source.DisposeCount);
        Assert.Equal(0, context.Converter.DisposeCount);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.True(context.Editor.HasUnsavedChanges);
        await context.Session.Controller.SeekAsync(new(1, 10));
        Assert.Null(context.Session.Controller.Snapshot.Error);
    }

    [Fact]
    public async Task FailedSavePreservesUnsavedSnapshotAndDoesNotRunDisposalBoundary()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        var original = context.Editor.Snapshot;
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = 1;
        var blockedDestination = Path.Combine(context.DirectoryPath, "blocked.aeginext");
        Directory.CreateDirectory(blockedDestination);
        context.Dialogs.SavePath = blockedDestination;
        var beforeDisposeCalls = 0;

        await Assert.ThrowsAnyAsync<IOException>(() => context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalls++;
            return Task.CompletedTask;
        }));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.IsClosing);
        Assert.Equal(0, beforeDisposeCalls);
        Assert.Equal(0, context.Source.DisposeCount);
        Assert.Equal(0, context.Converter.DisposeCount);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.Empty(Directory.EnumerateFiles(context.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task FailedCloseKeepsAutomaticTimingAvailableForAnotherAttempt()
    {
        await using var context = new WorkspaceSessionTestContext(hasAudio: true);
        await context.InitializeAsync();
        var mediaPath = Path.Combine(context.DirectoryPath, "controlled.mkv");
        await File.WriteAllBytesAsync(mediaPath, [0]);
        await context.Session.Controller.OpenAsync(mediaPath);
        context.Session.ApplicationContext.UpdatePreferences(value => value with
        {
            Koumori = value.Koumori with
            {
                Enabled = true,
                ModelSource = KoumoriModelSource.MANUAL,
                ModelDirectory = Path.Combine(context.DirectoryPath, "missing-model")
            }
        });
        Assert.True(context.Session.CanAnalyzeKoumori);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => context.Session.AnalyzeKoumoriAsync(default, null));
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        context.Dialogs.UnsavedChoice = 1;
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "blocked.aeginext");
        Directory.CreateDirectory(context.Dialogs.SavePath);

        await Assert.ThrowsAnyAsync<IOException>(() => context.Session.RequestCloseAsync());

        Assert.False(context.Session.IsClosing);
        Assert.True(context.Session.CanAnalyzeKoumori);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => context.Session.AnalyzeKoumoriAsync(default, null));
    }

    [Fact]
    public async Task SuccessfulCloseAwaitsOneBoundaryAndReleasesControllerResourcesOnce()
    {
        await using var context = new WorkspaceSessionTestContext(new ProjectDocument { Name = "Close" });
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = 2;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeDisposeCalls = 0;
        var close = context.Session.RequestCloseAsync(async () =>
        {
            beforeDisposeCalls++;
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(close.IsCompleted);
            Assert.True(context.Session.IsClosing);
            Assert.Equal(0, context.Source.DisposeCount);
            Assert.Equal(0, context.Converter.DisposeCount);
            Assert.False(await context.Session.RequestCloseAsync());
            release.TrySetResult();
            Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, beforeDisposeCalls);
            Assert.Equal(1, context.Source.DisposeCount);
            Assert.Equal(1, context.Converter.DisposeCount);
            Assert.All(context.Source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
            Assert.Equal(VideoPlaybackState.CLOSED, context.Session.Controller.Snapshot.State);
            Assert.False(Directory.Exists(context.Session.ScratchDirectory));
            await context.Session.DisposeAsync();
            Assert.Equal(1, context.Source.DisposeCount);
            Assert.Equal(1, context.Converter.DisposeCount);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                context.Session.Controller.OpenAsync("another.mkv"));
        }
        finally
        {
            release.TrySetResult();
            await close;
        }
    }
}
