using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Export;
using AegiNext.Export.Presets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证设置页压制预设编辑、批量交换及窗口和工程草稿的隔离。</summary>
public sealed class ExportSettingsUiTests
{
    /// <summary>预设编辑器禁用当前平台不支持的 GPU，加载、刷新和离开不会改写原始预设。</summary>
    [AvaloniaTheory]
    [InlineData(VideoCodec.Vp9, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Vp9, VideoRateControlMode.CBR)]
    [InlineData(VideoCodec.Av1, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Av1, VideoRateControlMode.CBR)]
    public async Task HardwarePresetUsesPlatformToggleWithoutImplicitPersistence(VideoCodec codec, VideoRateControlMode mode)
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(codec) with
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = mode,
            VideoBitrate = 12345000
        };
        var original = new VideoExportPreset(Guid.NewGuid(), "Windows GPU", settings);
        await WithSettingsAsync(async (context, _, window, _, directory) =>
        {
            var path = Path.Combine(directory, "export-presets.aegiexports");
            var bytes = await File.ReadAllBytesAsync(path);
            var model = window.ViewModel.ExportPresets;
            var toggle = UiTestActions.Find<CheckBox>(window, "HardwareEncoderToggle");
            var supported = OperatingSystem.IsWindows();

            Assert.Equal(supported, toggle.IsEnabled);
            Assert.Equal(supported, toggle.IsChecked);
            Assert.Equal(supported, model.UseHardwareEncoder);
            Assert.Equal(1, UiTestActions.Find<ComboBox>(window, "QualityModeCombo").SelectedIndex);
            Assert.Equal(mode == VideoRateControlMode.CBR ? 1 : 0,
                UiTestActions.Find<ComboBox>(window, "BitrateModeCombo").SelectedIndex);
            Assert.Equal("12.345", UiTestActions.Find<NumericDraftInput>(window, "VideoBitrateInput").RawText);
            Assert.False(model.IsDirty);
            model.RefreshLanguage();
            model.UpdatePresets(context.ExportPresetLibrary.Snapshot.Presets);
            Dispatcher.UIThread.RunJobs();

            Assert.False(model.IsDirty);
            Assert.True(await model.SavePendingAsync());
            Assert.Equal(original, Assert.Single(context.ExportPresetLibrary.Snapshot.Presets));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.False(window.ViewModel.HasError);
        }, original);
    }

    /// <summary>设置编辑器通过真实控件改名和保存参数，保持预设身份且不新增副本。</summary>
    [AvaloniaFact]
    public async Task EditingAndRenamingPresetPersistsCompleteConfigurationWithOriginalId()
    {
        var original = new VideoExportPreset(Guid.NewGuid(), "Original", new());
        await WithSettingsAsync(async (context, coordinator, window, _, directory) =>
        {
            UiTestActions.SetText(UiTestActions.Find<TextBox>(window, "ExportPresetNameInput"), "Renamed 字幕");
            UiTestActions.Find<NumericDraftInput>(window, "QualityInput").RawText = "17";
            UiTestActions.Find<ComboBox>(window, "CodecCombo").SelectedIndex = 2;
            UiTestActions.Find<ComboBox>(window, "SpeedCombo").SelectedIndex = 0;
            UiTestActions.Find<CheckBox>(window, "HardwareEncoderToggle").IsChecked = true;
            UiTestActions.Find<ComboBox>(window, "BitrateModeCombo").SelectedIndex = 1;
            UiTestActions.Find<NumericDraftInput>(window, "VideoBitrateInput").RawText = "12.345678";
            UiTestActions.Find<ComboBox>(window, "AudioModeCombo").SelectedIndex = 1;
            UiTestActions.Find<NumericDraftInput>(window, "AudioBitrateInput").RawText = "256";
            Dispatcher.UIThread.RunJobs();

            UiTestActions.Click(window, "SaveExportPresetButton");
            await window.ViewModel.ExportPresets.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.ExportCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            var saved = Assert.Single(context.ExportPresetLibrary.Snapshot.Presets);
            Assert.Equal(original.Id, saved.Id);
            Assert.Equal("Renamed 字幕", saved.Name);
            Assert.Equal(CreateSettings() with { Preset = "veryslow" }, saved.Settings);
            var persisted = await VideoExportPresetStore.LoadAsync(Path.Combine(directory, "export-presets.aegiexports"));
            Assert.Equal(saved, Assert.Single(persisted.Presets));
            Assert.False(window.ViewModel.ExportPresets.IsDirty);
            Assert.Equal(saved.Id, window.ViewModel.ExportPresets.SelectedPreset?.Id);
            Assert.False(window.ViewModel.HasError);
            UiTestCapture.CaptureExportPanel(window, "settings-edited-preset");
        }, original);
    }

    /// <summary>多文件导入只提交一次完整集合，损坏文件使整个批次保持原样。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleFileImportIsAtomicAndPreservesAllParameters(bool corruptSecondFile)
    {
        var existing = new VideoExportPreset(Guid.NewGuid(), "Existing", new());
        await WithSettingsAsync(async (context, coordinator, window, dialogs, directory) =>
        {
            var first = new VideoExportPreset(Guid.NewGuid(), "Hardware", CreateSettings());
            var second = new VideoExportPreset(Guid.NewGuid(), "Software", new()
            {
                Codec = VideoCodec.H264, Quality = 0, VideoBitrate = 199999999,
                Preset = "ultrafast", AudioMode = AudioExportMode.None, AudioBitrate = 512000
            });
            var firstPath = Path.Combine(directory, "first.aegiexports");
            var secondPath = Path.Combine(directory, "second.aegiexports");
            await VideoExportPresetStore.SaveAsync(new() { Presets = [first] }, firstPath);
            if (corruptSecondFile)
            {
                await File.WriteAllTextAsync(secondPath, "{broken");
            }
            else
            {
                await VideoExportPresetStore.SaveAsync(new() { Presets = [second] }, secondPath);
            }
            dialogs.Inner.OpenPaths = [firstPath, secondPath];
            var before = context.ExportPresetLibrary.Snapshot;
            var libraryPath = Path.Combine(directory, "export-presets.aegiexports");
            var bytes = await File.ReadAllBytesAsync(libraryPath);

            UiTestActions.Click(window, "ImportExportPresetsButton");
            await coordinator.ExportCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, dialogs.InputRequests);
            Assert.False(window.ViewModel.ExportPresets.IsBusy);
            if (corruptSecondFile)
            {
                Assert.Same(before, context.ExportPresetLibrary.Snapshot);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
                Assert.Single(window.ViewModel.ExportPresets.ExportPresets);
                Assert.True(window.ViewModel.HasError);
            }
            else
            {
                Assert.Equal(3, context.ExportPresetLibrary.Snapshot.Presets.Length);
                Assert.Contains(first, context.ExportPresetLibrary.Snapshot.Presets);
                Assert.Contains(second, context.ExportPresetLibrary.Snapshot.Presets);
                Assert.Equal(3, window.ViewModel.ExportPresets.ExportPresets.Length);
                Assert.False(window.ViewModel.HasError);
            }
        }, existing);
    }

    /// <summary>批量导出在文件选择器打开前冻结选中项的已保存配置，并只生成一个文件。</summary>
    [AvaloniaFact]
    public async Task BatchExportCapturesSavedSelectionBeforePicker()
    {
        var presets = Enumerable.Range(1, 3).Select(index =>
            new VideoExportPreset(Guid.NewGuid(), $"Preset {index}", CreateSettings() with { VideoBitrate = index * 4000000 }))
            .ToArray();
        await WithSettingsAsync(async (context, coordinator, window, dialogs, directory) =>
        {
            var model = window.ViewModel.ExportPresets;
            Assert.True(await model.SelectPresetsAsync(presets[0].Id, [presets[0].Id, presets[2].Id]));
            Assert.Equal(2, UiTestActions.Find<ListBox>(window, "ExportPresetList").Selection.SelectedItems.Count);
            Assert.False(UiTestActions.Find<StackPanel>(window, "ExportPresetEditor").IsEffectivelyEnabled);
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.PendingOutput = pending;
            var output = Path.Combine(directory, "batch.aegiexports");
            UiTestActions.Click(window, "ExportExportPresetsButton");
            var completion = coordinator.ExportCompletion;
            try
            {
                await dialogs.OutputShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(model.IsBusy);
                Assert.False(UiTestActions.Find<Button>(window, "ImportExportPresetsButton").IsEffectivelyEnabled);
                var changed = presets[0] with { Name = "Changed after picker", Settings = presets[0].Settings with { VideoBitrate = 24000000 } };
                await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(changed));
                pending.TrySetResult(output);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));

                var exported = await VideoExportPresetStore.LoadAsync(output);
                Assert.Equal(new[] { presets[0], presets[2] }, exported.Presets.ToArray());
                Assert.Equal(changed, context.ExportPresetLibrary.Snapshot.Presets.Single(value => value.Id == changed.Id));
                Assert.Equal(1, dialogs.Inner.SaveCount);
                Assert.Equal(".aegiexports", dialogs.Inner.SaveExtension);
                Assert.Equal(0, dialogs.Inner.FolderRequests);
                Assert.False(model.IsBusy);
            }
            finally
            {
                pending.TrySetResult(null);
                await completion;
            }
        }, presets);
    }

    /// <summary>取消批量导出不写文件，不更改库，也不丢弃未保存编辑。</summary>
    [AvaloniaFact]
    public async Task CancelledExportKeepsUnsavedEditorAndPersonalLibrary()
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Saved", CreateSettings());
        await WithSettingsAsync(async (context, coordinator, window, dialogs, directory) =>
        {
            var snapshot = context.ExportPresetLibrary.Snapshot;
            var model = window.ViewModel.ExportPresets;
            UiTestActions.SetText(UiTestActions.Find<TextBox>(window, "ExportPresetNameInput"), "Unsaved name");
            UiTestActions.Find<NumericDraftInput>(window, "VideoBitrateInput").RawText = "7e-";
            dialogs.Inner.SavePath = null;

            UiTestActions.Click(window, "ExportExportPresetsButton");
            await coordinator.ExportCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(snapshot, context.ExportPresetLibrary.Snapshot);
            Assert.Single(Directory.GetFiles(directory, "*.aegiexports"));
            Assert.Equal("Unsaved name", model.Name);
            Assert.Equal("7e-", model.VideoBitrateText);
            Assert.True(model.IsDirty);
            Assert.Equal(0, dialogs.Inner.PresetConfirmationRequests);
            Assert.Equal(1, dialogs.Inner.SaveCount);
        }, saved);
    }

    /// <summary>明确删除正在修改的预设后清除本地草稿，后续刷新不恢复已删除项。</summary>
    [AvaloniaFact]
    public async Task DeletingEditedPresetClearsDraftAndDoesNotRestoreDeletedIdentity()
    {
        var deleting = new VideoExportPreset(Guid.NewGuid(), "Deleting", new());
        var remaining = new VideoExportPreset(Guid.NewGuid(), "Remaining", CreateSettings());
        await WithSettingsAsync(async (context, coordinator, window, _, _) =>
        {
            window.ViewModel.ExportPresets.Name = "Unsaved deletion draft";
            UiTestActions.Find<NumericDraftInput>(window, "QualityInput").RawText = "7e-";

            UiTestActions.Click(window, "DeleteExportPresetButton");
            await coordinator.ExportCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(remaining, Assert.Single(context.ExportPresetLibrary.Snapshot.Presets));
            Assert.Null(window.ViewModel.ExportPresets.Draft);
            Assert.False(window.ViewModel.ExportPresets.IsDirty);
            Assert.False(window.ViewModel.ExportPresets.SaveCommand.CanExecute(null));
            var changed = remaining with { Name = "Remaining changed" };
            await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(changed));
            Assert.NotEqual(deleting.Id, window.ViewModel.ExportPresets.Draft?.Id);
            Assert.DoesNotContain(context.ExportPresetLibrary.Snapshot.Presets, value => value.Id == deleting.Id);
            Assert.False(window.ViewModel.ExportPresets.IsDirty);
        }, deleting, remaining);
    }

    /// <summary>关闭设置取消旧选择器，迟到结果不写文件或污染重新打开的窗口。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingPendingPickerCannotMutateReopenedSettings(bool export)
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Saved", new());
        await WithSettingsAsync(async (context, coordinator, window, dialogs, directory) =>
        {
            var input = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.PendingInput = input;
            dialogs.PendingOutput = output;
            var snapshot = context.ExportPresetLibrary.Snapshot;
            var lateOutput = Path.Combine(directory, "late-output.aegiexports");
            var importPath = Path.Combine(directory, "late-input.aegiexports");
            await VideoExportPresetStore.SaveAsync(new() { Presets = [new(Guid.NewGuid(), "Late import", CreateSettings())] }, importPath);
            UiTestActions.Click(window, export ? "ExportExportPresetsButton" : "ImportExportPresetsButton");
            var completion = coordinator.ExportCompletion;
            try
            {
                await (export ? dialogs.OutputShown.Task : dialogs.InputShown.Task).WaitAsync(TimeSpan.FromSeconds(5));
                var owner = Assert.IsType<Window>(window.Owner);
                window.Close();
                await window.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Null(coordinator.Window);
                Assert.False(window.IsVisible);
                await coordinator.OpenAsync(owner, page: SettingsPage.EXPORT_PRESETS);
                var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
                Assert.NotSame(window, reopened);
                reopened.ViewModel.ExportPresets.Name = "New window draft";
                input.TrySetResult([importPath]);
                output.TrySetResult(lateOutput);
                Dispatcher.UIThread.RunJobs();

                Assert.Same(snapshot, context.ExportPresetLibrary.Snapshot);
                Assert.False(File.Exists(lateOutput));
                Assert.Equal("New window draft", reopened.ViewModel.ExportPresets.Name);
                Assert.False(reopened.ViewModel.ExportPresets.IsBusy);
                Assert.Equal(saved.Id, Assert.Single(reopened.ViewModel.ExportPresets.ExportPresets).Id);
            }
            finally
            {
                input.TrySetResult([]);
                output.TrySetResult(null);
                await completion;
            }
        }, saved);
    }

    /// <summary>设置导航遵守压制草稿保存、丢弃或取消，并只在保存时写库。</summary>
    [AvaloniaTheory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    public async Task NavigationHonorsDirtyExportPresetDecision(int decision, bool leaves, bool saves)
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Saved", new());
        await WithSettingsAsync(async (context, _, window, dialogs, _) =>
        {
            dialogs.Inner.PresetChoice = decision;
            UiTestActions.SetText(UiTestActions.Find<TextBox>(window, "ExportPresetNameInput"), "Edited name");
            UiTestActions.Find<NumericDraftInput>(window, "QualityInput").RawText = "29";

            UiTestActions.SelectSettingsPage(window, SettingsPage.APPEARANCE);
            await window.ViewModel.NavigationCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(leaves ? SettingsPage.APPEARANCE : SettingsPage.EXPORT_PRESETS, window.CurrentPage);
            Assert.Equal(window.CurrentPage, UiTestActions.Find<ListBox>(window, "Navigation").SelectedValue);
            Assert.Equal(1, dialogs.Inner.PresetConfirmationRequests);
            var persisted = Assert.Single(context.ExportPresetLibrary.Snapshot.Presets);
            Assert.Equal(saved.Id, persisted.Id);
            Assert.Equal(saves ? "Edited name" : "Saved", persisted.Name);
            Assert.Equal(saves ? 29 : 20, persisted.Settings.Quality);
            Assert.Equal(!leaves, window.ViewModel.ExportPresets.IsDirty);
            Assert.True(window.ViewModel.IsNavigationAvailable);
        }, saved);
    }

    /// <summary>关闭协调器取消尚未决定的草稿对话框，迟到保存决定不影响新窗口。</summary>
    [AvaloniaFact]
    public async Task DisposingSettingsCancelsPendingDirtyDecisionWithoutSaving()
    {
        var first = new VideoExportPreset(Guid.NewGuid(), "First", new());
        var second = new VideoExportPreset(Guid.NewGuid(), "Second", CreateSettings());
        await WithSettingsAsync(async (context, coordinator, window, dialogs, _) =>
        {
            var snapshot = context.ExportPresetLibrary.Snapshot;
            var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.Inner.PendingPresetDecision = pending;
            window.ViewModel.ExportPresets.Name = "Old window draft";
            var switching = window.ViewModel.ExportPresets.SelectPresetsAsync(second.Id, [second.Id]);
            try
            {
                Assert.Equal(1, dialogs.Inner.PresetConfirmationRequests);
                Assert.False(switching.IsCompleted);
                var owner = Assert.IsType<Window>(window.Owner);
                coordinator.Dispose();
                Assert.False(await switching.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.False(pending.Task.IsCompleted);
                using var reopenedCoordinator = new SettingsWindowCoordinator(context, _ => dialogs);
                try
                {
                    await reopenedCoordinator.OpenAsync(owner, page: SettingsPage.EXPORT_PRESETS);
                    var reopened = reopenedCoordinator.Window!;
                    reopened.ViewModel.ExportPresets.Name = "New window draft";
                    pending.TrySetResult(0);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Same(snapshot, context.ExportPresetLibrary.Snapshot);
                    Assert.Equal("New window draft", reopened.ViewModel.ExportPresets.Name);
                    Assert.True(window.ViewModel.ExportPresets.SelectionCompletion.IsCompletedSuccessfully);
                    Assert.False(window.IsVisible);
                }
                finally
                {
                    reopenedCoordinator.Dispose();
                }
            }
            finally
            {
                pending.TrySetResult(2);
                await switching;
            }
        }, first, second);
    }

    /// <summary>捕获工作台配置不提交字幕或特效草稿，保存预设也不改变工程和历史。</summary>
    [AvaloniaFact]
    public async Task CapturingWorkspaceSettingsDoesNotCommitProjectDraftsOrUndo()
    {
        using var environment = new UiTestEnvironment();
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath));
        await context.Initialization;
        var line = new SubtitleLine { Text = "ab", End = new(2) };
        var editor = new ProjectEditor(new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        });
        var dialogs = new ExportSettingsDialogStub();
        await using var session = new WorkbenchSession(dialogs, dispatch: DispatchImmediately, editor: editor,
            applicationContext: context);
        await session.Styles.Completion;
        session.SelectCue(line.Id);
        session.Details.EditText(0, 1, "甲");
        session.ViewModel.Effects.PositionXText = "7e-";
        var settings = CreateSettings();
        session.ViewModel.Export.ApplySettings(settings);
        var document = editor.Snapshot;
        var owner = new Window();
        using var coordinator = new SettingsWindowCoordinator(context, _ => dialogs);
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, session, SettingsPage.EXPORT_PRESETS);
            var window = coordinator.Window!;
            UiTestActions.Click(window, "CaptureExportSettingsButton");
            await window.ViewModel.ExportPresets.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(settings, window.ViewModel.ExportPresets.CaptureSettings(true));
            Assert.True(window.ViewModel.ExportPresets.IsDirty);
            Assert.Empty(context.ExportPresetLibrary.Snapshot.Presets);
            UiTestActions.SetText(UiTestActions.Find<TextBox>(window, "ExportPresetNameInput"), "Captured daily");
            UiTestActions.Click(window, "SaveExportPresetButton");
            await window.ViewModel.ExportPresets.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(settings, Assert.Single(context.ExportPresetLibrary.Snapshot.Presets).Settings);
            Assert.Equal(settings, session.ViewModel.Export.CaptureSettings(true));
            Assert.Same(document, editor.Snapshot);
            Assert.Equal("ab", document.Subtitles[0].Text);
            Assert.Equal("甲b", session.Details.Line!.Text);
            Assert.Equal("7e-", session.ViewModel.Effects.PositionXText);
            Assert.False(editor.HasUnsavedChanges);
            Assert.False(editor.CanUndo);
            Assert.False(editor.CanRedo);
        }
        finally
        {
            var completion = coordinator.ExportCompletion;
            coordinator.Dispose();
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    private static async Task WithSettingsAsync(
        Func<DesktopApplicationContext, SettingsWindowCoordinator, SettingsWindow, ExportSettingsDialogStub, string, Task> test,
        params VideoExportPreset[] presets)
    {
        using var environment = new UiTestEnvironment();
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath));
        await context.Initialization;
        foreach (var preset in presets)
        {
            await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(preset));
        }
        var dialogs = new ExportSettingsDialogStub();
        using var coordinator = new SettingsWindowCoordinator(context, _ => dialogs);
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.EXPORT_PRESETS);
            await test(context, coordinator, coordinator.Window!, dialogs, environment.DirectoryPath);
        }
        finally
        {
            var completion = coordinator.ExportCompletion;
            coordinator.Dispose();
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    private static VideoExportSettings CreateSettings()
    {
        return new()
        {
            Codec = VideoCodec.Hevc,
            Container = VideoContainerFormat.MP4,
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.CBR,
            Preset = "slower",
            Quality = 17,
            VideoBitrate = 12345678,
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        };
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
