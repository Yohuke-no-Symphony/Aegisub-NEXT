using AegiNext.Core.Timing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportWorkspaceLifecycleUiTests
{
    [AvaloniaFact]
    public async Task LayoutRoundTripPreservesExportSnapshotParametersProgressAndAllPanelInstances()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.QualityText = "25";
        session.ViewModel.Export.AudioBitrateText = "256";
        context.Dialogs.ResolveOutput(context.OutputPath);
        var snapshot = context.Editor.Snapshot;
        var controller = session.Controller;
        var position = session.Controller.Snapshot.Position;
        var panels = context.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
        var panelModels = panels.ToDictionary(pair => pair.Key, pair => pair.Value.DataContext);
        var exportModel = session.ViewModel.Export;
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = session.Export.Completion;
        Assert.Same(snapshot, request.Project);
        Assert.Equal(25, request.Quality);
        Assert.Equal(256000, request.AudioBitrate);
        Assert.True(exportModel.IsRunning);
        Assert.False(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        context.ExportService.Report(new(12, new MediaTime(2, 5), 0.4, "encoding"));
        context.Flush();
        var exportTask = Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(), task => task.Name == "Tasks.VideoExport");
        Assert.True(exportTask.CanCancel);
        Assert.True(exportModel.CanCancel);

        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.EFFECTS));
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Assert.True(await context.Layouts.RestoreDefaultAsync());
        context.Flush();

        Assert.Same(completion, session.Export.Completion);
        Assert.Same(exportModel, session.ViewModel.Export);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Same(controller, session.Controller);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(position, session.Controller.Snapshot.Position);
        Assert.Equal("25", exportModel.QualityText);
        Assert.Equal("256", exportModel.AudioBitrateText);
        Assert.Equal(0.4, exportModel.Progress);
        Assert.Equal(1, context.ExportService.RequestCount);
        Assert.False(operation.IsCompleted);
        foreach (var id in WorkbenchPanelIds.All)
        {
            Assert.Same(panels[id], context.Layouts.PanelAdapters[id].View);
            Assert.Same(panelModels[id], context.Layouts.PanelAdapters[id].View.DataContext);
        }

        await session.CancelExportAsync();
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.True(exportModel.IsRunning);
        context.ExportService.Report(new(27, new MediaTime(9, 10), 0.9, "encoding"));
        context.Flush();
        Assert.False(exportModel.CanCancel);
        Assert.False(Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(), task => task.Id == exportTask.Id).CanCancel);
        Assert.Equal(0.4, exportModel.Progress);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(exportModel.IsRunning);
        Assert.False(exportModel.ProgressVisible);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(1, context.ExportService.CancellationCount);
        Assert.Equal(0, context.ExportService.DisposeCount);
        Assert.True(await session.RequestCloseAsync(context.Layouts.FlushAsync));
        await session.DisposeAsync();
        Assert.Equal(1, context.SourceDisposeCount);
        Assert.Equal(1, context.ExportService.DisposeCount);
    }

    [AvaloniaFact]
    public async Task ChoosingOutputBlocksDuplicateExportAndCloseWaitsUntilPickerReturns()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        Assert.True(session.Export.IsChoosingOutput);
        Assert.False(session.Export.IsRunning);
        Assert.False(session.Export.Completion.IsCompleted);
        Assert.False(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        await session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await session.Export.EncodeAsync();
        Assert.Equal(1, context.Dialogs.SaveRequests);

        var close = session.RequestCloseAsync(context.Layouts.FlushAsync);
        Assert.True(session.IsClosing);
        Assert.False(close.IsCompleted);
        Assert.Equal(0, context.SourceDisposeCount);
        Assert.Equal(0, context.ExportService.DisposeCount);
        context.Dialogs.ResolveOutput(context.OutputPath);
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(session.Export.IsChoosingOutput);
        Assert.Equal(0, context.ExportService.RequestCount);
        await session.DisposeAsync();
        Assert.Equal(1, context.SourceDisposeCount);
        Assert.Equal(1, context.ExportService.DisposeCount);
    }

    [AvaloniaFact]
    public async Task ClosingCancelsExportAndWaitsForItsDrainBeforeReleasingAnyResource()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.ResolveOutput(context.OutputPath);
        var session = context.Session;
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var flushes = 0;

        var close = session.RequestCloseAsync(async () =>
        {
            flushes++;
            await context.Layouts.FlushAsync();
        });
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted);
        Assert.False(operation.IsCompleted);
        Assert.Equal(0, flushes);
        Assert.Equal(0, context.SourceDisposeCount);
        Assert.Equal(0, context.ExportService.DisposeCount);
        Assert.False(await session.RequestCloseAsync());
        context.ExportService.Release();
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, flushes);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
        Assert.Equal(1, context.ExportService.CancellationCount);
        Assert.Equal(1, context.SourceDisposeCount);
        Assert.Equal(1, context.ExportService.DisposeCount);
    }
}
