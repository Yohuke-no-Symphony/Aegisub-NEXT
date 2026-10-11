using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchRegressionUiTests
{
    [AvaloniaFact]
    public async Task ActualFilePickerAdapterBuildsLocalizedOpenRequestAndCancellationDoesNotModifyProject()
    {
        await using var context = new MainWindowTestContext();
        var storage = System.Reflection.DispatchProxy.Create<Avalonia.Platform.Storage.IStorageProvider, RecordingStorageProvider>();
        var recorder = (RecordingStorageProvider)storage;
        var dialogs = new WindowWorkbenchDialogService(context.Window, () => storage);
        var before = context.Session.DocumentSnapshot;
        var path = await dialogs.OpenFileAsync("Open", "Videos", ["*.mp4", "*.mkv"]);
        Assert.Null(path);
        Assert.NotNull(recorder.OpenOptions);
        Assert.False(recorder.OpenOptions.AllowMultiple);
        Assert.False(string.IsNullOrWhiteSpace(recorder.OpenOptions.Title));
        Assert.Equal<string>(["*.mp4", "*.mkv"], Assert.Single(recorder.OpenOptions.FileTypeFilter!).Patterns);
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task ExportChoicesRemainBoundAndSelectionsAndInvalidInputSurviveLanguageRoundTrip()
    {
        await using var context = new MainWindowTestContext();
        context.Window.GetCommand(WorkbenchCommand.VIEW_EXPORT).Execute(null);
        var model = context.ViewModel.Export;
        var codec = UiTestActions.Find<ComboBox>(context.Window, "CodecCombo");
        var speed = UiTestActions.Find<ComboBox>(context.Window, "SpeedCombo");
        var audio = UiTestActions.Find<ComboBox>(context.Window, "AudioModeCombo");
        Assert.Equal(6, codec.ItemCount);
        Assert.Equal(9, speed.ItemCount);
        Assert.Equal("medium", model.EncodingPreset);
        Assert.Equal(4, audio.ItemCount);
        codec.SelectedIndex = 2;
        speed.SelectedIndex = 0;
        audio.SelectedIndex = 1;
        UiTestActions.Find<NumericDraftInput>(context.Window, "QualityInput").RawText = "pending";
        try
        {
        foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, codec.ItemCount);
            Assert.Equal(9, speed.ItemCount);
            Assert.Equal(4, audio.ItemCount);
            Assert.Equal(2, codec.SelectedIndex);
            Assert.Equal(0, speed.SelectedIndex);
            Assert.Equal("veryslow", model.EncodingPreset);
            Assert.Equal(1, audio.SelectedIndex);
            Assert.Equal("pending", model.QualityText);
            Assert.False(model.IsRunning);
        }
        }
        finally
        {
            UiTestActions.Find<NumericDraftInput>(context.Window, "QualityInput").RawText = "20";
            model.QualityText = "20";
            model.Codec = 2;
            model.Speed = 0;
            model.AudioMode = 1;
        }
    }
}
