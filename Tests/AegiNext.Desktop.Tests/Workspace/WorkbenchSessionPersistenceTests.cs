using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkbenchSessionPersistenceTests
{
    [Fact]
    public async Task AutoSaveKeepsInvalidInspectorDraftAndBackupStillRunsAfterDirtyIsCleared()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        var result = await context.Session.CreateProjectAsync(new ProjectCreationRequest("字幕项目", context.DirectoryPath));
        Assert.Equal(Desktop.Workspace.ProjectOpenStatus.OPENED, result.Status);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(context.Session.ProjectDirectory, "字幕 01.mkv"));
        context.Editor.Apply("Media asset", document => document with { Assets = [asset] });
        var id = Assert.Single(context.Editor.Snapshot.Subtitles).Id;
        context.Editor.UpdateSubtitle(id, line => line with { End = new(1), Text = "中文 ABC 123" });
        context.Session.SelectCue(id);
        await context.Session.WaitForProjectIdleAsync();
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "not a number";
        var undo = context.Editor.UndoLabel;
        var snapshot = context.Editor.Snapshot;

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion;

        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal("not a number", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
        Assert.Equal(undo, context.Editor.UndoLabel);
        var saved = await ProjectStore.LoadAsync(context.Session.ProjectPath!);
        Assert.Equal("中文 ABC 123", Assert.Single(saved.Subtitles).Text);
        Assert.Equal(asset, Assert.Single(saved.Assets));
        clock.Advance(TimeSpan.FromMinutes(3));
        await context.Session.Persistence.Completion;
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(context.Session.ProjectDirectory, "backup"), "*.aeginext"));
        Assert.Equal("中文 ABC 123", Assert.Single((await ProjectStore.LoadAsync(backup)).Subtitles).Text);
        Assert.Equal("not a number", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
    }

    [Fact]
    public async Task CancelledWaitAfterCreationCommitKeepsTheNamedDirectoryAndAcceptedProject()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        context.Session.ApplicationContext.RecentProjects.Changed += (_, _) => cancellation.Cancel();
        var request = new ProjectCreationRequest("committed", context.DirectoryPath);

        var result = await context.Session.CreateProjectAsync(request, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(Desktop.Workspace.ProjectOpenStatus.CANCELLED, result.Status);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(ProjectCreationService.GetProjectPath(request), context.Session.ProjectPath);
        Assert.Equal("committed", (await ProjectStore.LoadAsync(context.Session.ProjectPath!)).Name);
        Assert.True(Directory.Exists(Path.Combine(context.Session.ProjectDirectory, "backup")));
        Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    [Fact]
    public async Task ClosingTheCreationDialogExplicitlyCancelsItsQueuedTaskWithoutCancellingOtherWork()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var gate = new QueuedSaveGateTask(context.Session.TaskScope);
        var gateHandle = context.Session.ApplicationContext.Tasks.Submit(gate);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var request = new ProjectCreationRequest("cancelled-dialog", context.DirectoryPath);
        var creation = context.Session.CreateProjectFromDialogAsync(request, cancellation.Token);
        try
        {
            Assert.Contains(context.Session.ApplicationContext.Tasks.GetSnapshots(), task =>
                task.Name == "Tasks.CreateProject" && task.State == AegiTaskState.Queued);

            cancellation.Cancel();
            var result = await creation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(Desktop.Workspace.ProjectOpenStatus.CANCELLED, result.Status);
            Assert.False(File.Exists(ProjectCreationService.GetProjectPath(request)));
            Assert.Null(context.Session.ProjectPath);
            Assert.Equal(AegiTaskState.Running, gateHandle.Snapshot.State);
            Assert.Contains(context.Session.ApplicationContext.Tasks.GetSnapshots(), task =>
                task.Name == "Tasks.CreateProject" && task.State == AegiTaskState.Cancelled);
        }
        finally
        {
            gate.Released.TrySetResult();
            await gateHandle.Completion;
            await creation;
        }
    }

    [Fact]
    public async Task ManualSaveThenUndoRewritesActualDiskAndPreservesUndoSavePoint()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        await context.Session.CreateProjectAsync(new ProjectCreationRequest("history", context.DirectoryPath));
        context.Editor.Apply("A", document => document with { Width = 1280 });
        await context.Session.WaitForProjectIdleAsync();
        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion;
        context.Editor.Apply("B", document => document with { Width = 1600 });
        await context.Session.WaitForProjectIdleAsync();
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        Assert.Equal(1600, (await ProjectStore.LoadAsync(context.Session.ProjectPath!)).Width);
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion;

        Assert.Equal(1280, (await ProjectStore.LoadAsync(context.Session.ProjectPath!)).Width);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Editor.Redo());
        Assert.True(context.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task BackupOpenIsRejectedAndNormalProjectRemainsActive()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        await context.Session.CreateProjectAsync(new ProjectCreationRequest("restore", context.DirectoryPath));
        var originalPath = context.Session.ProjectPath!;
        var backup = await ProjectBackupStore.WriteAsync(context.Editor.Snapshot, originalPath, DateTimeOffset.Now, 20);

        var result = await context.Session.OpenProjectAsync(backup);

        Assert.Equal(Desktop.Workspace.ProjectOpenStatus.FAILED, result.Status);
        Assert.Equal(originalPath, context.Session.ProjectPath);
        Assert.Equal(Localization.Get("Workbench.BackupRestoreRequired"), result.Error!.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(backup)!, "backup")));
    }

    [Fact]
    public async Task NewProjectPanelRejectsReentryAndCancellationResumesBothTimers()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        await context.Session.CreateProjectAsync(new ProjectCreationRequest("active", context.DirectoryPath));
        context.Dialogs.PendingNewProjectRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = context.Session.ProjectPath;
        var pending = context.Session.ExecuteCommandAsync(WorkbenchCommand.NEW_PROJECT);
        await context.Dialogs.NewProjectShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.NEW_PROJECT));
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.OPEN_PROJECT));
        Assert.Equal(0, clock.ActiveTimerCount);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.NEW_PROJECT);
        Assert.Equal(1, context.Dialogs.NewProjectRequests);
        context.Dialogs.PendingNewProjectRequest.SetResult(null);
        await pending;

        Assert.Equal(path, context.Session.ProjectPath);
        Assert.True(context.Session.CanExecuteCommand(WorkbenchCommand.NEW_PROJECT));
        Assert.Equal(2, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task CancelCloseRestartsTimersAndDisposeStopsFurtherWrites()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        await context.Session.CreateProjectAsync(new ProjectCreationRequest("close", context.DirectoryPath));
        context.Editor.Apply("Edit", document => document with { Width = 1280 });
        await context.Session.WaitForProjectIdleAsync();
        Assert.False(await context.Session.RequestCloseAsync());
        Assert.Equal(2, clock.ActiveTimerCount);
        await context.Session.DisposeAsync();
        Assert.Equal(0, clock.ActiveTimerCount);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(Directory.GetFiles(Path.Combine(context.Session.ProjectDirectory, "backup"), "*.aeginext"));
    }
}
