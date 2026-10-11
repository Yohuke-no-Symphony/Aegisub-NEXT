using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests.Settings;

/// <summary>验证设置交换页的操作状态与既有草稿离开保护。</summary>
[Collection("Workspace session")]
public sealed class TransferSettingsTests
{
    /// <summary>只有完成预览才能登记恢复，繁忙期间所有交换命令均禁用。</summary>
    [Fact]
    public void PreviewAndPendingStatesControlAvailableOperations()
    {
        var model = new UserSettingsTransferViewModel();
        Assert.True(model.KeepWorkspaceRoot);
        Assert.False(model.StageRestoreCommand.CanExecute(null));
        Assert.False(model.CancelPendingCommand.CanExecute(null));
        Assert.False(model.RestartCommand.CanExecute(null));

        model.SetPreview("settings.aegisettings", 2, 3, 4, 1);
        model.UpdatePending(true, true);
        Assert.True(model.StageRestoreCommand.CanExecute(null));
        Assert.True(model.CancelPendingCommand.CanExecute(null));
        Assert.True(model.RestartCommand.CanExecute(null));

        model.IsBusy = true;
        Assert.False(model.ExportCommand.CanExecute(null));
        Assert.False(model.ImportCommand.CanExecute(null));
        Assert.False(model.StageRestoreCommand.CanExecute(null));
        Assert.False(model.CancelPendingCommand.CanExecute(null));
        Assert.False(model.RestartCommand.CanExecute(null));
        model.IsBusy = false;
        model.ClearPreview();
        model.UpdatePending(false, true);
        Assert.True(model.ExportCommand.CanExecute(null));
        Assert.False(model.StageRestoreCommand.CanExecute(null));
        Assert.False(model.RestartCommand.CanExecute(null));
    }

    /// <summary>语言刷新和其他窗口的恢复状态更新保留已选文件及本机目录选择。</summary>
    [Fact]
    public void PresentationRefreshPreservesImportPreviewAndWorkspaceChoice()
    {
        var model = new UserSettingsTransferViewModel();
        model.SetPreview("个人设置.aegisettings", 2, 3, 4, 1);
        model.KeepWorkspaceRoot = false;
        model.RefreshLanguage();
        model.UpdatePending(true, false);

        Assert.True(model.HasPreview);
        Assert.Equal("个人设置.aegisettings", model.FileName);
        Assert.False(model.KeepWorkspaceRoot);
        Assert.True(model.HasPending);
        Assert.False(model.RestartCommand.CanExecute(null));
    }

    /// <summary>新增导航页追加在末尾，压制预设的未保存草稿仍可取消离开。</summary>
    [Fact]
    public async Task TransferNavigationRetainsExistingTemplateLeaveGuard()
    {
        Assert.Equal(9, (int)SettingsPage.EXPORT_PRESETS);
        Assert.Equal(10, (int)SettingsPage.TRANSFER);
        var model = new SettingsWindowViewModel(new());
        var preset = new VideoExportPreset(Guid.NewGuid(), "Saved", new());
        model.ExportPresets.UpdatePresets([preset]);
        model.ExportPresets.SaveDraftAsync = _ => Task.FromResult(true);
        model.ExportPresets.ConfirmLeaveAsync = () => Task.FromResult(2);
        Assert.True(await model.SelectPageAsync(SettingsPage.EXPORT_PRESETS));
        model.ExportPresets.Name = "Unsaved name";

        Assert.False(await model.SelectPageAsync(SettingsPage.TRANSFER));
        Assert.Equal(SettingsPage.EXPORT_PRESETS, model.CurrentPage);
        Assert.Equal("Unsaved name", model.ExportPresets.Name);
        Assert.True(model.ExportPresets.IsDirty);
        model.ExportPresets.ConfirmLeaveAsync = () => Task.FromResult(1);
        Assert.True(await model.SelectPageAsync(SettingsPage.TRANSFER));
        Assert.True(model.IsTransferVisible);
        Assert.False(model.ExportPresets.IsDirty);
    }
}
