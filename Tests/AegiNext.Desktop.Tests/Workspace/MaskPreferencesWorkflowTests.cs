using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class MaskPreferencesWorkflowTests
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionInitializationRestoresMaskAndPreviewWithoutSavingPreferences(bool existingSettings)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        var expected = new WorkbenchPreferences { MaskSnapEnabled = !existingSettings };
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(expected, jsonOptions);
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

        AssertOption(expected, session);
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
    public async Task UserChangesPersistAndSynchronizeBorrowedSessionsWithoutProjectEdits()
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

        first.ViewModel.Masks.IsSnapEnabled = false;
        AssertOption(application.Preferences, second);
        application.UpdatePreferences(current => current with { AccentColor = "#123456", Volume = 0.375f });
        await application.Completion;

        var expected = application.Preferences;
        Assert.False(expected.MaskSnapEnabled);
        Assert.True(expected.TimelineSnapEnabled);
        Assert.Equal(2, notifications);
        AssertOption(expected, first);
        AssertOption(expected, second);
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
    public async Task ExternalPreferenceRefreshAppliesMaskAndPreviewWithOneNotification()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        await using var session = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: application);
        await session.Styles.Completion;
        var document = session.DocumentSnapshot;
        var notifications = 0;
        application.PreferencesChanged += (_, _) => notifications++;

        application.UpdatePreferences(current => current with { MaskSnapEnabled = false });
        await application.Completion;

        AssertOption(application.Preferences, session);
        Assert.Equal(1, notifications);
        Assert.Equal(application.Preferences, application.PreferencesStore.Load());
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(session.Editor.CanUndo);
        Assert.False(session.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task ReleasedSessionNeitherPublishesNorReceivesMaskPreferenceChanges()
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

            session.ViewModel.Masks.IsSnapEnabled = false;
            await application.Completion;

            Assert.Same(original, application.Preferences);
            Assert.Equal(0, notifications);
            Assert.False(File.Exists(Path.Combine(directory.Path, "preferences.json")));
            application.UpdatePreferences(current => current with { MaskSnapEnabled = false });
            await application.Completion;

            Assert.Equal(1, notifications);
            Assert.True(session.ViewModel.Preview.IsMaskSnapEnabled);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static void AssertOption(WorkbenchPreferences expected, WorkbenchSession session)
    {
        Assert.Equal(expected.MaskSnapEnabled, session.ViewModel.Masks.IsSnapEnabled);
        Assert.Equal(expected.MaskSnapEnabled, session.ViewModel.Preview.IsMaskSnapEnabled);
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
