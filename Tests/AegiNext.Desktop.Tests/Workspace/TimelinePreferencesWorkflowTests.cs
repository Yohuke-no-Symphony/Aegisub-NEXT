using System.Text.Json;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelinePreferencesWorkflowTests
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new() { WriteIndented = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionInitializationRestoresOptionsWithoutSavingPreferences(bool existingSettings)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        var expected = existingSettings ? new WorkbenchPreferences
        {
            TimelineSnapEnabled = false,
            TimelineStepEnabled = true,
            TimelineAutoSeekEnabled = true,
            TimelineSpectrumVisible = false,
            TimelineWaveformVisible = false
        } : new();
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(expected, JSON_OPTIONS);
        if (existingSettings)
        {
            await File.WriteAllBytesAsync(path, originalBytes, CancellationToken.None);
        }
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        var notifications = 0;
        application.PreferencesChanged += (_, _) => notifications++;
        await using var session = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        await session.Styles.Completion;
        await application.Completion;

        AssertOptions(expected, session.ViewModel.Timeline);
        Assert.Equal(0, notifications);
        Assert.False(session.Editor.CanUndo);
        Assert.False(session.Editor.HasUnsavedChanges);
        if (existingSettings)
        {
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
        }
        else
        {
            Assert.False(File.Exists(path));
        }
    }

    [Fact]
    public async Task UserChangesPersistAndSynchronizeBorrowedSessionsWithoutExtraRequestsOrProjectEdits()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        await using var first = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        await using var second = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        await Task.WhenAll(first.Styles.Completion, second.Styles.Completion);
        var firstDocument = first.DocumentSnapshot;
        var secondDocument = second.DocumentSnapshot;
        var notifications = 0;
        application.PreferencesChanged += (_, _) => notifications++;

        first.ViewModel.Timeline.IsSnapEnabled = false;
        AssertOptions(application.Preferences, second.ViewModel.Timeline);
        first.ViewModel.Timeline.IsStepEnabled = true;
        AssertOptions(application.Preferences, second.ViewModel.Timeline);
        first.ViewModel.Timeline.IsAutoSeekEnabled = true;
        AssertOptions(application.Preferences, second.ViewModel.Timeline);
        first.ViewModel.Timeline.IsSpectrumVisible = false;
        AssertOptions(application.Preferences, second.ViewModel.Timeline);
        first.ViewModel.Timeline.IsWaveformVisible = false;
        AssertOptions(application.Preferences, second.ViewModel.Timeline);
        application.UpdatePreferences(current => current with { AccentColor = "#123456", Volume = 0.375f });
        await application.Completion;

        var expected = application.Preferences;
        Assert.False(expected.TimelineSnapEnabled);
        Assert.True(expected.TimelineStepEnabled);
        Assert.True(expected.TimelineAutoSeekEnabled);
        Assert.False(expected.TimelineSpectrumVisible);
        Assert.False(expected.TimelineWaveformVisible);
        Assert.Equal(6, notifications);
        AssertOptions(expected, first.ViewModel.Timeline);
        AssertOptions(expected, second.ViewModel.Timeline);
        Assert.Same(firstDocument, first.DocumentSnapshot);
        Assert.Same(secondDocument, second.DocumentSnapshot);
        Assert.False(first.Editor.CanUndo);
        Assert.False(second.Editor.CanUndo);
        Assert.False(first.Editor.HasUnsavedChanges);
        Assert.False(second.Editor.HasUnsavedChanges);
        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(expected, reopened.Load());
        Assert.Null(reopened.LoadError);
    }

    [Fact]
    public async Task ExternalPreferenceRefreshAppliesEveryOptionWithOneNotification()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        await using var session = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        await session.Styles.Completion;
        var notifications = 0;
        application.PreferencesChanged += (_, _) => notifications++;

        application.UpdatePreferences(current => current with
        {
            TimelineSnapEnabled = false,
            TimelineStepEnabled = true,
            TimelineAutoSeekEnabled = true,
            TimelineSpectrumVisible = false,
            TimelineWaveformVisible = false
        });
        await application.Completion;

        AssertOptions(application.Preferences, session.ViewModel.Timeline);
        Assert.Equal(1, notifications);
        Assert.Equal(application.Preferences, application.PreferencesStore.Load());
    }

    [Fact]
    public async Task ChangingAReleasedSessionCannotPublishPreferencesIntoItsBorrowedContext()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        var session = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        try
        {
            await session.Styles.Completion;
            await session.DisposeAsync();
            var original = application.Preferences;
            var notifications = 0;
            application.PreferencesChanged += (_, _) => notifications++;

            session.ViewModel.Timeline.IsSnapEnabled = false;
            session.ViewModel.Timeline.IsStepEnabled = true;
            session.ViewModel.Timeline.IsAutoSeekEnabled = true;
            session.ViewModel.Timeline.IsSpectrumVisible = false;
            session.ViewModel.Timeline.IsWaveformVisible = false;
            await application.Completion;

            Assert.Same(original, application.Preferences);
            Assert.Equal(0, notifications);
            Assert.False(File.Exists(Path.Combine(directory.Path, "preferences.json")));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static void AssertOptions(WorkbenchPreferences expected, TimelinePanelViewModel timeline)
    {
        Assert.Equal(expected.TimelineSnapEnabled, timeline.IsSnapEnabled);
        Assert.Equal(expected.TimelineStepEnabled, timeline.IsStepEnabled);
        Assert.Equal(expected.TimelineAutoSeekEnabled, timeline.IsAutoSeekEnabled);
        Assert.Equal(expected.TimelineSpectrumVisible, timeline.IsSpectrumVisible);
        Assert.Equal(expected.TimelineWaveformVisible, timeline.IsWaveformVisible);
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
