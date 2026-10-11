using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Export;
using AegiNext.Export.Presets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportPresetUiTests
{
    /// <summary>压制面板仅套用预设，选择完整配置不产生工程事务。</summary>
    [AvaloniaFact]
    public async Task SelectingPresetAppliesCompleteConfigurationWithoutProjectChanges()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var first = new VideoExportPreset(Guid.NewGuid(), "CPU quality", new() { Quality = 26 });
        var second = new VideoExportPreset(Guid.NewGuid(), "CPU CBR", new()
        {
            Codec = VideoCodec.Hevc,
            RateControlMode = VideoRateControlMode.CBR,
            VideoBitrate = 10000000,
            Preset = "veryslow",
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        });
        await PublishAsync(context, first, second);
        var vm = context.Session.ViewModel.Export;
        var original = context.Editor.Snapshot;
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var combo = view.FindControl<ComboBox>("ExportPresetCombo")!;

        combo.SelectedItem = vm.Presets.Single(value => value.Id == second.Id);
        context.Flush();

        Assert.Equal(second.Settings, vm.CaptureSettings(true));
        Assert.Equal(0, view.FindControl<ComboBox>("SpeedCombo")!.SelectedIndex);
        Assert.True(view.FindControl<NumericDraftInput>("VideoBitrateInput")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<NumericDraftInput>("AudioBitrateInput")!.IsEffectivelyVisible);
        Assert.Null(view.FindControl<TextBox>("ExportPresetNameInput"));
        Assert.Null(view.FindControl<Button>("SaveExportPresetButton"));
        combo.SelectedItem = vm.Presets.Single(value => value.Id == first.Id);
        context.Flush();
        Assert.Equal(first.Settings, vm.CaptureSettings(true));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>语言和个人库更新保留选中身份与当前未完成参数，不重新套用。</summary>
    [AvaloniaFact]
    public async Task LanguageAndLibraryRefreshPreserveSelectedPresetAndCurrentInvalidDraft()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var preset = new VideoExportPreset(Guid.NewGuid(), "Stable selection", new());
        await PublishAsync(context, preset);
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        view.FindControl<ComboBox>("ExportPresetCombo")!.SelectedItem = vm.Presets[0];
        view.FindControl<NumericDraftInput>("QualityInput")!.RawText = "unfinished";

        Localization.SetLanguage("zh-CN");
        await PublishAsync(context, preset with { Name = "Renamed selection", Settings = new() { Quality = 30 } });
        context.Flush();

        Assert.Equal(preset.Id, vm.SelectedPreset!.Id);
        Assert.Equal("Renamed selection", vm.SelectedPreset.Name);
        Assert.Equal(vm.SelectedPreset, view.FindControl<ComboBox>("ExportPresetCombo")!.SelectedItem);
        Assert.Equal("unfinished", vm.QualityText);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>所选预设被其他窗口删除时清空选择，当前压制草稿保持不变。</summary>
    [AvaloniaFact]
    public async Task RemovingSelectedPresetRefreshesSelectorWithoutApplyingAnotherPreset()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var selected = new VideoExportPreset(Guid.NewGuid(), "Selected", new() { Quality = 26 });
        var remaining = new VideoExportPreset(Guid.NewGuid(), "Remaining", new() { Quality = 35 });
        await PublishAsync(context, selected, remaining);
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        view.FindControl<ComboBox>("ExportPresetCombo")!.SelectedItem = vm.Presets.Single(value => value.Id == selected.Id);
        vm.QualityText = "unfinished";
        var application = context.Session.ApplicationContext;

        await application.RunExportPresetOperationAsync(() => application.ExportPresetLibrary.RemoveAsync(selected.Id));
        context.Flush();

        Assert.Null(vm.SelectedPreset);
        Assert.Null(view.FindControl<ComboBox>("ExportPresetCombo")!.SelectedItem);
        Assert.Equal(remaining.Id, Assert.Single(vm.Presets).Id);
        Assert.Equal("unfinished", vm.QualityText);
        Assert.False(context.Editor.CanUndo);
    }

    private static async Task PublishAsync(ExportWorkspaceTestContext context, params VideoExportPreset[] presets)
    {
        var application = context.Session.ApplicationContext;
        foreach (var preset in presets)
        {
            await application.RunExportPresetOperationAsync(() => application.ExportPresetLibrary.UpsertAsync(preset));
        }
        context.Flush();
    }
}
