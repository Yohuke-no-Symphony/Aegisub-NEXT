using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineViewStatePersistenceTests
{
    [Theory]
    [InlineData(LayerKind.SUBTITLE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task InitializationRestoresExpandedClipsWithoutChangingTheEditorSnapshotOrHistory(LayerKind kind)
    {
        var document = CreateDocument();
        var row = GetClipId(document, kind);
        document = document with { TimelineViewState = new() { ExpandedAnimationClips = [row] } };
        await using var context = new WorkspaceSessionTestContext(document);

        await context.InitializeAsync();

        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Equal(row, Assert.Single(context.Session.ViewModel.Timeline.TimelineViewState.ExpandedAnimationClips));
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Theory]
    [InlineData(LayerKind.SUBTITLE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task CollapseAndExpandPreserveTheContentSnapshotAndReturningToTheSavePointClearsViewDirty(LayerKind kind)
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = GetClipId(document, kind);

        context.Session.SetTimelineEffectClipCollapsed(row, false);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Equal(row, Assert.Single(context.Session.ViewModel.Timeline.TimelineViewState.ExpandedAnimationClips));
        Assert.Empty(context.Editor.Snapshot.TimelineViewState.ExpandedAnimationClips);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);

        context.Session.SetTimelineEffectClipCollapsed(row, false);

        Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Same(document, context.Editor.Snapshot);

        context.Session.SetTimelineEffectClipCollapsed(row, true);

        Assert.Empty(context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Empty(context.Session.ViewModel.Timeline.TimelineViewState.ExpandedAnimationClips);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ViewChangesPreserveRedoAndContentUndoDoesNotUndoTheExpandedClips()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = GetClipId(document, LayerKind.SUBTITLE);
        context.Editor.Apply("Resize project", value => value with { Width = 1280 });
        await context.Session.WaitForProjectIdleAsync();
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        var redoLabel = context.Editor.RedoLabel;

        context.Session.SetTimelineEffectClipCollapsed(row, false);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
        Assert.Equal(redoLabel, context.Editor.RedoLabel);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Editor.Redo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(1280, context.Editor.Snapshot.Width);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.True(context.Session.HasUnsavedChanges);

        context.Session.SetTimelineEffectClipCollapsed(row, true);

        Assert.False(context.Session.HasUnsavedChanges);
        Assert.True(context.Editor.CanRedo);
        Assert.Equal(redoLabel, context.Editor.RedoLabel);
    }

    [Fact]
    public async Task ManualSaveAndSaveAsPersistViewStateAndReopeningRestoresEachProjectsOwnRows()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var originalSnapshot = context.Editor.Snapshot;
        var subtitleRow = GetClipId(originalSnapshot, LayerKind.SUBTITLE);
        var sceneRow = GetClipId(originalSnapshot, LayerKind.SHAPE);
        context.Session.SetTimelineEffectClipCollapsed(subtitleRow, false);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(originalSnapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(subtitleRow, Assert.Single((await ProjectStore.LoadAsync(originalPath)).TimelineViewState.ExpandedAnimationClips));

        context.Session.SetTimelineEffectClipCollapsed(sceneRow, false);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "relocated", "saved-as.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.Null(context.Session.LastError);
        Assert.Equal(context.Dialogs.SavePath, context.Session.ProjectPath);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), (await ProjectStore.LoadAsync(context.Dialogs.SavePath)).TimelineViewState.ExpandedAnimationClips);

        var reopenedOriginal = await context.Session.OpenProjectAsync(originalPath);

        Assert.Equal(ProjectOpenStatus.OPENED, reopenedOriginal.Status);
        Assert.Equal(subtitleRow, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.False(context.Session.HasUnsavedChanges);
        var reopenedCopy = await context.Session.OpenProjectAsync(context.Dialogs.SavePath);
        Assert.Equal(ProjectOpenStatus.OPENED, reopenedCopy.Status);
        Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), context.Session.ViewModel.Timeline.TimelineViewState.ExpandedAnimationClips);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ReturningToANonemptySavedViewStateClearsDirtyWithoutAContentEdit()
    {
        var document = CreateDocument();
        var row = GetClipId(document, LayerKind.SHAPE);
        document = document with { TimelineViewState = new() { ExpandedAnimationClips = [row] } };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        context.Session.SetTimelineEffectClipCollapsed(row, true);

        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(document, context.Editor.Snapshot);

        context.Session.SetTimelineEffectClipCollapsed(row, false);

        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task AutoSaveWritesViewOnlyChangesAndKeepsTheOriginalContentSnapshot()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetClipId(snapshot, LayerKind.SUBTITLE);
        context.Session.SetTimelineEffectClipCollapsed(row, false);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Session.HasUnsavedChanges);

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var saved = Assert.Single(storage.SavedSnapshots);
        Assert.Equal(row, Assert.Single(saved.TimelineViewState.ExpandedAnimationClips));
        Assert.Equal(snapshot.Layers, saved.Layers);
        Assert.Equal(snapshot.Subtitles, saved.Subtitles);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(storage.SavedSnapshots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingViewDuringAutoSaveKeepsTheLaterStateDirtyAndSavesItOnTheNextTick(bool returnToPreviousSavePoint)
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var subtitleRow = GetClipId(snapshot, LayerKind.SUBTITLE);
        var sceneRow = GetClipId(snapshot, LayerKind.SHAPE);
        context.Session.SetTimelineEffectClipCollapsed(subtitleRow, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (returnToPreviousSavePoint)
            {
                context.Session.SetTimelineEffectClipCollapsed(subtitleRow, true);
            }
            else
            {
                context.Session.SetTimelineEffectClipCollapsed(sceneRow, false);
            }

            release.TrySetResult();
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(subtitleRow, Assert.Single(Assert.Single(storage.SavedSnapshots).TimelineViewState.ExpandedAnimationClips));
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.False(context.Editor.HasUnsavedChanges);
            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            storage.SaveWork = null;
            clock.Advance(TimeSpan.FromMinutes(2));
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, storage.SavedSnapshots.Count);
            var expectedRows = returnToPreviousSavePoint ? Array.Empty<TimelineEffectClipId>() : new[] { subtitleRow, sceneRow };
            Assert.Equal(expectedRows.OrderBy(id => id.LayerId), storage.SavedSnapshots[1].TimelineViewState.ExpandedAnimationClips);
            Assert.Equal(expectedRows.OrderBy(id => id.LayerId), context.Session.TimelineViewState.ExpandedAnimationClips);
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.Same(snapshot, context.Editor.Snapshot);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedAutoSaveKeepsViewDirtyAndTheNextTickRetriesTheSameExpandedClips()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetClipId(snapshot, LayerKind.SHAPE);
        context.Session.SetTimelineEffectClipCollapsed(row, false);
        var failure = new IOException("Timeline view state save failed");
        storage.SaveWork = _ => Task.FromException(failure);

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(failure, context.Session.LastError);
        Assert.Equal(1, storage.SaveAttempts);
        Assert.Empty(storage.SavedSnapshots);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        storage.SaveWork = null;

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, storage.SaveAttempts);
        Assert.Equal(row, Assert.Single(Assert.Single(storage.SavedSnapshots).TimelineViewState.ExpandedAnimationClips));
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
    }

    [Fact]
    public async Task FailedManualSaveAsPreservesTheActiveProjectAndUnsavedExpandedClips()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetClipId(snapshot, LayerKind.SUBTITLE);
        context.Session.SetTimelineEffectClipCollapsed(row, false);
        var destination = Path.Combine(context.DirectoryPath, "directory.aeginext");
        Directory.CreateDirectory(destination);
        context.Dialogs.SavePath = destination;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.NotNull(context.Session.LastError);
        Assert.Equal(originalPath, context.Session.ProjectPath);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Empty((await ProjectStore.LoadAsync(originalPath)).TimelineViewState.ExpandedAnimationClips);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DecliningDiscardOfViewOnlyChangesKeepsTheActiveProjectAndExpandedClips()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetClipId(snapshot, LayerKind.SUBTITLE);
        context.Session.SetTimelineEffectClipCollapsed(row, false);
        var nextPath = Path.Combine(context.DirectoryPath, "next.aeginext");
        await ProjectStore.SaveAsync(CreateDocument(), nextPath);
        context.Dialogs.UnsavedChoice = 0;

        var result = await context.Session.OpenProjectAsync(nextPath);

        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.Equal(originalPath, context.Session.ProjectPath);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task ViewOnlyDirtyMarksTheTitleAndCloseCancellationThenSaveKeepsTheCorrectPersistenceState()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        var path = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var cleanTitle = context.Session.ViewModel.Title;
        var row = GetClipId(snapshot, LayerKind.SUBTITLE);
        context.Session.SetTimelineEffectClipCollapsed(row, false);

        Assert.Equal(cleanTitle + " •", context.Session.ViewModel.Title);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        context.Dialogs.UnsavedChoice = 0;

        Assert.False(await context.Session.RequestCloseAsync());

        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.False(context.Session.IsClosing);
        Assert.Equal(2, clock.ActiveTimerCount);
        Assert.Equal(cleanTitle + " •", context.Session.ViewModel.Title);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Empty((await ProjectStore.LoadAsync(path)).TimelineViewState.ExpandedAnimationClips);
        context.Dialogs.UnsavedChoice = 1;
        var beforeDisposeCalled = false;

        var closed = await context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalled = true;
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.Equal(cleanTitle, context.Session.ViewModel.Title);
            return Task.CompletedTask;
        });

        Assert.True(closed);
        Assert.True(beforeDisposeCalled);
        Assert.Equal(2, context.Dialogs.ConfirmationRequests);
        Assert.True(context.Session.IsClosing);
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Equal(cleanTitle, context.Session.ViewModel.Title);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single((await ProjectStore.LoadAsync(path)).TimelineViewState.ExpandedAnimationClips));
        await context.Session.DisposeAsync();
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task OlderAutoSaveKeepsNewContentAndViewDirtyAndContentUndoReturnsToItsSavePointWithoutChangingView()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var subtitleRow = GetClipId(snapshot, LayerKind.SUBTITLE);
        var sceneRow = GetClipId(snapshot, LayerKind.SHAPE);
        context.Session.SetTimelineEffectClipCollapsed(subtitleRow, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Editor.Apply("Resize during timeline state save", document => document with { Width = 1280 });
            var newerSnapshot = context.Editor.Snapshot;
            context.Session.SetTimelineEffectClipCollapsed(sceneRow, false);
            release.TrySetResult();
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            var saved = Assert.Single(storage.SavedSnapshots);
            Assert.Equal(snapshot.Width, saved.Width);
            Assert.Equal(subtitleRow, Assert.Single(saved.TimelineViewState.ExpandedAnimationClips));
            Assert.Same(newerSnapshot, context.Editor.Snapshot);
            Assert.True(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), context.Session.TimelineViewState.ExpandedAnimationClips);
            Assert.Equal("Resize during timeline state save", context.Editor.UndoLabel);
            Assert.True(context.Editor.Undo());
            await context.Session.WaitForProjectIdleAsync();

            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), context.Session.TimelineViewState.ExpandedAnimationClips);
            Assert.True(context.Editor.CanRedo);
            storage.SaveWork = null;
            clock.Advance(TimeSpan.FromMinutes(2));
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, storage.SavedSnapshots.Count);
            Assert.Equal(snapshot.Width, storage.SavedSnapshots[1].Width);
            Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), storage.SavedSnapshots[1].TimelineViewState.ExpandedAnimationClips);
            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.True(context.Editor.Redo());
            await context.Session.WaitForProjectIdleAsync();
            Assert.Same(newerSnapshot, context.Editor.Snapshot);
            Assert.True(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }.OrderBy(row => row.LayerId), context.Session.TimelineViewState.ExpandedAnimationClips);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static ProjectDocument CreateDocument()
    {
        var cue = new SubtitleLine { Start = new(1), End = new(5), Text = "Timeline view state" };
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        return new()
        {
            Tracks = [ProjectTrack.Default, shapeTrack],
            Subtitles = [cue],
            Layers =
            [
                new()
                {
                    Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25)])]
                },
                new()
                {
                    TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(5),
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.75)])]
                }
            ]
        };
    }

    private static TimelineEffectClipId GetClipId(ProjectDocument document, LayerKind kind)
    {
        var clip = Assert.Single(document.Layers, layer => layer.Kind == kind);
        return new(clip.Id, new(AnimationProperty.OPACITY));
    }

    private static async Task<string> OpenFixtureAsync(WorkspaceSessionTestContext context, ProjectDocument document)
    {
        var path = Path.Combine(context.DirectoryPath, "timeline.aeginext");
        await ProjectStore.SaveAsync(document, path);
        var result = await context.Session.OpenProjectAsync(path);
        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        await context.Session.WaitForProjectIdleAsync();
        Assert.False(context.Session.HasUnsavedChanges);
        return path;
    }
}
