using AegiNext.Application.ColorTags;
using AegiNext.Application.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Startup;

[Collection("Workspace session")]
public sealed class SettingsRestoreStartupTests
{
    [Fact]
    public async Task OpeningAnotherContextDoesNotApplyPendingSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        await store.SaveAsync(new() { AccentColor = "#123456" });
        await using var firstContext = new DesktopApplicationContext(new(directory.Path));
        await firstContext.Initialization;
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new() { Preferences = new() { AccentColor = "#654321" } });

        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.Equal("#123456", context.Preferences.AccentColor);
        Assert.True(context.SettingsRestore.HasPending);
    }

    [Fact]
    public async Task StartupRestoresSettingsBeforePreferencesAndLibrariesLoad()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new() { Preferences = new() { AccentColor = "#654321" } });

        SettingsRestoreStartup.ApplyOnce(directory.Path);
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.Equal("#654321", context.Preferences.AccentColor);
        Assert.False(context.SettingsRestore.HasPending);
        Assert.Null(context.SettingsLoadError);
        Assert.Empty(context.StyleLibrary.Snapshot.Presets);
        Assert.Empty(context.EffectScriptLibrary.Snapshot.Presets);
        Assert.Empty(context.ExportPresetLibrary.Snapshot.Presets);
        Assert.Empty(context.ColorTagLibrary.Snapshot.Tags);
    }

    /// <summary>旧版设置包在启动恢复时保留本地标签库，不重新初始化默认色。</summary>
    [Fact]
    public async Task VersionOneStartupRestorePreservesTheExistingTagLibrary()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "subtitle-color-tags.json");
        var document = new SubtitleColorTagLibraryDocument
        {
            Tags = [new() { Name = "本地标签", ColorHex = "#123456" }]
        };
        await SubtitleColorTagStore.SaveAsync(document, path);
        var original = await File.ReadAllBytesAsync(path);
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new() { ColorTags = null });

        SettingsRestoreStartup.ApplyOnce(directory.Path);
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(document.Tags[0], Assert.Single(context.ColorTagLibrary.Snapshot.Tags));
        Assert.False(context.SettingsRestore.HasPending);
    }

    [Fact]
    public async Task PendingImportStagedAfterStartupIsNotAppliedAgainInTheSameProcess()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        SettingsRestoreStartup.ApplyOnce(directory.Path);
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new() { Preferences = new() { AccentColor = "#654321" } });

        SettingsRestoreStartup.ApplyOnce(directory.Path);

        Assert.True(restore.HasPending);
        Assert.False(File.Exists(Path.Combine(directory.Path, "preferences.json")));
    }

    [Fact]
    public async Task DamagedPendingBundleReportsAnErrorWithoutBlockingTheOriginalConfiguration()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        await store.SaveAsync(new() { AccentColor = "#123456" });
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new() { Preferences = new() { AccentColor = "#654321" } });
        await File.WriteAllTextAsync(Path.Combine(directory.Path, ".settings-restore", "pending.aegisettings"), "damaged");

        SettingsRestoreStartup.ApplyOnce(directory.Path);
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.Equal("#123456", context.Preferences.AccentColor);
        Assert.NotNull(context.LastError);
        Assert.Null(context.SettingsLoadError);
        Assert.True(context.SettingsRestore.HasPending);
        await context.SettingsRestore.CancelPendingAsync();
        Assert.False(context.SettingsRestore.HasPending);
    }

    [Fact]
    public async Task ADirectoryAliasCannotApplyPendingSettingsAgainInTheSameProcess()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var personal = Path.Combine(directory.Path, "personal");
        Directory.CreateDirectory(personal);
        var alias = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(alias, personal);
        try
        {
            SettingsRestoreStartup.ApplyOnce(personal);
            using var restore = new UserSettingsRestoreService(alias);
            await restore.StageAsync(new() { Preferences = new() { AccentColor = "#654321" } });

            SettingsRestoreStartup.ApplyOnce(alias);

            Assert.True(restore.HasPending);
            Assert.False(File.Exists(Path.Combine(personal, "preferences.json")));
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    [Fact]
    public async Task CorruptRecentProjectsDoNotPreventBackingUpSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "recent-projects.json"), "{broken");
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.NotNull(context.LastError);
        Assert.Null(context.SettingsLoadError);
    }

    [Fact]
    public async Task CorruptLibraryIsIsolatedFromSettingsUntilAValidLibraryIsPublished()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "effect-scripts.json");
        await File.WriteAllTextAsync(path, "{broken");
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        Assert.NotNull(context.EffectLibraryLoadError);
        Assert.Null(context.SettingsLoadError);

        await EffectScriptPresetStore.SaveAsync(new(), path);
        await context.RunEffectOperationAsync(() => context.EffectScriptLibrary.LoadAsync());

        Assert.Null(context.SettingsLoadError);
        Assert.Null(context.EffectLibraryLoadError);
    }

    [Fact]
    public async Task CommittingValidPreferencesClearsTheirInitialBackupError()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "preferences.json"), "{broken");
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        Assert.NotNull(context.SettingsLoadError);

        context.UpdatePreferences(value => value with { AccentColor = "#123456" });
        await context.Completion;

        Assert.Null(context.SettingsLoadError);
    }
}
