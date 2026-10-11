using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleColorTagWorkflowTests
{
    [Fact]
    public async Task DisplayNumbersFollowVisibleTimeOrderAfterFilteringEditingAndUndo()
    {
        var source = CreateDocument();
        var document = source with { Subtitles = [source.Subtitles[2], source.Subtitles[0], source.Subtitles[1]] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var subtitles = session.ViewModel.Subtitles;
        Assert.Equal(source.Subtitles.Select(line => line.Id), subtitles.VisibleRows.Select(row => row.Id));
        Assert.Equal([1, 2, 3], subtitles.VisibleRows.Select(row => row.DisplayNumber));
        var last = subtitles.VisibleRows[^1];
        var notifications = 0;
        last.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SubtitleRow.DisplayNumber))
            {
                notifications++;
            }
        };

        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, source.ColorTags[0].Id)));
        Assert.Equal([source.Subtitles[0].Id, source.Subtitles[2].Id], subtitles.VisibleRows.Select(row => row.Id));
        Assert.Equal([1, 2], subtitles.VisibleRows.Select(row => row.DisplayNumber));
        Assert.Same(last, subtitles.VisibleRows[^1]);
        Assert.Equal(1, notifications);
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);

        Assert.True(session.TrySelectSubtitleColorTagFilter(SubtitleColorTagFilter.All));
        last.StartText = "00:00:02.000";
        last.EndText = "00:00:03.000";
        Assert.True(session.TryCommitDrafts(), subtitles.ValidationError ?? session.LastError?.ToString());
        Assert.Equal([source.Subtitles[0].Id, last.Id, source.Subtitles[1].Id], subtitles.VisibleRows.Select(row => row.Id));
        Assert.Equal([1, 2, 3], subtitles.VisibleRows.Select(row => row.DisplayNumber));
        Assert.True(context.Editor.Undo());
        Assert.Equal(source.Subtitles.Select(line => line.Id), subtitles.VisibleRows.Select(row => row.Id));
        Assert.Equal([1, 2, 3], subtitles.VisibleRows.Select(row => row.DisplayNumber));
        Assert.Same(document, session.DocumentSnapshot);
    }

    [Fact]
    public async Task FilteringPrunesHiddenSelectionWithoutChangingTheProject()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectSubtitleRows(document.Subtitles[0].Id, document.Subtitles.Select(line => line.Id));

        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));

        Assert.Equal(document.Subtitles.Where(line => line.ColorTagId == document.ColorTags[0].Id).Select(line => line.Id),
            session.ViewModel.Subtitles.VisibleRows.Select(row => row.Id));
        Assert.Equal(session.ViewModel.Subtitles.VisibleRows.Select(row => row.Id), session.SelectedSubtitleIds);
        Assert.DoesNotContain(document.Layers[1].Id, session.ViewModel.Effects.SelectedIds);
        Assert.DoesNotContain(document.Layers[1].Id, session.ViewModel.Timeline.SelectedLayerIds);
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidDraftRejectsFilterAndPreservesTheOriginalSelection()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var row = session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";
        try
        {
            Assert.False(session.TrySelectSubtitleColorTagFilter(SubtitleColorTagFilter.Untagged));
            Assert.Equal(SubtitleColorTagFilter.All, session.ViewModel.Subtitles.ColorTagFilter);
            Assert.Equal(document.Subtitles[0].Id, session.SelectedCueId);
            Assert.Equal("invalid", row.StartText);
            Assert.Same(document, session.DocumentSnapshot);
        }
        finally
        {
            row.Accept(document.Subtitles[0]);
        }
    }

    [Fact]
    public async Task ReassigningFilteredRowsPrunesThemAndUndoRestoresTheVisibleRows()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var filter = new SubtitleColorTagFilter(false, document.ColorTags[0].Id);
        Assert.True(session.TrySelectSubtitleColorTagFilter(filter));

        await session.SetSubtitleColorTagAsync([document.Subtitles[0].Id], document.ColorTags[1].Id);

        Assert.Equal(filter, session.ViewModel.Subtitles.ColorTagFilter);
        Assert.Single(session.ViewModel.Subtitles.VisibleRows);
        Assert.Null(session.SelectedCueId);
        Assert.Empty(session.SelectedSubtitleIds);
        Assert.Empty(session.ViewModel.Effects.SelectedIds);
        Assert.True(context.Editor.Undo());
        Assert.Equal(2, session.ViewModel.Subtitles.VisibleRows.Length);
        Assert.Same(document, session.DocumentSnapshot);
    }

    [Fact]
    public async Task SelectingAHiddenTimelineClipClearsFilterAndKeepsTheSelectedClip()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        var hidden = document.Layers[1];

        session.SelectLayer(hidden.Id, [hidden.Id]);

        Assert.Equal(SubtitleColorTagFilter.All, session.ViewModel.Subtitles.ColorTagFilter);
        Assert.Equal(hidden.SubtitleId, session.SelectedCueId);
        Assert.Equal(hidden.Id, session.SelectedLayerId);
        Assert.Equal(3, session.ViewModel.Subtitles.VisibleRows.Length);
    }

    [Fact]
    public async Task FilteredAdvanceSkipsHiddenRowsAndStopsAtTheLastResult()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));

        Assert.Equal(document.Subtitles[2].Id, await session.AdvanceSubtitleRowAsync(document.Subtitles[0].Id));
        Assert.Equal(document.Subtitles[2].Id, await session.AdvanceSubtitleRowAsync(document.Subtitles[2].Id));
        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ListMergeRejectsTheHiddenActualNextRow()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));

        Assert.False(session.CanMergeVisibleSubtitleSelection);
        await session.MergeVisibleSubtitleSelectionAsync();

        Assert.Same(document, session.DocumentSnapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task UndoingFirstLabelAssignmentResetsAnInvalidatedFilter()
    {
        var source = CreateDocument();
        var document = source with
        {
            ColorTags = [],
            Subtitles = source.Subtitles.Select(line => line with { ColorTagId = null }).ToImmutableArray()
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        await session.ApplySubtitleColorTagAsync([document.Subtitles[0].Id], source.ColorTags[0]);
        var tagId = Assert.Single(session.DocumentSnapshot.ColorTags).Id;
        Assert.True(session.TrySelectSubtitleColorTagFilter(new(false, tagId)));

        Assert.True(context.Editor.Undo());

        Assert.Equal(SubtitleColorTagFilter.All, session.ViewModel.Subtitles.ColorTagFilter);
        Assert.Equal(3, session.ViewModel.Subtitles.VisibleRows.Length);
    }

    private static ProjectDocument CreateDocument()
    {
        var tag = new SubtitleColorTag { Name = "To do", ColorHex = "#FF3B30" };
        var other = new SubtitleColorTag { Name = "Reviewed", ColorHex = "#007AFF" };
        var lines = Enumerable.Range(0, 3).Select(index => new SubtitleLine
        {
            Text = "Line " + index,
            Start = new(index * 3),
            End = new(index * 3 + 2),
            ColorTagId = index == 1 ? other.Id : tag.Id
        }).ToImmutableArray();
        return new()
        {
            ColorTags = [tag, other],
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
    }
}
