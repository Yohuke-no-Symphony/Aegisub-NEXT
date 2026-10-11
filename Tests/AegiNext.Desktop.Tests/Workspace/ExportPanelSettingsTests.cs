using System.Globalization;
using AegiNext.Desktop.Editing;
using AegiNext.Export;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ExportPanelSettingsTests
{
    /// <summary>切换编码后端保留软件模式和未完成草稿。</summary>
    [Fact]
    public async Task HardwareToggleRemembersCpuQualityModeAndRetainsBothRawDrafts()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.CrfText = "pending quality";
        vm.VideoBitrateText = "pending bitrate";

        vm.UseHardwareEncoder = true;
        Assert.Equal(1, vm.QualityMode);
        vm.QualityMode = 0;
        Assert.Equal(1, vm.QualityMode);
        vm.UseHardwareEncoder = false;
        Assert.Equal(0, vm.QualityMode);
        vm.QualityMode = 1;
        vm.BitrateMode = 1;
        vm.UseHardwareEncoder = true;
        vm.UseHardwareEncoder = false;

        Assert.Equal(1, vm.QualityMode);
        Assert.Equal(1, vm.BitrateMode);
        Assert.Equal("pending quality", vm.CrfText);
        Assert.Equal("pending bitrate", vm.VideoBitrateText);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>全部编码速度经预设投影后仍完整保存。</summary>
    [Theory]
    [InlineData("veryslow", 0)]
    [InlineData("slower", 1)]
    [InlineData("slow", 2)]
    [InlineData("medium", 3)]
    [InlineData("fast", 4)]
    [InlineData("faster", 5)]
    [InlineData("veryfast", 6)]
    [InlineData("superfast", 7)]
    [InlineData("ultrafast", 8)]
    public async Task ApplySettingsPreservesEverySupportedEncodingSpeed(string preset, int index)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var settings = new VideoExportSettings { Preset = preset };
        var vm = context.Session.ViewModel.Export;

        vm.ApplySettings(settings);

        Assert.Equal(index, vm.Speed);
        Assert.Equal(preset, vm.EncodingPreset);
        Assert.Equal(settings, vm.CaptureSettings(true));
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>运行和预设捕获共享显式模式且只读活动草稿。</summary>
    [Theory]
    [InlineData(false, 0, 0, VideoRateControlMode.CRF)]
    [InlineData(false, 1, 0, VideoRateControlMode.VBR)]
    [InlineData(false, 1, 1, VideoRateControlMode.CBR)]
    [InlineData(true, 1, 0, VideoRateControlMode.VBR)]
    [InlineData(true, 1, 1, VideoRateControlMode.CBR)]
    public async Task CaptureSettingsProducesExplicitImmutableModesWithoutWritingValues(bool hardware,
        int qualityMode, int bitrateMode, VideoRateControlMode expectedMode)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.UseHardwareEncoder = hardware;
        vm.QualityMode = qualityMode;
        vm.BitrateMode = bitrateMode;
        vm.CrfText = "25";
        vm.VideoBitrateText = "12";
        var original = context.Editor.Snapshot;

        var captured = vm.CaptureSettings(true);
        vm.CrfText = "30";
        vm.VideoBitrateText = "18";

        Assert.Equal(expectedMode, captured.RateControlMode);
        Assert.Equal(hardware ? VideoEncodingMode.HARDWARE : VideoEncodingMode.SOFTWARE, captured.EncodingMode);
        Assert.Equal(25, captured.Crf);
        Assert.Equal(12000000, captured.VideoBitrate);
        Assert.Equal(20m, vm.Crf);
        Assert.Equal(8m, vm.VideoBitrate);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>活动字段校验失败保留原文并定位对应控件。</summary>
    [Theory]
    [InlineData(0, 0, "20.5", "8", "192", "CrfInput")]
    [InlineData(1, 0, "20", "invalid", "192", "VideoBitrateInput")]
    [InlineData(1, 0, "20", "0.099", "192", "VideoBitrateInput")]
    [InlineData(0, 1, "20", "8", "invalid", "AudioBitrateInput")]
    [InlineData(0, 1, "20", "8", "513", "AudioBitrateInput")]
    public async Task ActiveInvalidDraftReportsStableFieldAndLeavesAllDraftsUnchanged(int qualityMode,
        int audioMode, string quality, string bitrate, string audio, string field)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.QualityMode = qualityMode;
        vm.AudioMode = audioMode;
        vm.CrfText = quality;
        vm.VideoBitrateText = bitrate;
        vm.AudioBitrateText = audio;

        var error = Assert.Throws<ExportSettingsValidationException>(() => vm.CaptureSettings(true));

        Assert.Equal(field, error.FieldKey);
        Assert.Equal(quality, vm.CrfText);
        Assert.Equal(bitrate, vm.VideoBitrateText);
        Assert.Equal(audio, vm.AudioBitrateText);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>隐藏的无效草稿以最后有效值捕获，接受活动字段不清空它。</summary>
    [Fact]
    public async Task InactiveInvalidDraftsRemainEditableAcrossCaptureAndAcceptance()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.Crf = 27;
        vm.QualityMode = 1;
        vm.BitrateMode = 1;
        vm.CrfText = "unfinished quality";
        vm.VideoBitrateText = "12";
        vm.AudioBitrateText = "unfinished audio";

        var settings = vm.CaptureSettings(true);
        vm.AcceptSettings(settings);

        Assert.Equal(27, settings.Crf);
        Assert.Equal(192000, settings.AudioBitrate);
        Assert.Equal(VideoRateControlMode.CBR, settings.RateControlMode);
        Assert.Equal("unfinished quality", vm.CrfText);
        Assert.Equal("unfinished audio", vm.AudioBitrateText);
        Assert.Equal(12m, vm.VideoBitrate);
    }

    /// <summary>预设先完整校验再投影，失败不产生部分修改。</summary>
    [Fact]
    public async Task InvalidPresetCannotPartiallyReplaceTheCurrentConfiguration()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.CrfText = "unfinished";

        Assert.Throws<ArgumentException>(() => vm.ApplySettings(new()
        {
            Codec = VideoCodec.Hevc,
            Preset = "veryslow",
            Crf = 52
        }));

        Assert.Equal(0, vm.Codec);
        Assert.Equal(3, vm.Speed);
        Assert.Equal("unfinished", vm.CrfText);
    }

    /// <summary>同步库与选项只更新投影，不自动套用预设或丢失草稿。</summary>
    [Fact]
    public async Task RefreshPresetsAndChoicesPreserveSelectionAndUserDrafts()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        var selected = new VideoExportPreset(Guid.NewGuid(), "selected", new() { Crf = 30 });
        vm.CrfText = "unfinished";
        vm.Speed = 0;
        vm.QualityMode = 1;
        vm.BitrateMode = 1;
        vm.RefreshPresets([selected], selected.Id);

        vm.RefreshPresets([selected with { Name = "renamed" }]);
        vm.RefreshChoices(["Auto", "H264", "HEVC"], ["Fast", "Medium", "Slow"], ["Copy", "AAC", "None"]);

        Assert.Equal(selected.Id, vm.SelectedPreset!.Id);
        Assert.Equal("renamed", vm.SelectedPreset.Name);
        Assert.Equal("unfinished", vm.CrfText);
        Assert.Equal(0, vm.Speed);
        Assert.Equal("veryslow", vm.EncodingPreset);
        Assert.Equal(1, vm.QualityMode);
        Assert.Equal(1, vm.BitrateMode);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>预设数值按当前输入文化投影并准确回读。</summary>
    [Fact]
    public async Task PresetProjectionUsesCurrentNumericCulture()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var vm = context.Session.ViewModel.Export;
            var settings = new VideoExportSettings { RateControlMode = VideoRateControlMode.VBR, VideoBitrate = 12500000 };

            vm.ApplySettings(settings);

            Assert.Equal("12,5", vm.VideoBitrateText);
            Assert.Equal(settings, vm.CaptureSettings(true));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
