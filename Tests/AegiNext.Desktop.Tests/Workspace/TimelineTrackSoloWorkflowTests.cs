using AegiNext.Application;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineTrackSoloWorkflowTests
{
    [Fact]
    public async Task SoloOfANonCurrentTrackPreservesSelectionInvalidDraftProjectHistoryAndPersistence()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "7e-";
        var selectedCue = context.Session.SelectedCueId;
        var selectedLayer = context.Session.SelectedLayerId;
        var selectedTrack = context.Session.CurrentTrackId;
        var before = context.Editor.Snapshot;
        var persisted = ProjectStore.Serialize(context.Session.CreatePersistenceSnapshot(before));
        var dirty = context.Session.HasUnsavedChanges;
        var undo = context.Editor.UndoLabel;
        var redo = context.Editor.RedoLabel;
        var solo = document.Tracks[1].Id;

        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);
        context.Session.RefreshDocument();

        Assert.Equal(solo, context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.Equal(selectedCue, context.Session.SelectedCueId);
        Assert.Equal(selectedLayer, context.Session.SelectedLayerId);
        Assert.Equal(selectedTrack, context.Session.CurrentTrackId);
        Assert.Equal("7e-", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
        Assert.Same(before, context.Editor.Snapshot);
        Assert.Equal(persisted, ProjectStore.Serialize(context.Session.CreatePersistenceSnapshot(context.Editor.Snapshot)));
        Assert.Equal(dirty, context.Session.HasUnsavedChanges);
        Assert.Equal(undo, context.Editor.UndoLabel);
        Assert.Equal(redo, context.Editor.RedoLabel);
        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);
        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.Equal("7e-", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
    }

    [Fact]
    public async Task SuccessfulSelectionOfTheCurrentTrackCancelsSoloOfAnotherTrack()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var current = document.Tracks[0].Id;
        var solo = document.Tracks[1].Id;
        Assert.True(context.Session.SelectTrack(current));
        var before = context.Editor.Snapshot;
        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);

        Assert.True(context.Session.SelectTrack(current));

        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.Equal(current, context.Session.CurrentTrackId);
        Assert.Same(before, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task RejectedTrackSelectionKeepsSoloAndTheInvalidDraft()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "7e-";
        var solo = document.Tracks[1].Id;
        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);

        Assert.False(context.Session.SelectTrack(document.Tracks[0].Id));

        Assert.Equal(solo, context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.Equal("7e-", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task SelectingAClipFromAnotherTrackCancelsSoloAfterSelectionSucceeds()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        context.Session.ViewModel.Timeline.ToggleTrackSolo(document.Tracks[0].Id);

        context.Session.SelectCue(document.Subtitles[1].Id);

        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DeletingTheSoloTrackAndUndoingDoesNotReinstateSoloAndZeroTracksStayValid()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var solo = document.Tracks[1].Id;
        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);

        context.Editor.RemoveTrack(solo);

        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        context.Editor.RemoveTrack(solo);
        context.Session.ViewModel.Timeline.ToggleTrackSolo(document.Tracks[0].Id);
        context.Editor.RemoveTrack(document.Tracks[0].Id);
        Assert.Empty(context.Editor.Snapshot.Tracks);
        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
        context.Session.ViewModel.Timeline.ToggleTrackSolo(solo);
        Assert.Null(context.Session.ViewModel.Timeline.SoloTrackId);
    }

    [Fact]
    public async Task ANewProjectIdentityAndExplicitProjectResetClearSolo()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        model.ToggleTrackSolo(document.Tracks[0].Id);

        model.Document = document with { Id = Guid.NewGuid() };

        Assert.Null(model.SoloTrackId);
        model.ToggleTrackSolo(document.Tracks[0].Id);
        context.Session.ResetSelection();
        Assert.Null(model.SoloTrackId);
        Assert.Same(document, context.Editor.Snapshot);
    }

    private static ProjectDocument CreateDocument()
    {
        var editor = new ProjectEditor();
        var secondTrack = editor.AddTrack("Second");
        editor.AddSubtitle(new(0), new(2), "First");
        editor.AddSubtitle(new(3), new(5), "Second", secondTrack);
        return editor.Snapshot;
    }
}
