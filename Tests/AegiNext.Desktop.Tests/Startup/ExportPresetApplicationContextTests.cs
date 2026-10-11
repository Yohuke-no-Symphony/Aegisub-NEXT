using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Tests.Workspace;
using AegiNext.Desktop.Workspace;
using AegiNext.Export;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests.Startup;

/// <summary>验证共享压制预设库的队列、错误恢复、窗口同步和关闭行为。</summary>
[Collection("Workspace session")]
public sealed class ExportPresetApplicationContextTests
{
    /// <summary>资源冲突的队首保持提交顺序，启动后独立库仍可并行更新。</summary>
    [Fact]
    public async Task ConflictingExportQueueHeadPreservesOrderBeforeIndependentStyleWork()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exportChanges = 0;
        var styleChanges = 0;
        var effectChanges = 0;
        context.ExportPresetsChanged += (_, _) => exportChanges++;
        context.StylesChanged += (_, _) => styleChanges++;
        context.EffectsChanged += (_, _) => effectChanges++;
        var preset = new VideoExportPreset(Guid.NewGuid(), "排队配置", new());
        var first = context.RunExportPresetOperationAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        var second = context.RunExportPresetOperationAsync(async () =>
        {
            nextEntered.TrySetResult();
            await releaseSecond.Task;
            await context.ExportPresetLibrary.UpsertAsync(preset);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(nextEntered.Task.IsCompleted);
            Assert.True(context.ExportPresetsBusy);
            Assert.False(context.StylesBusy);
            Assert.False(context.EffectsBusy);
            Assert.Equal(0, exportChanges);
            var style = new SubtitleStylePreset(Guid.NewGuid(), "独立字幕样式", new());
            var styleWork = context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
            Assert.False(styleWork.IsCompleted);
            Assert.False(nextEntered.Task.IsCompleted);
            release.TrySetResult();
            await nextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await styleWork.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, styleChanges);
            Assert.Equal(0, effectChanges);
            Assert.Equal(0, exportChanges);
            Assert.True(nextEntered.Task.IsCompleted);
            releaseSecond.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(preset, Assert.Single(context.ExportPresetLibrary.Snapshot.Presets));
            Assert.Equal(1, exportChanges);
            Assert.False(context.ExportPresetsBusy);
            Assert.False(context.StylesBusy);
            Assert.False(context.EffectsBusy);
        }
        finally
        {
            release.TrySetResult();
            releaseSecond.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    /// <summary>压制预设队列向调用方报告失败，但完成信号和后续操作保持可用。</summary>
    [Fact]
    public async Task FailedExportOperationPreservesSnapshotAndDoesNotPoisonCompletionOrQueue()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var snapshot = context.ExportPresetLibrary.Snapshot;
        var failure = new IOException("Rejected export preset write");
        var errors = 0;
        var changes = 0;
        context.ErrorChanged += (_, _) => errors++;
        context.ExportPresetsChanged += (_, _) => changes++;

        var observed = await Assert.ThrowsAsync<IOException>(() =>
            context.RunExportPresetOperationAsync(() => Task.FromException(failure)));
        await context.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(failure, observed);
        Assert.Same(failure, context.LastError);
        Assert.Same(snapshot, context.ExportPresetLibrary.Snapshot);
        Assert.Equal(1, errors);
        Assert.Equal(0, changes);
        Assert.False(context.ExportPresetsBusy);
        var preset = new VideoExportPreset(Guid.NewGuid(), "失败后保存", new());
        await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(preset));
        Assert.Equal(preset, Assert.Single(context.ExportPresetLibrary.Snapshot.Presets));
        Assert.Equal(1, changes);
    }

    /// <summary>共享库刷新多个窗口而不自动套用，主动选择才更新完整压制参数。</summary>
    [Fact]
    public async Task SharedSessionsRefreshWithoutApplyingUntilPresetIsSelected()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        await using var first = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: context);
        await using var second = new WorkbenchSession(new WorkspaceDialogStub(), dispatch: DispatchImmediately,
            applicationContext: context);
        await Task.WhenAll(first.Styles.Completion, second.Styles.Completion);
        var firstSettings = new VideoExportSettings { Crf = 15, Preset = "fast" };
        var secondSettings = new VideoExportSettings { Crf = 31, Preset = "slow", AudioMode = AudioExportMode.None };
        first.ViewModel.Export.ApplySettings(firstSettings);
        second.ViewModel.Export.ApplySettings(secondSettings);
        var preset = new VideoExportPreset(Guid.NewGuid(), "共享硬件配置", new()
        {
            Codec = VideoCodec.Hevc,
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.CBR,
            Preset = "slower",
            Crf = 17,
            VideoBitrate = 12345678,
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        });

        await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(preset));

        Assert.Equal(preset.Id, Assert.Single(first.ViewModel.Export.Presets).Id);
        Assert.Equal(preset.Id, Assert.Single(second.ViewModel.Export.Presets).Id);
        Assert.Equal(firstSettings, first.ViewModel.Export.CaptureSettings(true));
        Assert.Equal(secondSettings, second.ViewModel.Export.CaptureSettings(true));
        Assert.Null(first.ViewModel.Export.SelectedPreset);
        Assert.Null(second.ViewModel.Export.SelectedPreset);

        first.ViewModel.Export.SelectedPreset = Assert.Single(first.ViewModel.Export.Presets);

        Assert.Equal(preset.Settings, first.ViewModel.Export.CaptureSettings(true));
        Assert.True(first.ViewModel.Export.UseHardwareEncoder);
        Assert.Equal(1, first.ViewModel.Export.QualityMode);
        Assert.Equal(1, first.ViewModel.Export.BitrateMode);
        Assert.Equal(secondSettings, second.ViewModel.Export.CaptureSettings(true));
        var updated = preset with { Name = "共享配置改名", Settings = preset.Settings with { VideoBitrate = 16000000 } };
        await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(updated));

        Assert.Equal(updated.Name, first.ViewModel.Export.SelectedPreset?.Name);
        Assert.Equal(preset.Settings, first.ViewModel.Export.CaptureSettings(true));
        Assert.Equal(secondSettings, second.ViewModel.Export.CaptureSettings(true));
        await first.DisposeAsync();
        await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.RemoveAsync(preset.Id));
        Assert.True(first.IsClosing);
        Assert.False(second.IsClosing);
        Assert.Empty(second.ViewModel.Export.Presets);
        Assert.Equal(secondSettings, second.ViewModel.Export.CaptureSettings(true));
    }

    /// <summary>应用关闭排空正在执行的压制库操作，拒绝新工作后才释放共享库。</summary>
    [Fact]
    public async Task ApplicationDisposalDrainsExportQueueBeforeDisposingLibrary()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preset = new VideoExportPreset(Guid.NewGuid(), "关闭前保存", new());
        var operation = context.RunExportPresetOperationAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            await context.ExportPresetLibrary.UpsertAsync(preset);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = context.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                context.RunExportPresetOperationAsync(() => Task.CompletedTask));
            release.TrySetResult();
            await Task.WhenAll(operation, disposal).WaitAsync(TimeSpan.FromSeconds(5));

            var stored = await VideoExportPresetStore.LoadAsync(Path.Combine(directory.Path, "export-presets.aegiexports"));
            Assert.Equal(preset, Assert.Single(stored.Presets));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.ExportPresetLibrary.LoadAsync());
            await context.DisposeAsync();
        }
        finally
        {
            release.TrySetResult();
            await operation;
            await context.DisposeAsync();
        }
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
