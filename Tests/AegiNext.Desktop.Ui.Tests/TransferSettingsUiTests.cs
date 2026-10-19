using AegiNext.Core.Presets;
using System.IO.Compression;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;
using AegiNext.Export.Presets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证设置交换界面的快照、完整预览、共享待恢复状态与关闭隔离。</summary>
public sealed class TransferSettingsUiTests
{
    /// <summary>导出冻结文件选择前的设置和实时布局，并排除独立效果库。</summary>
    [AvaloniaFact]
    public async Task ExportFreezesPreferencesLibrariesAndLiveLayoutBeforePicker()
    {
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            var original = new VideoExportPreset(Guid.NewGuid(), "Daily", new());
            await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(original));
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new SubtitleStylePreset(Guid.NewGuid(), "Personal style", new())));
            var preferences = context.Preferences;
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.Inner.PendingSaveSelection = pending;
            var path = Path.Combine(outputDirectory, "backup.aegisettings");
            UiTestActions.Click(window, "ExportUserSettingsButton");
            var completion = coordinator.TransferCompletion;
            try
            {
                Assert.Equal(1, dialogs.Inner.SaveCount);
                Assert.False(UiTestActions.Find<Button>(window, "ImportUserSettingsButton").IsEffectivelyEnabled);
                context.UpdatePreferences(value => value with { Volume = 0.25f });
                await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(original with { Name = "Changed" }));
                pending.TrySetResult(path);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));

                var exported = await UserSettingsBundleStore.LoadAsync(path);
                Assert.Equal(preferences, exported.Preferences);
                Assert.Equal(original, Assert.Single(exported.ExportPresets.Presets));
                Assert.Equal("Live layout", Assert.Single(exported.Layouts.Presets).Name);
                Assert.Equal(".aegisettings", dialogs.Inner.SaveExtension);
                using var archive = ZipFile.OpenRead(path);
                Assert.Equal(5, archive.Entries.Count);
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName is "subtitle-styles.aegistyles" or "effect-scripts.json");
                Assert.False(window.ViewModel.HasError);
            }
            finally
            {
                pending.TrySetResult(null);
                await completion;
            }
        }, captureLayout: CreateLayout);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LegacySettingsPreviewExplainsIgnoredMaterialsAndStagesOnlyVersionThree(int version)
    {
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            var styles = context.StyleLibrary.Snapshot;
            var effects = context.EffectScriptLibrary.Snapshot;
            var imported = new UserSettingsBundle
            {
                Preferences = context.Preferences with { Volume = 0.2f },
                ExportPresets = new() { Presets = [new(Guid.NewGuid(), "Legacy export", new())] },
                Layouts = CreateLayout(),
                ColorTags = version == 1 ? null : new()
            };
            var path = Path.Combine(outputDirectory, "legacy.aegisettings");
            WriteLegacySettingsFile(path, imported, version);
            dialogs.Inner.OpenPath = path;

            UiTestActions.Click(window, "ImportUserSettingsButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(window.ViewModel.Transfer.HasPreview);
            Assert.True(window.ViewModel.Transfer.HasIgnoredLegacyMaterials);
            Assert.Equal(Localization.Format("Settings.TransferPreviewSummary", 1, 1), window.ViewModel.Transfer.PreviewSummary);
            var notice = UiTestActions.Find<TextBlock>(window, "UserSettingsLegacyMaterialsNotice");
            Assert.True(notice.IsVisible);
            Assert.Equal(Localization.Get("Settings.TransferLegacyMaterialsIgnored"), notice.Text);

            UiTestActions.Click(window, "StageSettingsRestoreButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(styles, context.StyleLibrary.Snapshot);
            Assert.Same(effects, context.EffectScriptLibrary.Snapshot);
            var pendingPath = Path.Combine(context.PreferencesStore.DirectoryPath, ".settings-restore", "pending.aegisettings");
            var pending = await UserSettingsBundleStore.LoadAsync(pendingPath);
            Assert.Equal(3, pending.SourceVersion);
            Assert.Equal(version == 1, pending.ColorTags is null);
            using var archive = ZipFile.OpenRead(pendingPath);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName is "subtitle-styles.aegistyles" or "effect-scripts.json");
        });
    }

    /// <summary>确认关闭仅请求一次正常退出；稍后保留待恢复，共享窗口同步且当前配置不变。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageRestoreSharesPendingStateAndHonorsRestartDecision(bool restart)
    {
        var exitRequests = 0;
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            var preferences = context.Preferences;
            var library = context.ExportPresetLibrary.Snapshot;
            var imported = new UserSettingsBundle
            {
                Preferences = preferences with
                {
                    Volume = 0.15f,
                    Projects = preferences.Projects with { WorkspaceRoot = Path.Combine(outputDirectory, "ImportedProjects") }
                },
                ExportPresets = new() { Presets = [new(Guid.NewGuid(), "Imported", new())] },
                Layouts = CreateLayout()
            };
            var path = Path.Combine(outputDirectory, "restore.aegisettings");
            await UserSettingsBundleStore.SaveAsync(imported, path);
            dialogs.Inner.OpenPath = path;
            dialogs.RestartChoice = restart;
            using var other = new SettingsWindowCoordinator(context, _ => new TransferSettingsDialogStub());
            await other.OpenAsync(Assert.IsType<Window>(window.Owner), page: SettingsPage.TRANSFER);
            try
            {
                UiTestActions.Click(window, "ImportUserSettingsButton");
                await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(window.ViewModel.Transfer.HasPreview);
                Assert.True(UiTestActions.Find<CheckBox>(window, "KeepWorkspaceRootToggle").IsChecked);
                Assert.Equal("restore.aegisettings", window.ViewModel.Transfer.FileName);
                Assert.Equal(preferences, context.Preferences);
                Assert.False(context.SettingsRestore.HasPending);
                CaptureTransfer(window, "preview");

                UiTestActions.Click(window, "StageSettingsRestoreButton");
                await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                Assert.True(context.SettingsRestore.HasPending);
                Assert.True(window.ViewModel.Transfer.HasPending);
                Assert.True(other.Window!.ViewModel.Transfer.HasPending);
                Assert.Same(library, context.ExportPresetLibrary.Snapshot);
                Assert.Equal(preferences, context.Preferences);
                Assert.Equal(1, dialogs.RestartRequests);
                Assert.Equal(restart ? 1 : 0, exitRequests);
                var staged = await UserSettingsBundleStore.LoadAsync(Path.Combine(context.PreferencesStore.DirectoryPath,
                    ".settings-restore", "pending.aegisettings"));
                Assert.Equal(preferences.Projects.WorkspaceRoot, staged.Preferences.Projects.WorkspaceRoot);
                Assert.Equal(imported.Preferences.Volume, staged.Preferences.Volume);
                CaptureTransfer(window, "pending");

                UiTestActions.Click(window, "CancelSettingsRestoreButton");
                await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                Assert.False(context.SettingsRestore.HasPending);
                Assert.False(other.Window.ViewModel.Transfer.HasPending);
            }
            finally
            {
                other.Dispose();
                await other.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }, requestExit: () => exitRequests++);
    }

    /// <summary>逻辑配置目录及其符号链接别名都不能作为备份目标，不受文件扩展名影响。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRejectsPersonalConfigurationDirectoryAndSymbolicAliases(bool useAlias)
    {
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            await context.PreferencesStore.SaveAsync(context.Preferences);
            var formalPath = Path.Combine(context.PreferencesStore.DirectoryPath, "preferences.json");
            var before = await File.ReadAllBytesAsync(formalPath);
            var destination = formalPath;
            var alias = Path.Combine(outputDirectory, "settings-alias");
            if (useAlias)
            {
                Directory.CreateSymbolicLink(alias, context.PreferencesStore.DirectoryPath);
                destination = Path.Combine(alias, "preferences.json");
            }
            try
            {
                dialogs.Inner.SavePath = destination;
                UiTestActions.Click(window, "ExportUserSettingsButton");
                await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                var current = await File.ReadAllBytesAsync(formalPath);
                var aliasTarget = useAlias ? new DirectoryInfo(alias).ResolveLinkTarget(true)?.FullName : null;
                Assert.Equal(1, dialogs.Inner.SaveCount);
                Assert.True(window.ViewModel.HasError,
                    $"configuration={formalPath}; destination={destination}; aliasTarget={aliasTarget}; " +
                    $"saveCount={dialogs.Inner.SaveCount}; status={window.ViewModel.Transfer.Status}; preferencesUnchanged={before.AsSpan().SequenceEqual(current)}");
                Assert.Equal(before, current);
            }
            finally
            {
                if (useAlias)
                {
                    Directory.Delete(alias);
                }
            }
        });
    }

    /// <summary>关闭设置取消选择器，晚返回的路径既不导出文件也不创建导入预览。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingWindowIgnoresLatePickerResult(bool importing)
    {
        await WithSettingsAsync(async (_, coordinator, window, dialogs, outputDirectory) =>
        {
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (importing)
            {
                dialogs.Inner.PendingSelection = pending;
            }
            else
            {
                dialogs.Inner.PendingSaveSelection = pending;
            }
            var path = Path.Combine(outputDirectory, "late.aegisettings");
            UiTestActions.Click(window, importing ? "ImportUserSettingsButton" : "ExportUserSettingsButton");
            window.Close();
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(coordinator.Window);
            pending.TrySetResult(path);
            Dispatcher.UIThread.RunJobs();
            Assert.False(File.Exists(path));
            Assert.False(window.ViewModel.Transfer.HasPreview);
        });
    }

    /// <summary>关闭重启提示所在设置窗口会取消确认，晚确认不请求退出且保留已登记恢复。</summary>
    [AvaloniaFact]
    public async Task ClosingWindowIgnoresLateRestartConfirmation()
    {
        var exitRequests = 0;
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            var path = Path.Combine(outputDirectory, "restore.aegisettings");
            await UserSettingsBundleStore.SaveAsync(new(), path);
            dialogs.Inner.OpenPath = path;
            dialogs.PendingRestart = new(TaskCreationOptions.RunContinuationsAsynchronously);
            UiTestActions.Click(window, "ImportUserSettingsButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            UiTestActions.Click(window, "StageSettingsRestoreButton");
            await dialogs.RestartShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            dialogs.PendingRestart.TrySetResult(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, exitRequests);
            Assert.True(context.SettingsRestore.HasPending);
        }, requestExit: () => exitRequests++);
    }

    /// <summary>未保留本机目录时拒绝其他平台路径，保留导入预览且不登记恢复。</summary>
    [AvaloniaFact]
    public async Task ForeignWorkspacePathRequiresKeepingLocalDirectory()
    {
        await WithSettingsAsync(async (context, coordinator, window, dialogs, outputDirectory) =>
        {
            var foreign = OperatingSystem.IsWindows() ? "/home/example/Workspace" : "C:\\Users\\Example\\Workspace";
            var bundle = new UserSettingsBundle
            {
                Preferences = context.Preferences with
                {
                    Projects = context.Preferences.Projects with { WorkspaceRoot = foreign }
                }
            };
            var path = Path.Combine(outputDirectory, "foreign.aegisettings");
            await UserSettingsBundleStore.SaveAsync(bundle, path);
            dialogs.Inner.OpenPath = path;
            UiTestActions.Click(window, "ImportUserSettingsButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            UiTestActions.Find<CheckBox>(window, "KeepWorkspaceRootToggle").IsChecked = false;
            UiTestActions.Click(window, "StageSettingsRestoreButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(window.ViewModel.HasError);
            Assert.True(window.ViewModel.Transfer.HasPreview);
            Assert.False(context.SettingsRestore.HasPending);

            UiTestActions.Find<CheckBox>(window, "KeepWorkspaceRootToggle").IsChecked = true;
            UiTestActions.Click(window, "StageSettingsRestoreButton");
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.SettingsRestore.HasPending);
            Assert.False(window.ViewModel.HasError);
        });
    }

    private static async Task WithSettingsAsync(
        Func<DesktopApplicationContext, SettingsWindowCoordinator, SettingsWindow, TransferSettingsDialogStub, string, Task> test,
        Func<WorkspaceLayoutFile>? captureLayout = null, Action? requestExit = null)
    {
        using var environment = new UiTestEnvironment();
        var outputDirectory = environment.DirectoryPath + "-settings-export";
        Directory.CreateDirectory(outputDirectory);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath));
        await context.Initialization;
        var dialogs = new TransferSettingsDialogStub();
        using var coordinator = new SettingsWindowCoordinator(context, _ => dialogs, captureLayout, requestExit);
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TRANSFER);
            await test(context, coordinator, coordinator.Window!, dialogs, outputDirectory);
        }
        finally
        {
            coordinator.Dispose();
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
            Directory.Delete(outputDirectory, true);
        }
    }

    private static WorkspaceLayoutFile CreateLayout()
    {
        return new() { Presets = [new("user-live", "Live layout", false, WorkspaceLayoutPresets.Standard)] };
    }

    private static void WriteLegacySettingsFile(string path, UserSettingsBundle settings, int version)
    {
        var files = UserSettingsBundleStore.SerializeFiles(settings);
        files.Add("subtitle-styles.aegistyles", "ignored style document"u8.ToArray());
        files.Add("effect-scripts.json", [0xFF]);
        files.Add("manifest.json", System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version}}}"));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    private static void CaptureTransfer(SettingsWindow window, string state)
    {
        window.Width = 860;
        window.Height = 580;
        foreach (var (language, theme) in new[] { ("en-US", ThemeVariant.Light), ("zh-CN", ThemeVariant.Dark) })
        {
            Localization.SetLanguage(language);
            window.RequestedThemeVariant = theme;
            UiTestCapture.CaptureExportPanel(window, $"settings-transfer-{state}-{language}");
        }
    }
}
