using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleListPreferencesWorkflowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisplayOptionsRestoreSynchronizeAndPersistWithoutEditingProjects(bool hideTimes, bool hideDuration)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var saved = new WorkbenchPreferences
        {
            SubtitleListHideTimes = hideTimes,
            SubtitleListHideDuration = hideDuration,
            Volume = 0.375f
        };
        await store.SaveAsync(saved);
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        var notifications = 0;
        application.PreferencesChanged += (_, _) => notifications++;
        await using var first = new WorkspaceSessionTestContext(applicationContext: application);
        await using var second = new WorkspaceSessionTestContext(applicationContext: application);
        await first.InitializeAsync();
        await second.InitializeAsync();
        var firstDocument = first.Session.DocumentSnapshot;
        var secondDocument = second.Session.DocumentSnapshot;
        Assert.Equal(hideTimes, first.Session.ViewModel.Subtitles.HideTimes);
        Assert.Equal(hideDuration, first.Session.ViewModel.Subtitles.HideDuration);
        Assert.Equal(0, notifications);

        first.Session.ViewModel.Subtitles.HideTimes = !hideTimes;
        Assert.Equal(!hideTimes, second.Session.ViewModel.Subtitles.HideTimes);
        Assert.Equal(hideDuration, second.Session.ViewModel.Subtitles.HideDuration);
        second.Session.ViewModel.Subtitles.HideDuration = !hideDuration;
        Assert.Equal(!hideDuration, first.Session.ViewModel.Subtitles.HideDuration);
        await application.Completion;

        var expected = saved with { SubtitleListHideTimes = !hideTimes, SubtitleListHideDuration = !hideDuration };
        Assert.Equal(2, notifications);
        Assert.Equal(expected, store.Load());
        Assert.Null(store.LoadError);
        Assert.Same(firstDocument, first.Session.DocumentSnapshot);
        Assert.Same(secondDocument, second.Session.DocumentSnapshot);
        Assert.False(first.Editor.CanUndo);
        Assert.False(second.Editor.CanUndo);
        Assert.False(first.Editor.HasUnsavedChanges);
        Assert.False(second.Editor.HasUnsavedChanges);

        await using var reopened = new WorkspaceSessionTestContext(applicationContext: application);
        await reopened.InitializeAsync();
        Assert.Equal(!hideTimes, reopened.Session.ViewModel.Subtitles.HideTimes);
        Assert.Equal(!hideDuration, reopened.Session.ViewModel.Subtitles.HideDuration);
        await first.Session.DisposeAsync();
        first.Session.ViewModel.Subtitles.HideTimes = hideTimes;
        first.Session.ViewModel.Subtitles.HideDuration = hideDuration;
        Assert.Equal(expected, application.Preferences);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task ExistingSettingsKeepColumnsVisibleWithoutRewritingTheFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Version\":1,\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.False(preferences.SubtitleListHideTimes);
        Assert.False(preferences.SubtitleListHideDuration);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path));
    }
}
