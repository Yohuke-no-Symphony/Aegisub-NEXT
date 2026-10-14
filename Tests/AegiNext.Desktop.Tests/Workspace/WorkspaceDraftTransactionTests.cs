using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkspaceDraftTransactionTests
{
    [Fact]
    public async Task UnboundDetailsInputsDoNotCreateProjectDrafts()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Session.Details.StyleDraft.FontSizeText = "unbound control input";

        Assert.True(context.Session.Details.StyleDraft.IsDirty);
        Assert.Null(context.Session.Details.Line);
        Assert.False(context.Session.Details.HasDrafts);
        Assert.False(context.Session.HasProjectDrafts);
        Assert.False(context.Session.HasUnsavedChanges);
    }

    [Fact]
    public async Task InvalidSecondSubtitleKeepsAllDraftsAndLeavesDocumentAndHistoryUnchanged()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument());
        await context.InitializeAsync();
        var session = context.Session;
        var original = context.Editor.Snapshot;
        var rows = session.ViewModel.Subtitles.Rows;
        rows[0].Text = "Valid pending edit";
        rows[1].StartText = "invalid time";
        var focusRequests = 0;
        session.ViewModel.DraftErrorFocusRequested += (_, _) => focusRequests++;

        Assert.False(session.ViewModel.TryCommitDrafts(), $"Updating={session.IsUpdating}; Busy={session.IsProjectBusy}; Drafts={session.HasProjectDrafts}");

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Null(context.Editor.UndoLabel);
        Assert.Equal("Valid pending edit", rows[0].Text);
        Assert.Equal("invalid time", rows[1].StartText);
        Assert.All(rows, row => Assert.True(row.IsDirty));
        Assert.Equal(rows[1].Id, session.ViewModel.Subtitles.InvalidRowId);
        Assert.Equal("subtitles", session.ViewModel.InvalidPanelId);
        Assert.Equal("StartText", session.ViewModel.InvalidFieldKey);
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public async Task ValidSubtitleDraftsCommitInOneTransactionAndOneUndoRestoresBothAndLayerTiming()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var rows = context.Session.ViewModel.Subtitles.Rows;
        rows[0].Text = "First edited";
        rows[0].StartText = "00:00:00.250";
        rows[1].Text = "Second edited";
        rows[1].EndText = "00:00:05.500";

        Assert.True(context.Session.ViewModel.TryCommitDrafts());

        var changed = context.Editor.Snapshot;
        Assert.Equal("First edited", changed.Subtitles[0].Text);
        Assert.Equal(new MediaTime(1, 4), changed.Subtitles[0].Start);
        Assert.Equal("Second edited", changed.Subtitles[1].Text);
        Assert.Equal(new MediaTime(11, 2), changed.Subtitles[1].End);
        Assert.Equal(changed.Subtitles[0].Start, changed.Layers[0].Start);
        Assert.Equal(changed.Subtitles[1].End, changed.Layers[1].End);
        Assert.Equal("Commit workspace drafts", context.Editor.UndoLabel);
        Assert.All(context.Session.ViewModel.Subtitles.Rows, row => Assert.False(row.IsDirty));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidStyleOrTimingPreventsAnyPartialSubtitleStyleAndLayerUpdate(bool invalidStyle)
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var model = context.Session.ViewModel;
        context.Session.SelectCue(original.Subtitles[0].Id);
        model.Subtitles.Rows[0].Text = "Pending text";
        model.Styles.Subtitle.FontSize = invalidStyle ? null : 72;
        model.Styles.Subtitle.FontSizeText = invalidStyle ? "invalid font size" : "72";
        model.Effects.PositionX = 640;
        model.Effects.PositionXText = "640";
        model.Timeline.LayerEnd = invalidStyle ? "00:00:02.000" : "invalid end";

        Assert.False(model.TryCommitDrafts());

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("Pending text", model.Subtitles.Rows[0].Text);
        Assert.Equal(640, model.Effects.PositionX);
        Assert.Equal(invalidStyle ? "styles" : "timeline", model.InvalidPanelId);
        Assert.NotNull(model.Error);

        model.Styles.Subtitle.FontSize = 72;
        model.Timeline.LayerEnd = "00:00:02.000";
        Assert.True(model.TryCommitDrafts());
        Assert.Equal("Pending text", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(72, context.Editor.Snapshot.Subtitles[0].Style.FontSize);
        Assert.Equal(640, model.Effects.PositionX);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    internal static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Start = MediaTime.Zero, End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(5), Text = "Second" };
        return new()
        {
            Name = "Workspace test",
            Subtitles = [first, second],
            Layers =
            [
                new()
                {
                    Name = "First subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start,
                    End = first.End
                },
                new()
                {
                    Name = "Second subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start,
                    End = second.End
                }
            ]
        };
    }
}
