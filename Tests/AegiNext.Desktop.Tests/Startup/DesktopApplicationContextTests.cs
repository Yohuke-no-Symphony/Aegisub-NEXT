using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Tests.Workspace;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Startup;

[Collection("Workspace session")]
public sealed class DesktopApplicationContextTests
{
    [Fact]
    public async Task PreferencePatchesUseTheLatestSnapshotAndPersistTogether()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var notifications = 0;
        context.PreferencesChanged += (_, _) => notifications++;

        context.UpdatePreferences(current => current with { AccentColor = "#123456" });
        context.UpdatePreferences(current => current with { Volume = 0.25f });
        await context.Completion;

        Assert.Equal("#123456", context.Preferences.AccentColor);
        Assert.Equal(0.25f, context.Preferences.Volume);
        Assert.Equal(context.Preferences, context.PreferencesStore.Load());
        Assert.Equal(2, notifications);
        Assert.Null(context.LastError);
    }

    [Fact]
    public async Task InvalidPreferencePatchPreservesThePreviousSnapshotWithoutNotification()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var original = context.Preferences;
        var notifications = 0;
        context.PreferencesChanged += (_, _) => notifications++;

        Assert.Throws<InvalidDataException>(() => context.UpdatePreferences(current => current with { Volume = float.NaN }));
        await context.Completion;

        Assert.Same(original, context.Preferences);
        Assert.Equal(0, notifications);
        Assert.False(File.Exists(Path.Combine(directory.Path, "preferences.json")));
    }

    [Fact]
    public async Task StyleOperationsAreSerializedAndBusyDoesNotPublishUnchangedLibraryData()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = 0;
        var busyChanges = 0;
        context.StylesChanged += (_, _) => changes++;
        context.BusyChanged += (_, _) => busyChanges++;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Queued", new());
        var first = context.RunStyleOperationAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        var second = context.RunStyleOperationAsync(async () =>
        {
            nextEntered.TrySetResult();
            await context.StyleLibrary.UpsertAsync(preset);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(nextEntered.Task.IsCompleted);
            Assert.True(context.StylesBusy);
            Assert.Equal(0, changes);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(context.StylesBusy);
            Assert.Equal(1, changes);
            Assert.Equal(4, busyChanges);
            Assert.Equal(preset, Assert.Single(context.StyleLibrary.Snapshot.Presets));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task FailedLibraryOperationReportsItsErrorAndDoesNotPoisonTheQueue()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var original = context.StyleLibrary.Snapshot;
        var failure = new IOException("Rejected write");
        var errorChanges = 0;
        var dataChanges = 0;
        context.ErrorChanged += (_, _) => errorChanges++;
        context.StylesChanged += (_, _) => dataChanges++;

        var observed = await Assert.ThrowsAsync<IOException>(() => context.RunStyleOperationAsync(() => Task.FromException(failure)));
        await context.Completion;

        Assert.Same(failure, observed);
        Assert.Same(failure, context.LastError);
        Assert.Same(original, context.StyleLibrary.Snapshot);
        Assert.Equal(1, errorChanges);
        Assert.Equal(0, dataChanges);
        Assert.False(context.StylesBusy);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "After failure", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset));
        Assert.Equal(preset, Assert.Single(context.StyleLibrary.Snapshot.Presets));
        Assert.Equal(1, dataChanges);
    }

    [Fact]
    public async Task CorruptLibraryInitializationRetainsTheFileAndOtherApplicationServices()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "effect-scripts.json");
        const string CORRUPT_JSON = "{broken";
        await File.WriteAllTextAsync(path, CORRUPT_JSON);
        await using var context = new DesktopApplicationContext(new(directory.Path));

        await context.Initialization;
        await context.Completion;

        Assert.NotNull(context.LastError);
        Assert.Contains(context.Tasks.GetSnapshots(), value => value.Name == "Tasks.Initialization" &&
            value.State == AegiTaskState.Failed && value.ErrorSummary is not null);
        Assert.Equal(CORRUPT_JSON, await File.ReadAllTextAsync(path));
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Unaffected styles", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset));
        Assert.Equal(preset, Assert.Single(context.StyleLibrary.Snapshot.Presets));
    }

    [Fact]
    public async Task BorrowedSessionsShareUpdatesAndDisposalLeavesApplicationResourcesAlive()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        await using var first = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: context);
        await using var second = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: context);
        await Task.WhenAll(first.Styles.Completion, second.Styles.Completion);
        Assert.Same(context, first.ApplicationContext);
        Assert.Same(first.StyleLibrary, second.StyleLibrary);
        Assert.Same(first.PreferencesStore, second.PreferencesStore);

        await first.DisposeAsync();
        context.UpdatePreferences(current => current with { AccentColor = "#987654" });
        second.ViewModel.Preview.Volume = 0.375;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Still available", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset));
        await context.Completion;

        Assert.True(first.IsClosing);
        Assert.False(second.IsClosing);
        Assert.Equal("#987654", second.Preferences.AccentColor);
        Assert.Equal(0.375f, second.Preferences.Volume);
        Assert.Equal(context.Preferences, context.PreferencesStore.Load());
        Assert.Contains(second.ViewModel.Styles.Subtitle.Presets, item => item.Id == preset.Id);
    }

    [Fact]
    public async Task ApplicationDisposalDrainsQueuedOperationsAndRejectsNewWork()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = context.RunEffectOperationAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = context.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => context.UpdatePreferences(current => current with { Volume = 0.5f }));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.RunEffectOperationAsync(() => Task.CompletedTask));
            release.TrySetResult();
            await Task.WhenAll(operation, disposal).WaitAsync(TimeSpan.FromSeconds(5));
            await context.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.StyleLibrary.LoadAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.EffectScriptLibrary.LoadAsync());
        }
        finally
        {
            release.TrySetResult();
            await operation;
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task SessionWithoutInjectedContextOwnsItsIsolatedResources()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var session = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            preferencesStore: new(directory.Path));
        var context = session.ApplicationContext;
        try
        {
            await session.Styles.Completion;
        }
        finally
        {
            await session.DisposeAsync();
        }

        Assert.Throws<ObjectDisposedException>(() => context.UpdatePreferences(current => current with { Volume = 0.5f }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.StyleLibrary.LoadAsync());
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
