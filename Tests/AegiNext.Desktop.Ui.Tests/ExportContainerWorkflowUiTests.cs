using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Export;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportContainerWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(VideoContainerFormat.MP4, ".MP4")]
    [InlineData(VideoContainerFormat.MKV, ".mkv")]
    [InlineData(VideoContainerFormat.WEBM, ".webm")]
    public async Task SelectedContainerControlsPickerAndFrozenOutput(VideoContainerFormat container, string extension)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.SelectedContainer = container;
        model.AudioMode = (int)AudioExportMode.None;
        var output = Path.ChangeExtension(context.OutputPath, extension);
        context.Dialogs.ResolveOutput(output);

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.SelectedContainer = container == VideoContainerFormat.MKV ? VideoContainerFormat.MP4 : VideoContainerFormat.MKV;
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(output, request.OutputPath);
        Assert.Equal(container, request.ToSettings().Container);
        Assert.Equal(extension.ToLowerInvariant(), context.Dialogs.DefaultExtension);
        Assert.Equal<string>(["*" + extension.ToLowerInvariant()], context.Dialogs.Patterns);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task MismatchedFileExtensionDoesNotSubmitExportOrRenameTheTarget()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Session.ViewModel.Export.SelectedContainer = VideoContainerFormat.MKV;
        await File.WriteAllTextAsync(context.OutputPath, "existing output");
        context.Dialogs.ResolveOutput(context.OutputPath);

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Contains(".mkv", context.Dialogs.ErrorMessages[0], StringComparison.Ordinal);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal("existing output", await File.ReadAllTextAsync(context.OutputPath));
        Assert.True(context.Session.Export.CanStart);
    }

    [AvaloniaTheory]
    [InlineData("aac", AudioExportMode.Opus)]
    [InlineData("pcm_s16le", AudioExportMode.Opus)]
    [InlineData("opus", AudioExportMode.Copy)]
    [InlineData("vorbis", AudioExportMode.Copy)]
    public async Task KnownSourceUsesCachedCodecAndOnlyConvertsIncompatibleAudio(string codec, AudioExportMode expected)
    {
        var probeCalls = 0;
        await using var context = new ExportWorkspaceTestContext(media: Media(codec), exportMediaProbe: (_, _) =>
        {
            probeCalls++;
            throw new InvalidOperationException("Cached audio should not require another probe.");
        });
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.SelectedContainer = VideoContainerFormat.WEBM;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".webm"));

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected, request.AudioMode);
        Assert.Equal((int)expected, model.AudioMode);
        Assert.Equal(0, probeCalls);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    [AvaloniaTheory]
    [InlineData("aac", AudioExportMode.Opus)]
    [InlineData("opus", AudioExportMode.Copy)]
    [InlineData("vorbis", AudioExportMode.Copy)]
    public async Task UnknownSourceIsProbedOnceBeforeCapturingFinalAudio(string codec, AudioExportMode expected)
    {
        var probeCalls = 0;
        await using var context = new ExportWorkspaceTestContext(media: Media(null), exportMediaProbe: (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            probeCalls++;
            return Task.FromResult(Source(codec));
        });
        await context.InitializeAsync();
        context.Session.ViewModel.Export.SelectedCodec = VideoCodec.Vp9;
        context.Session.ViewModel.Export.SelectedContainer = VideoContainerFormat.WEBM;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".webm"));

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, probeCalls);
        Assert.Equal(expected, request.AudioMode);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task UnreportedSourceCodecFailsBeforePickerAndPreservesCopyIntent()
    {
        await using var context = new ExportWorkspaceTestContext(media: Media(null),
            exportMediaProbe: (_, _) => Task.FromResult(Source(null)));
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.SelectedContainer = VideoContainerFormat.WEBM;

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal((int)AudioExportMode.Copy, model.AudioMode);
    }

    [AvaloniaFact]
    public async Task ConversionValidatesPreviouslyInactiveBitrateBeforePicker()
    {
        await using var context = new ExportWorkspaceTestContext(media: Media(null),
            exportMediaProbe: (_, _) => Task.FromResult(Source("aac")));
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.SelectedContainer = VideoContainerFormat.WEBM;
        model.AudioBitrateText = "unfinished bitrate";

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.Equal("unfinished bitrate", model.AudioBitrateText);
        Assert.Equal("AudioBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSettingsRejectLateProbeWithoutChangingAudio(bool fails)
    {
        var probe = new TaskCompletionSource<MediaAssetInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var context = new ExportWorkspaceTestContext(media: Media(null), exportMediaProbe: (_, _) =>
        {
            entered.TrySetResult();
            return probe.Task;
        });
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.SelectedContainer = VideoContainerFormat.WEBM;

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.SelectedContainer = VideoContainerFormat.MKV;
        }
        finally
        {
            if (fails)
            {
                probe.TrySetException(new IOException("Stale probe failure"));
            }
            else
            {
                probe.TrySetResult(Source("aac"));
            }
        }
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(VideoContainerFormat.MKV, model.SelectedContainer);
        Assert.Equal((int)AudioExportMode.Copy, model.AudioMode);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    private static VideoPreviewMedia Media(string? codec)
    {
        return new(0, MediaTime.Zero, new(20), 1, 1, 1) { AudioCodecName = codec };
    }

    private static MediaAssetInfo Source(string? codec)
    {
        return new() { Streams = [new() { Index = 1, CodecType = "audio", CodecName = codec }] };
    }
}
