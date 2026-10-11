using AegiNext.Core.Projects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TrackDeletionWorkflowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OnlyPopulatedTracksRequireConfirmationAndDeletionIsOneUndo(bool populated, bool accepted)
    {
        var otherTrack = new ProjectTrack { Name = "Other" };
        var line = new SubtitleLine { Start = new(0), End = new(2), Text = "Delete" };
        var other = new SubtitleLine { Start = new(0), End = new(2), Text = "Keep" };
        await using var context = new WorkspaceSessionTestContext(new()
        {
            Tracks = [ProjectTrack.Default, otherTrack],
            Subtitles = populated ? [line, other] : [other],
            Layers = populated ? [SubtitleLayer(line), SubtitleLayer(other) with { TrackId = otherTrack.Id }] : [SubtitleLayer(other) with { TrackId = otherTrack.Id }]
        });
        await context.InitializeAsync();
        var session = context.Session;
        context.Dialogs.TrackDeletionChoice = accepted;
        if (populated)
        {
            session.SelectCue(line.Id);
            Assert.True(session.SelectKeyframe(new(line.Id, AnimationProperty.OPACITY, new(1), new(1))));
        }
        var before = context.Editor.Snapshot;
        Assert.True(session.ViewModel.Timeline.DeleteTrackCommand.CanExecute(null));

        await session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);

        Assert.Equal(populated ? 1 : 0, context.Dialogs.TrackDeletionRequests);
        Assert.False(session.IsProjectBusy);
        Assert.Null(session.LastError);
        if (populated)
        {
            Assert.Equal(ProjectTrack.Default.Name, context.Dialogs.DeletedTrackName);
            Assert.Equal(1, context.Dialogs.DeletedTrackSubtitleCount);
        }
        if (populated && !accepted)
        {
            Assert.Same(before, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            Assert.Equal(line.Id, session.SelectedCue?.Id);
            Assert.NotNull(session.SelectedKeyTime);
            return;
        }

        Assert.Equal(otherTrack.Id, Assert.Single(context.Editor.Snapshot.Tracks).Id);
        Assert.Same(other, Assert.Single(context.Editor.Snapshot.Subtitles));
        Assert.Equal(other.Id, Assert.Single(context.Editor.Snapshot.Layers).SubtitleId);
        Assert.Null(session.SelectedCue);
        Assert.Null(session.SelectedLayer);
        Assert.Null(session.SelectedKeyTime);
        Assert.Empty(session.SelectedSubtitleIds);
        Assert.Empty(session.ViewModel.Effects.SelectedIds);
        Assert.Equal(otherTrack.Id, session.CurrentTrackId);
        var after = context.Editor.Snapshot;
        Assert.True(context.Editor.Undo());
        Assert.Same(before, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(after, context.Editor.Snapshot);
    }

    [Fact]
    public async Task LastTrackDeletionClearsSelectionAndCommandsThenAddingATrackRestoresCreation()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;

        await session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);

        Assert.Empty(context.Editor.Snapshot.Tracks);
        Assert.Null(session.CurrentTrackId);
        Assert.Null(session.ViewModel.Subtitles.SelectedTrack);
        Assert.Null(session.ViewModel.Timeline.SelectedTrackId);
        Assert.Empty(session.ViewModel.Subtitles.VisibleRows);
        Assert.False(session.ViewModel.Timeline.DeleteTrackCommand.CanExecute(null));
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.ADD_SUBTITLE));
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.TIMING_ENTER));
        Assert.True(session.CanExecuteCommand(WorkbenchCommand.IMPORT_SUBTITLES));
        Assert.True(session.CanExecuteCommand(WorkbenchCommand.IMPORT_ASS));
        var empty = context.Editor.Snapshot;
        await session.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        await session.AddCueAsync();
        await session.SetCueStartAsync();
        Assert.Same(empty, context.Editor.Snapshot);
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        Assert.True(context.Editor.Undo());
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, session.CurrentTrackId);
        Assert.True(context.Editor.Redo());
        Assert.Null(session.CurrentTrackId);

        await session.ViewModel.Timeline.AddTrackCommand.ExecuteAsync(null);
        var track = Assert.Single(context.Editor.Snapshot.Tracks);
        Assert.Equal(track.Id, session.CurrentTrackId);
        Assert.True(session.CanExecuteCommand(WorkbenchCommand.ADD_SUBTITLE));
        await session.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        Assert.Equal(track.Id, Assert.Single(context.Editor.Snapshot.Layers).TrackId);
        Assert.Null(session.LastError);
    }

    [Fact]
    public async Task ChangedSnapshotInvalidatesPendingDeletionWithoutDeletingAnyTrack()
    {
        var line = new SubtitleLine { Start = new(0), End = new(2), Text = "Keep" };
        await using var context = new WorkspaceSessionTestContext(new()
        {
            Subtitles = [line], Layers = [SubtitleLayer(line)]
        });
        await context.InitializeAsync();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.PendingTrackDeletion = decision;
        var deletion = context.Session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(1, context.Dialogs.TrackDeletionRequests);
            Assert.False(deletion.IsCompleted);
            Assert.True(context.Session.IsProjectBusy);
            context.Editor.Apply("Changed project", document => document with { Name = "Changed" });
            var changed = context.Editor.Snapshot;
            decision.SetResult(true);
            await deletion;
            Assert.Same(changed, context.Editor.Snapshot);
            Assert.Single(context.Editor.Snapshot.Tracks);
            Assert.Single(context.Editor.Snapshot.Subtitles);
            Assert.False(context.Session.IsProjectBusy);
            Assert.Null(context.Session.LastError);
        }
        finally
        {
            decision.TrySetResult(false);
            await deletion;
        }
    }

    [Fact]
    public async Task CancelledDeletionPreservesUncommittedSubtitleDraftAndHistory()
    {
        var line = new SubtitleLine { Start = new(0), End = new(2), Text = "Original" };
        await using var context = new WorkspaceSessionTestContext(new()
        {
            Subtitles = [line], Layers = [SubtitleLayer(line)]
        });
        await context.InitializeAsync();
        var row = Assert.Single(context.Session.ViewModel.Subtitles.Rows);
        row.Text = "Uncommitted draft";
        var before = context.Editor.Snapshot;

        await context.Session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Dialogs.TrackDeletionRequests);
        Assert.Same(before, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(row.IsDirty);
        Assert.Equal("Uncommitted draft", row.Text);
        Assert.Null(context.Session.LastError);
    }

    [Fact]
    public async Task DeletingActiveTimingTrackInvalidatesF9WithoutAccessingDeletedCue()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        await context.Session.SetCueStartAsync();
        Assert.True(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        context.Dialogs.TrackDeletionChoice = true;

        await context.Session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);

        Assert.Empty(context.Editor.Snapshot.Tracks);
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        var before = context.Editor.Snapshot;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.TIMING_EXIT);
        Assert.Same(before, context.Editor.Snapshot);
        Assert.Null(context.Session.LastError);
    }

    [Fact]
    public async Task DeletingTheLastMixedTrackRemovesAllClipsAndRejectsEveryClipboardKind()
    {
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(2) };
        await using var context = new WorkspaceSessionTestContext(new() { Layers = [shape] });
        await context.InitializeAsync();
        var session = context.Session;
        var cue = context.Editor.AddSubtitle(new(3), new(5), "Copied subtitle");
        session.SelectLayer(shape.Id, [shape.Id]);
        await session.CopyTimelineClipsAsync(shape.Id, [shape.Id]);
        context.Dialogs.TrackDeletionChoice = true;
        await session.ViewModel.Timeline.DeleteTrackCommand.ExecuteAsync(null);

        Assert.Equal(2, context.Dialogs.DeletedTrackSubtitleCount);
        Assert.Empty(context.Editor.Snapshot.Tracks);
        Assert.Empty(context.Editor.Snapshot.Layers);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
        Assert.False(session.CanCopyTimelineClips);
        Assert.False(session.CanPasteTimelineClips);
        var before = context.Editor.Snapshot;
        await session.PasteTimelineClipsAsync(new(6));
        Assert.Same(before, context.Editor.Snapshot);
        Assert.Null(session.LastError);
        Assert.True(context.Editor.Undo());
        Assert.Equal(cue, context.Editor.Snapshot.Subtitles[0].Id);
        Assert.True(session.CanPasteTimelineClips);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportIntoZeroTrackProjectCreatesOnlyTheImportedTrack(bool ass)
    {
        await using var context = new WorkspaceSessionTestContext(new() { Tracks = [] });
        await context.InitializeAsync();
        context.Session.SetProjectLocation(null, context.DirectoryPath);
        context.Dialogs.OpenPath = Path.Combine(context.DirectoryPath, ass ? "import.ass" : "import.srt");
        context.Dialogs.ConversionChoice = true;
        await File.WriteAllTextAsync(context.Dialogs.OpenPath, ass
            ? "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,Imported\n"
            : "1\n00:00:00,000 --> 00:00:02,000\nImported\n");
        var before = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(ass ? WorkbenchCommand.IMPORT_ASS : WorkbenchCommand.IMPORT_SUBTITLES);

        Assert.Null(context.Session.LastError);
        var track = Assert.Single(context.Editor.Snapshot.Tracks);
        Assert.Equal("import", track.Name);
        Assert.Equal(track.Id, Assert.Single(context.Editor.Snapshot.Layers).TrackId);
        Assert.Equal(track.Id, context.Session.CurrentTrackId);
        Assert.True(context.Editor.Undo());
        Assert.Same(before, context.Editor.Snapshot);
        Assert.Null(context.Session.CurrentTrackId);
    }

    [Fact]
    public async Task SrtImportIntoZeroTracksUsesSelectedPresetWithoutAnExtraEmptyTrack()
    {
        await using var context = new WorkspaceSessionTestContext(new() { Tracks = [] });
        await context.InitializeAsync();
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Selected", new() { FontSize = 77, Bold = true });
        await context.Session.Styles.UpsertAsync(preset);
        Assert.Equal(preset.Id, context.Session.ViewModel.Styles.Subtitle.SelectedPreset?.Id);
        context.Session.SetProjectLocation(null, context.DirectoryPath);
        context.Dialogs.OpenPath = Path.Combine(context.DirectoryPath, "styled.srt");
        await File.WriteAllTextAsync(context.Dialogs.OpenPath, "1\n00:00:00,000 --> 00:00:02,000\nStyled\n");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);

        Assert.Null(context.Session.LastError);
        Assert.Single(context.Editor.Snapshot.Tracks);
        Assert.Equal(preset.Style, Assert.Single(context.Editor.Snapshot.Subtitles).Style);
    }

    private static ProjectLayer SubtitleLayer(SubtitleLine line)
    {
        return new()
        {
            Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
        };
    }
}
