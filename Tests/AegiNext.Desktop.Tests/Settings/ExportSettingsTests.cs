using AegiNext.Desktop.Settings.Export;
using AegiNext.Export;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests.Settings;

/// <summary>验证压制预设编辑草稿、完整保存、批量选择及离开保护。</summary>
[Collection("Workspace session")]
public sealed class ExportSettingsTests
{
    /// <summary>编辑和改名保存使用原身份，并持久化全部活动及保留参数。</summary>
    [Fact]
    public async Task SavingEditedPresetPreservesIdentityAndPersistsCompleteSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "export-presets.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var stored = new VideoExportPreset(Guid.NewGuid(), "Original", new());
        await library.UpsertAsync(stored);
        var model = new ExportSettingsViewModel();
        model.UpdatePresets(library.Snapshot.Presets);
        var settings = CreateSettings();
        model.ApplySettings(settings);
        model.Name = "  Renamed 字幕  ";
        model.SaveDraftAsync = async preset =>
        {
            await library.UpsertAsync(preset);
            model.UpdatePresets(library.Snapshot.Presets, preset.Id);
            return true;
        };

        Assert.True(await model.SavePendingAsync());

        var saved = Assert.Single((await VideoExportPresetStore.LoadAsync(path)).Presets);
        Assert.Equal(stored.Id, saved.Id);
        Assert.Equal("Renamed 字幕", saved.Name);
        Assert.Equal(settings, saved.Settings);
        Assert.Equal(saved, model.SelectedPreset);
        Assert.Equal(saved, model.Draft);
        Assert.False(model.IsDirty);
        Assert.Null(model.Error);
    }

    /// <summary>与其他预设同名的修改在提交之前失败，保留原始编辑值。</summary>
    [Fact]
    public async Task DuplicateNameRejectsSaveWithoutCallingPersistence()
    {
        var first = new VideoExportPreset(Guid.NewGuid(), "Daily", new());
        var second = new VideoExportPreset(Guid.NewGuid(), "Other", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([first, second], second.Id);
        var saves = 0;
        model.SaveDraftAsync = _ =>
        {
            saves++;
            return Task.FromResult(true);
        };
        model.Name = "daily";
        model.VideoBitrateText = "18";

        Assert.False(await model.SavePendingAsync());

        Assert.Equal(0, saves);
        Assert.Equal(new[] { first, second }, model.ExportPresets.ToArray());
        Assert.Equal(second.Id, model.Draft?.Id);
        Assert.Equal("daily", model.Name);
        Assert.Equal("18", model.VideoBitrateText);
        Assert.Equal("ExportPresetNameInput", model.InvalidFieldKey);
        Assert.NotNull(model.Error);
        Assert.True(model.IsDirty);
    }

    /// <summary>无效活动码率保持可编辑草稿，刷新库和语言都不会覆盖原始文字。</summary>
    [Fact]
    public async Task InvalidActiveInputSurvivesRefreshUntilDiscardRestoresSavedPreset()
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Saved", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([saved]);
        var saves = 0;
        model.SaveDraftAsync = _ =>
        {
            saves++;
            return Task.FromResult(true);
        };
        model.VideoBitrateText = "7e-";

        Assert.False(await model.SavePendingAsync());
        model.UpdatePresets([saved, new(Guid.NewGuid(), "Another", new())]);
        model.RefreshLanguage();

        Assert.Equal(0, saves);
        Assert.Equal(saved.Id, model.SelectedPreset?.Id);
        Assert.Equal("7e-", model.VideoBitrateText);
        Assert.Equal("VideoBitrateInput", model.InvalidFieldKey);
        Assert.True(model.IsDirty);
        model.DiscardDraft();
        Assert.Equal(saved.Settings, model.CaptureSettings(true));
        Assert.False(model.IsDirty);
        Assert.Null(model.Error);
    }

    /// <summary>离开编辑项时分别遵守保存、丢弃和取消选择。</summary>
    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(1, true, 0)]
    [InlineData(2, false, 0)]
    public async Task DirtySelectionHonorsSaveDiscardAndCancel(int decision, bool expected, int expectedSaves)
    {
        var first = new VideoExportPreset(Guid.NewGuid(), "First", new());
        var second = new VideoExportPreset(Guid.NewGuid(), "Second", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([first, second]);
        model.Name = "Edited first";
        model.CrfText = "27";
        model.ConfirmLeaveAsync = () => Task.FromResult(decision);
        var saves = 0;
        VideoExportPreset? committed = null;
        model.SaveDraftAsync = preset =>
        {
            saves++;
            committed = preset;
            model.UpdatePresets([preset, second], preset.Id);
            return Task.FromResult(true);
        };

        Assert.Equal(expected, await model.SelectPresetsAsync(second.Id, [second.Id]));

        Assert.Equal(expectedSaves, saves);
        Assert.Equal(expected ? second.Id : first.Id, model.SelectedPreset?.Id);
        Assert.Equal(expected ? "Second" : "Edited first", model.Name);
        if (decision == 0)
        {
            Assert.NotNull(committed);
            Assert.Equal(first.Id, committed.Id);
            Assert.Equal("Edited first", committed.Name);
            Assert.Equal(27, committed.Settings.Crf);
        }
        if (!expected)
        {
            Assert.Equal("27", model.CrfText);
            Assert.True(model.IsDirty);
        }
    }

    /// <summary>切换预设等待存储结果，写入失败保持原选择与完整草稿。</summary>
    [Fact]
    public async Task FailedSaveBeforeSelectionKeepsDraftAndAllowsRetry()
    {
        var first = new VideoExportPreset(Guid.NewGuid(), "First", new());
        var second = new VideoExportPreset(Guid.NewGuid(), "Second", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([first, second]);
        model.Name = "Changed";
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.SaveDraftAsync = _ => result.Task;
        model.ConfirmLeaveAsync = () => Task.FromResult(0);
        var switching = model.SelectPresetsAsync(second.Id, [second.Id]);
        try
        {
            Assert.False(switching.IsCompleted);
            Assert.Equal(first.Id, model.SelectedPreset?.Id);
            Assert.False(model.ImportCommand.CanExecute(null));
            result.TrySetResult(false);
            Assert.False(await switching.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(first.Id, model.SelectedPreset?.Id);
            Assert.Equal("Changed", model.Name);
            Assert.True(model.IsDirty);
            model.SaveDraftAsync = preset =>
            {
                model.UpdatePresets([preset, second], preset.Id);
                return Task.FromResult(true);
            };

            Assert.True(await model.SelectPresetsAsync(second.Id, [second.Id]));
            Assert.Equal("Changed", model.ExportPresets[0].Name);
            Assert.Equal(second.Id, model.SelectedPreset?.Id);
        }
        finally
        {
            result.TrySetResult(false);
            await switching;
        }
    }

    /// <summary>批量导出只发出选中项的已保存不可变配置，批量选择禁止编辑。</summary>
    [Fact]
    public async Task BatchSelectionExportsSavedSnapshotsAndDisablesEditing()
    {
        var values = Enumerable.Range(1, 3).Select(index =>
            new VideoExportPreset(Guid.NewGuid(), $"Preset {index}", CreateSettings() with { VideoBitrate = index * 4000000 }))
            .ToArray();
        var model = new ExportSettingsViewModel();
        model.UpdatePresets(values);
        Assert.True(await model.SelectPresetsAsync(values[0].Id, [values[0].Id, values[2].Id]));
        model.Name = "Ignored editor text";
        model.VideoBitrateText = "invalid";
        VideoExportPreset[]? exported = null;
        model.ExportRequested += (_, args) => exported = args.Presets.ToArray();

        model.ExportCommand.Execute(null);

        Assert.Equal(new[] { values[0], values[2] }, exported);
        Assert.False(model.CanEdit);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.True(model.DeleteCommand.CanExecute(null));
        Assert.False(model.DuplicateCommand.CanExecute(null));
        Assert.True(model.ExportCommand.CanExecute(null));
    }

    /// <summary>复制已保存预设生成新身份，保存前不写入列表且保留全部参数。</summary>
    [Fact]
    public async Task DuplicatingPresetCreatesIndependentUnsavedDraftWithCompleteSettings()
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Daily", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([saved]);

        model.DuplicateCommand.Execute(null);
        await model.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(model.Draft);
        Assert.NotEqual(saved.Id, model.Draft.Id);
        Assert.NotEqual(saved.Name, model.Name);
        Assert.Equal(saved.Settings, model.CaptureSettings(true));
        Assert.Equal(saved, Assert.Single(model.ExportPresets));
        Assert.Empty(model.SelectedIds);
        Assert.True(model.IsDirty);
        var requests = 0;
        model.DeleteRequested += (_, args) =>
        {
            requests++;
            Assert.True(args.IsDraftOnly);
        };
        model.DeleteCommand.Execute(null);
        Assert.Equal(1, requests);
        Assert.NotNull(model.Draft);
        model.DiscardDraft();
        Assert.Null(model.Draft);
        Assert.Equal(saved, Assert.Single(model.ExportPresets));
    }

    /// <summary>其他窗口删除正在编辑的预设时保留孤立草稿，保存可用原身份恢复预设。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovingEditedPresetFromSharedLibraryPreservesDraftAndCanRestoreItsIdentity(bool valid)
    {
        var editing = new VideoExportPreset(Guid.NewGuid(), "Editing", new());
        var remaining = new VideoExportPreset(Guid.NewGuid(), "Remaining", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([editing, remaining]);
        model.Name = "Still editing";
        model.CrfText = valid ? "26" : "7e-";

        model.UpdatePresets([remaining]);

        Assert.Equal(editing.Id, model.Draft?.Id);
        Assert.Equal("Still editing", model.Name);
        Assert.Equal(valid ? "26" : "7e-", model.CrfText);
        Assert.True(model.IsDirty);
        Assert.Null(model.SelectedPreset);
        Assert.Empty(model.SelectedIds);
        Assert.Equal(remaining, Assert.Single(model.ExportPresets));
        model.CrfText = "26";
        VideoExportPreset? restored = null;
        model.SaveDraftAsync = preset =>
        {
            restored = preset;
            model.UpdatePresets([remaining, preset], preset.Id);
            return Task.FromResult(true);
        };

        Assert.True(await model.SavePendingAsync());
        Assert.NotNull(restored);
        Assert.Equal(editing.Id, restored.Id);
        Assert.Equal("Still editing", restored.Name);
        Assert.Equal(26, restored.Settings.Crf);
        Assert.Equal(editing.Id, model.SelectedPreset?.Id);
        Assert.False(model.IsDirty);
    }

    /// <summary>非法 UTF-16 名称通过字段错误拒绝保存，库刷新不抛异常或覆盖原始值。</summary>
    [Fact]
    public async Task InvalidUtf16NameRemainsEditableAcrossLibraryRefresh()
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), "Saved", new());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([saved]);
        model.Name = "\uD800";
        var saves = 0;
        model.SaveDraftAsync = _ =>
        {
            saves++;
            return Task.FromResult(true);
        };

        Assert.False(await model.SavePendingAsync());
        model.UpdatePresets([saved, new(Guid.NewGuid(), "Other", CreateSettings())]);

        Assert.Equal(0, saves);
        Assert.Equal("\uD800", model.Name);
        Assert.Equal(saved.Id, model.Draft?.Id);
        Assert.Equal("ExportPresetNameInput", model.InvalidFieldKey);
        Assert.NotNull(model.Error);
        Assert.True(model.IsDirty);
    }

    /// <summary>最大长度且含代理对的名称可以连续复制，生成的默认名称合法且不冲突。</summary>
    [Fact]
    public async Task DuplicatingMaximumLengthUnicodeNameCreatesUniqueImmediatelySavableNames()
    {
        var saved = new VideoExportPreset(Guid.NewGuid(), new string('A', 126) + "😀", CreateSettings());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([saved]);
        model.SaveDraftAsync = preset =>
        {
            VideoExportPresetValidator.Validate(preset);
            model.UpdatePresets(model.ExportPresets.Where(value => value.Id != preset.Id).Append(preset), preset.Id);
            return Task.FromResult(true);
        };

        model.DuplicateCommand.Execute(null);
        await model.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var firstName = model.Name;
        Assert.InRange(firstName.Length, 1, 128);
        Assert.True(await model.SavePendingAsync());
        Assert.True(await model.SelectPresetsAsync(saved.Id, [saved.Id]));
        model.DuplicateCommand.Execute(null);
        await model.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(model.Name.Length, 1, 128);
        Assert.False(string.Equals(firstName, model.Name, StringComparison.OrdinalIgnoreCase));
        Assert.True(await model.SavePendingAsync());
        Assert.Equal(3, model.ExportPresets.Length);
        Assert.All(model.ExportPresets, VideoExportPresetValidator.Validate);
        Assert.False(model.IsDirty);
    }

    private static VideoExportSettings CreateSettings()
    {
        return new()
        {
            Codec = VideoCodec.Hevc,
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.CBR,
            Preset = "slower",
            Crf = 17,
            VideoBitrate = 12345678,
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        };
    }
}
