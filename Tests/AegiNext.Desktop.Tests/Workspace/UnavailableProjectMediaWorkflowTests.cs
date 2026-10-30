using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class UnavailableProjectMediaWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuePreservesTheMediaReferenceAndAllowsEditingWithoutRetrying(bool external)
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var asset = external
            ? new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
                ExternalPath: Path.Combine(context.Session.ProjectDirectory, "missing.mkv"))
            : new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var path = await SaveProjectAsync(context, "offline.aeginext", asset);
        context.Dialogs.OpenPath = path;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Null(context.Session.ViewModel.Error);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);
        Assert.Equal(Path.Combine(context.Session.ProjectDirectory, "missing.mkv"), context.Dialogs.UnavailableMediaPath);
        Assert.NotEmpty(context.Dialogs.UnavailableMediaReason!);
        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal(asset, Assert.Single(context.Session.Editor.Snapshot.Assets));
        Assert.Equal(asset.Id, context.Session.Editor.Snapshot.Media!.AssetId);
        Assert.False(context.Session.Editor.HasUnsavedChanges);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
        Assert.Equal(VideoPlaybackState.CREATED, context.Session.Controller.Snapshot.State);
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
        Assert.Null(context.Session.Controller.Snapshot.Error);
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.PLAY_PAUSE));
        Assert.True(context.Session.CanExecuteCommand(WorkbenchCommand.OPEN_MEDIA));
        var probes = context.ProbePaths.Count;

        await context.Session.EditAsync(() => context.Session.Editor.AddSubtitle(new(0), new(1), "离线编辑 ABC 123"));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(probes, context.ProbePaths.Count);
        var saved = await ProjectStore.LoadAsync(path);
        Assert.Equal("离线编辑 ABC 123", Assert.Single(saved.Subtitles).Text);
        Assert.Equal(asset.Id, saved.Media!.AssetId);
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
        await context.Session.EditAsync(() => context.Session.Editor.AddSubtitle(new(1), new(2), "After normalized save"));
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(probes, context.ProbePaths.Count);
        Assert.Null(context.Session.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableAndUndecodableMediaCanBeOpenedOffline(bool unreadable)
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "broken.mkv");
        var mediaPath = Path.Combine(context.Session.ProjectDirectory, asset.RelativePath);
        await File.WriteAllTextAsync(mediaPath, "broken fixture");
        context.ProbeFailures[mediaPath] = unreadable
            ? new UnauthorizedAccessException("Cannot read the media.")
            : new InvalidDataException("Cannot decode the media.");
        var path = await SaveProjectAsync(context, "broken-media.aeginext", asset);
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Null(result.Error);
        Assert.Empty(result.Diagnostics);
        Assert.Null(context.Session.LastError);
        Assert.Equal(context.ProbeFailures[mediaPath].Message, context.Dialogs.UnavailableMediaReason);
        Assert.Equal(asset, Assert.Single(context.Session.Editor.Snapshot.Assets));
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
    }

    [Fact]
    public async Task ForeignPlatformPathCanBeKeptOfflineWithoutResolvingItDuringEdits()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var foreignPath = OperatingSystem.IsWindows() ? "/Volumes/Media/video.mkv" : @"D:\Media\video.mkv";
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: foreignPath);
        context.Dialogs.OpenPath = await SaveProjectAsync(context, "foreign.aeginext", asset);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        await context.Session.EditAsync(() => context.Session.Editor.AddSubtitle(new(0), new(1), "Foreign path"));

        Assert.Null(context.Session.LastError);
        Assert.Equal(foreignPath, context.Dialogs.UnavailableMediaPath);
        Assert.Equal(foreignPath, Assert.Single(context.Session.Editor.Snapshot.Assets).ExternalPath);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);
        Assert.Empty(context.ProbePaths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualRelinkingAndUndoRedoPreserveSubtitlesAndTheAcceptedOfflineBinding(bool foreign)
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var foreignPath = OperatingSystem.IsWindows() ? "/Volumes/Media/video.mkv" : @"D:\Media\video.mkv";
        var original = foreign
            ? new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: foreignPath)
            : new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var projectPath = await SaveProjectAsync(context, "offline.aeginext", original);
        context.Dialogs.OpenPath = projectPath;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        await context.Session.EditAsync(() => context.Session.Editor.AddSubtitle(new(0), new(1), "Keep subtitle"));
        var source = Path.Combine(context.Session.ProjectDirectory, "replacement.mkv");
        await File.WriteAllTextAsync(source, "video fixture");
        context.Dialogs.OpenPath = source;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_MEDIA);

        Assert.Null(context.Session.LastError);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.Equal("Keep subtitle", Assert.Single(context.Session.Editor.Snapshot.Subtitles).Text);
        Assert.NotEqual(original.Id, context.Session.Editor.Snapshot.Media!.AssetId);
        Assert.True(context.Session.Editor.HasUnsavedChanges);
        var probes = context.ProbePaths.Count;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.UNDO);
        await context.Session.WaitForProjectIdleAsync();

        Assert.Equal(original, Assert.Single(context.Session.Editor.Snapshot.Assets));
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
        Assert.Null(context.Session.LastError);
        Assert.Equal(probes, context.ProbePaths.Count);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.REDO);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Null(context.Session.LastError);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        var saved = await ProjectStore.LoadAsync(projectPath);
        Assert.Equal("replacement.mkv", Assert.Single(saved.Assets).RelativePath);
        Assert.Null(saved.Assets[0].ExternalPath);
        Assert.Equal("Keep subtitle", Assert.Single(saved.Subtitles).Text);
        context.Dialogs.OpenPath = projectPath;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);
    }

    [Fact]
    public async Task DecliningAnotherProjectRestoresAnAlreadyOfflineProjectWithoutRetryingItsMedia()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var original = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "first-missing.mkv");
        context.Dialogs.OpenPath = await SaveProjectAsync(context, "first.aeginext", original);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        var document = context.Session.Editor.Snapshot;
        var projectPath = context.Session.ProjectPath;
        var recent = context.Session.ApplicationContext.RecentProjects.Entries.ToArray();
        var next = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "second-missing.mkv");
        context.Dialogs.OpenPath = await SaveProjectAsync(context, "second.aeginext", next);
        context.Dialogs.UnavailableMediaChoice = false;
        var probes = context.ProbePaths.Count;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Same(document, context.Session.Editor.Snapshot);
        Assert.Equal(projectPath, context.Session.ProjectPath);
        Assert.Equal(recent, context.Session.ApplicationContext.RecentProjects.Entries);
        Assert.Equal(probes + 1, context.ProbePaths.Count);
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
    }

    [Fact]
    public async Task CancellingTheCallerWaitDoesNotCancelThePendingProjectOpen()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var path = await SaveProjectAsync(context, "cancelled.aeginext", asset);
        context.Dialogs.PendingMediaConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var original = context.Session.Editor.Snapshot;
        var operation = workflow.OpenProjectAsync(path, cancellation.Token);
        await WaitForAsync(() => context.Dialogs.UnavailableMediaRequests == 1 || operation.IsCompleted);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);

        cancellation.Cancel();
        var result = await operation;
        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Null(result.Error);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Null(context.Session.ProjectPath);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
        Assert.True(context.Session.IsProjectBusy);

        context.Dialogs.PendingMediaConfirmation.SetResult(true);
        await context.Session.WaitForProjectIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
        Assert.False(context.Session.IsProjectBusy);
    }

    [Fact]
    public async Task ExplicitTaskCancellationCannotCommitWhenConfirmationArrivesLater()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var path = await SaveProjectAsync(context, "cancelled.aeginext", asset);
        context.Dialogs.PendingMediaConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var original = context.Session.Editor.Snapshot;
        var operation = workflow.OpenProjectAsync(path);
        await WaitForAsync(() => context.Dialogs.UnavailableMediaRequests == 1 || operation.IsCompleted);
        var task = Assert.Single(context.Session.ApplicationContext.Tasks.GetSnapshots(), task =>
            task.Name == "Tasks.OpenProject" && !task.IsFinished);

        Assert.True(context.Session.ApplicationContext.Tasks.RequestCancel(task.Id));
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Dialogs.PendingMediaConfirmation.SetResult(true);

        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Null(context.Session.ProjectPath);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
        Assert.False(context.Session.IsProjectBusy);
    }

    [Fact]
    public async Task MediaCancellationAndBindingMismatchDoNotAskToContinue()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "video.mkv");
        var mediaPath = Path.Combine(context.Session.ProjectDirectory, asset.RelativePath);
        await File.WriteAllTextAsync(mediaPath, "video fixture");
        var path = await SaveProjectAsync(context, "video.aeginext", asset);
        context.ProbeFailures[mediaPath] = new OperationCanceledException();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var cancelled = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.CANCELLED, cancelled.Status);
        Assert.Equal(0, context.Dialogs.UnavailableMediaRequests);
        context.ProbeFailures.Clear();
        var document = await ProjectStore.LoadAsync(path);
        await ProjectStore.SaveAsync(document with { Width = 2 }, path);
        var mismatch = await workflow.OpenProjectAsync(path);
        Assert.Equal(ProjectOpenStatus.FAILED, mismatch.Status);
        Assert.IsType<InvalidDataException>(mismatch.Error);
        Assert.Equal(0, context.Dialogs.UnavailableMediaRequests);
        Assert.Null(context.Session.ProjectPath);
    }

    [Fact]
    public async Task SaveAsRebasesTheOfflineReferenceWithoutRetryingItDuringLaterEdits()
    {
        using var destination = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var originalSource = Path.Combine(context.Session.ProjectDirectory, asset.RelativePath);
        context.Dialogs.OpenPath = await SaveProjectAsync(context, "offline.aeginext", asset);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        context.Dialogs.SavePath = Path.Combine(destination.Path, "rebased.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        var probes = context.ProbePaths.Count;
        await context.Session.EditAsync(() => context.Session.Editor.AddSubtitle(new(0), new(1), "After save as"));
        await context.Session.WaitForProjectIdleAsync();

        Assert.Null(context.Session.LastError);
        Assert.Equal(probes, context.ProbePaths.Count);
        Assert.Equal(destination.Path, context.Session.ProjectDirectory);
        Assert.Equal(originalSource, Assert.Single(context.Session.Editor.Snapshot.Assets).ExternalPath);
        Assert.Equal(asset.Id, context.Session.Editor.Snapshot.Media!.AssetId);
        Assert.Null(context.Session.Controller.Snapshot.FilePath);
    }

    [Fact]
    public async Task DisposalWaitsForUndoToFinishRestoringTheOfflineBinding()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        context.Dialogs.UnavailableMediaChoice = true;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        context.Dialogs.OpenPath = await SaveProjectAsync(context, "offline.aeginext", asset);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        var replacement = Path.Combine(context.Session.ProjectDirectory, "replacement.mkv");
        await File.WriteAllTextAsync(replacement, "video fixture");
        await context.Session.OpenMediaAsync(replacement, true);
        var reachedDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DispatchOverride = async (action, cancellationToken) =>
        {
            reachedDispatch.TrySetResult();
            await releaseDispatch.Task.WaitAsync(cancellationToken);
            action();
        };
        Task? disposal = null;
        try
        {
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.UNDO);
            await reachedDispatch.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = context.Session.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            releaseDispatch.TrySetResult();
            if (disposal is not null)
            {
                await disposal;
            }
        }
        Assert.False(context.Session.IsProjectBusy);
        Assert.Equal(asset.Id, context.Session.Editor.Snapshot.Media!.AssetId);
    }

    private static async Task WaitForAsync(Func<bool> completed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!completed())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static async Task<string> SaveProjectAsync(ProjectMediaPathTestContext context, string fileName, ProjectAsset asset)
    {
        var path = Path.Combine(context.Session.ProjectDirectory, fileName);
        await ProjectStore.SaveAsync(new()
        {
            Width = 1, Height = 1, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        }, path);
        return path;
    }
}
