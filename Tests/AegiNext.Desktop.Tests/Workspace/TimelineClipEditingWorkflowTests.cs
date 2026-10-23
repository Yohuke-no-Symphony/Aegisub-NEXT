using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineClipEditingWorkflowTests
{
    [Fact]
    public async Task CopyPasteAndDeleteUseWholeSelectionAndIndependentTransactions()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[1], ids);

        await session.CopyTimelineClipsAsync(ids[1], ids);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        await session.PasteTimelineClipsAsync(new(10));
        var pasted = context.Editor.Snapshot;
        Assert.Equal(4, pasted.Layers.Length);
        Assert.Equal(new MediaTime(10), pasted.Subtitles[2].Start);
        Assert.Equal(new MediaTime(13), pasted.Subtitles[3].Start);
        Assert.Equal(pasted.Layers.Skip(2).Select(layer => layer.Id).Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(pasted.Layers[3].Id, session.SelectedLayerId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        session.SelectLayer(ids[1], ids);
        await session.DeleteTimelineClipsAsync(ids);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
        Assert.Empty(context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidDraftAndStaleMenuCannotEditOrReplaceClipboard()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        var row = session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";
        await session.CopyTimelineClipsAsync(ids[0], ids);
        Assert.False(session.CanPasteTimelineClips);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("invalid", row.StartText);
        row.Accept(document.Subtitles[0]);

        context.Editor.ShiftClips(ids, new(1));
        var changed = context.Editor.Snapshot;
        await session.DeleteTimelineClipsAsync(ids, document);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(6, 6)]
    public async Task ContextCreationUsesTwoSecondsOnTheCapturedTrackInOneTransaction(int requestedStart, int expectedStart)
    {
        var track = new ProjectTrack { Name = "Destination" };
        var document = new ProjectDocument { Tracks = [ProjectTrack.Default, track] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        await context.Session.CreateTimelineSubtitleAsync(track.Id, new(requestedStart), document);

        Assert.Null(context.Session.LastError);
        var created = context.Editor.Snapshot;
        var cue = Assert.Single(created.Subtitles);
        Assert.Equal(new MediaTime(expectedStart), cue.Start);
        Assert.Equal(new MediaTime(expectedStart + 2), cue.End);
        Assert.Equal(string.Empty, cue.Text);
        var layer = Assert.Single(created.Layers);
        Assert.Equal(track.Id, layer.TrackId);
        Assert.Equal(cue.Id, layer.SubtitleId);
        Assert.Equal((cue.Start, cue.End), (layer.Start, layer.End));
        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        Assert.Equal(track.Id, context.Session.CurrentTrackId);
        Assert.Null(context.Session.ViewModel.Timeline.TimingPreview);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(created, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextCreationFitsTheGapBeforeTheNextSubtitleOrGraphicOnItsTrack(bool graphic)
    {
        var next = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Start = new(7), End = new(9),
            Shape = new(ShapeKind.RECTANGLE, 40, 20)
        };
        var otherTrack = new ProjectTrack { Name = "Other" };
        var other = new SubtitleLine { Start = new(6), End = new(8), Text = "Other track" };
        var nextCue = new SubtitleLine { Start = next.Start, End = next.End, Text = "Next" };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, otherTrack],
            Subtitles = graphic ? [other] : [other, nextCue],
            Layers = [Layer(other) with { TrackId = otherTrack.Id }, graphic ? next : Layer(nextCue)]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        await context.Session.CreateTimelineSubtitleAsync(ProjectTrack.DEFAULT_TRACK_ID, new(6), document);

        Assert.Null(context.Session.LastError);
        var cue = Assert.Single(context.Editor.Snapshot.Subtitles.Where(line =>
            line.Id != other.Id && line.Id != nextCue.Id));
        Assert.Equal(new MediaTime(6), cue.Start);
        Assert.Equal(new MediaTime(7), cue.End);
        Assert.Null(context.Session.ViewModel.Timeline.TimingPreview);
        Assert.All(document.Layers, layer => Assert.Same(layer, context.Editor.Snapshot.Layers.Single(value => value.Id == layer.Id)));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ContextCreationInsideAnExistingClipPreservesTheDocumentAndHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        await context.Session.CreateTimelineSubtitleAsync(ProjectTrack.DEFAULT_TRACK_ID, new(2), document);

        var error = Assert.IsType<InvalidOperationException>(context.Session.LastError);
        Assert.Equal(Localization.Get("Workbench.TimelineClipCollision"), error.Message);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Null(context.Session.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ContextCreationRejectsInvalidDraftsAndStaleMenuTargets()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = context.Session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";

        await context.Session.CreateTimelineSubtitleAsync(ProjectTrack.DEFAULT_TRACK_ID, new(10), document);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("invalid", row.StartText);
        Assert.Null(context.Session.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Editor.CanUndo);
        row.Accept(document.Subtitles[0]);
        context.Editor.ShiftClips([document.Layers[0].Id], new(1));
        var changed = context.Editor.Snapshot;

        await context.Session.CreateTimelineSubtitleAsync(ProjectTrack.DEFAULT_TRACK_ID, new(10), document);

        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Null(context.Session.ViewModel.Timeline.TimingPreview);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task PasteCollisionIsLocalizedAndPreservesSnapshotSelectionAndHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);

        await session.PasteTimelineClipsAsync(new(1));

        var error = Assert.IsType<InvalidOperationException>(session.LastError);
        Assert.Equal(Localization.Get("Workbench.TimelinePasteFailed"), error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(ids.Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task SaveAndSaveAsRetainClipboardButReopeningTheSameProjectClearsIt()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "saved.aeginext");

        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        Assert.Null(session.LastError);
        Assert.True(session.CanPasteTimelineClips);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "saved-as.aeginext");
        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Null(session.LastError);
        Assert.True(session.CanPasteTimelineClips);

        var reopened = await session.OpenProjectAsync(context.Dialogs.SavePath);

        Assert.Equal(ProjectOpenStatus.OPENED, reopened.Status);
        Assert.Equal(document.Id, session.DocumentSnapshot.Id);
        Assert.False(session.CanPasteTimelineClips);
        await session.PasteTimelineClipsAsync(new(10));
        Assert.Equal(2, context.Editor.Snapshot.Layers.Length);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewProjectAndSessionDisposalReleaseTheCapturedClipboard(bool dispose)
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);
        Assert.True(session.CanPasteTimelineClips);

        if (dispose)
        {
            await session.DisposeAsync();
        }
        else
        {
            var created = await session.CreateProjectAsync(Path.Combine(context.DirectoryPath, "created.aeginext"));
            Assert.Equal(ProjectOpenStatus.OPENED, created.Status);
            context.Editor.Reset(document);
        }

        Assert.False(session.CanPasteTimelineClips);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Second" };
        return new()
        {
            Subtitles = [first, second],
            Layers = [Layer(first), Layer(second)]
        };
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };
}
