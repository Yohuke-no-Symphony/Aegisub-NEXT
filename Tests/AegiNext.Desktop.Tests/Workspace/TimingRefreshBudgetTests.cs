using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimingRefreshBudgetTests
{
    [Fact]
    public async Task TimingEnterPublishesOneDocumentRefreshAndKeepsExistingRows()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(128));
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("timing-refresh-budget.mkv");
        var session = context.Session;
        var original = context.Editor.Snapshot;
        var rows = session.ViewModel.Subtitles.Rows.ToArray();
        var refreshes = 0;
        session.SelectionChanged += (_, _) => refreshes++;

        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);

        Assert.Equal(1, refreshes);
        Assert.Equal(original.Subtitles.Length + 1, context.Editor.Snapshot.Subtitles.Length);
        Assert.All(rows, row => Assert.Same(row, session.ViewModel.Subtitles.Rows.Single(value => value.Id == row.Id)));
        var created = context.Editor.Snapshot.Subtitles.Single(line => !original.Subtitles.Any(value => value.Id == line.Id));
        Assert.Equal(created.Id, session.SelectedCueId);
        Assert.Equal(created.Id, session.SelectedLayer?.SubtitleId);
        Assert.Equal(created.Id, session.ViewModel.Subtitles.SelectedRow?.Id);
        Assert.Equal(created.Id, session.ViewModel.Timeline.TimingPreview?.CueId);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CleanDraftCommitDoesNotPublishADocumentRefreshAndKeepsMaskFields()
    {
        var document = CreateDocument(1);
        document = document with
        {
            Layers = [document.Layers[0] with { Mask = new RectangleClipMask { BottomRight = new(100, 100) } }]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var fields = session.MaskEditing.Fields.ToArray();
        Assert.NotEmpty(fields);
        var refreshes = 0;
        session.SelectionChanged += (_, _) => refreshes++;

        Assert.True(session.TryCommitDrafts());

        Assert.Equal(0, refreshes);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(fields.Select(field => field.Key), session.MaskEditing.Fields.Select(field => field.Key));
    }

    [Fact]
    public async Task CleanDraftCommitDoesNotAllocatePerSubtitle()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(3000));
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(context.Editor.Snapshot.Subtitles[0].Id);
        Assert.True(session.TryCommitDrafts());
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 8; index++)
        {
            Assert.True(session.TryCommitDrafts());
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 512 * 1024);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ChangedDraftStillRejectsTrackCollisionBeforeCommitting()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(2));
        await context.InitializeAsync();
        var session = context.Session;
        var original = context.Editor.Snapshot;
        var row = session.ViewModel.Subtitles.Rows[0];
        row.EndText = "12.5";

        Assert.False(session.TryCommitDrafts(false));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("12.5", row.EndText);
        Assert.True(row.IsDirty);
        Assert.NotNull(session.ViewModel.Subtitles.ValidationError);
    }

    [Fact]
    public async Task CleanCommitRestoresSelectedTransformOperationDraftsForTheNextEdit()
    {
        var document = CreateDocument(1);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), MediaTime.Zero, new(1), 0.5);
        var track = new AnimationTrack(AnimationProperty.OPACITY, []) { InitialValue = 1, Transforms = [operation] };
        document = document with { Layers = [document.Layers[0] with { Tracks = [track] }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.SceneEditing.Target = new(AnimationProperty.OPACITY);
        session.RefreshKeyframeInspector();
        Assert.Equal(operation.Id, session.ViewModel.Effects.SelectedOperation?.Id);
        Assert.True(session.TryCommitDrafts());

        session.ViewModel.Effects.OperationValueX.RawText = "0.75";

        Assert.Equal(0.75, session.PreviewDocument.Layers[0].Tracks[0].Transforms[0].Value.Scalar);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(session.TryCommitDrafts());
        Assert.Equal(0.75, context.Editor.Snapshot.Layers[0].Tracks[0].Transforms[0].Value.Scalar);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task CommittedCreationStillRefreshesAndClearsPendingTimingAfterANotificationFailure()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(1));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("timing-refresh-budget.mkv");
        var original = context.Editor.Snapshot;
        context.Editor.Changed += ThrowAfterCreation;
        try
        {
            await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        }
        finally
        {
            context.Editor.Changed -= ThrowAfterCreation;
        }

        Assert.Equal(original.Subtitles.Length + 1, context.Editor.Snapshot.Subtitles.Length);
        Assert.Equal(context.Editor.Snapshot.Subtitles.Length, session.ViewModel.Subtitles.Rows.Length);
        Assert.False(session.ViewModel.IsBusy);
        Assert.Null(session.ViewModel.Timeline.TimingPreview);
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        Assert.IsType<InvalidOperationException>(session.LastError);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);

        return;

        void ThrowAfterCreation(object? sender, EventArgs args)
        {
            if (context.Editor.Snapshot.Subtitles.Length > original.Subtitles.Length)
            {
                throw new InvalidOperationException("Creation observer failed.");
            }
        }
    }

    [Fact]
    public async Task EquivalentRowAndInspectorDraftsNormalizeWithoutADocumentRefresh()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(1));
        await context.InitializeAsync();
        var session = context.Session;
        var original = context.Editor.Snapshot;
        session.SelectCue(original.Subtitles[0].Id);
        var row = Assert.Single(session.ViewModel.Subtitles.Rows);
        row.StartText = "10.000000";
        session.ViewModel.Styles.Subtitle.FontSizeText = original.Subtitles[0].Style.FontSize.ToString("0.000", WorkbenchSession.InterfaceCulture);
        var refreshes = 0;
        session.SelectionChanged += (_, _) => refreshes++;

        Assert.True(session.TryCommitDrafts());

        Assert.Equal(0, refreshes);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.Equal(TimelineTimeText.Format(row.Original.Start), row.StartText);
        Assert.False(row.IsDirty);
        Assert.Equal(session.ViewModel.Styles.Subtitle.FontSize?.ToString(WorkbenchSession.InterfaceCulture), session.ViewModel.Styles.Subtitle.FontSizeText);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimingEnterKeepsInvalidRowOrExportDraftsAndDoesNotCreateACue(bool invalidRow)
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(1));
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("timing-refresh-budget.mkv");
        var session = context.Session;
        var original = context.Editor.Snapshot;
        var row = Assert.Single(session.ViewModel.Subtitles.Rows);
        if (invalidRow)
        {
            row.StartText = "invalid start";
        }
        else
        {
            session.ViewModel.Export.QualityText = "invalid quality";
        }

        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Null(session.ViewModel.Timeline.TimingPreview);
        Assert.Equal(invalidRow ? "subtitles" : "export", session.ViewModel.InvalidPanelId);
        Assert.Equal(invalidRow ? "invalid start" : TimelineTimeText.Format(row.Original.Start), row.StartText);
        if (!invalidRow)
        {
            Assert.Equal("invalid quality", session.ViewModel.Export.QualityText);
        }
    }

    [Fact]
    public async Task TimingOverlayReusesTheSamePreviewAndInvalidatesWhenEndOrSourceChanges()
    {
        await using var context = new WorkspaceSessionTestContext(CreateDocument(1));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("timing-refresh-budget.mkv");
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        await session.Controller.SeekAsync(new(1, 10));
        session.Tick();
        var first = session.PreviewDocument;

        Assert.Same(first, session.PreviewDocument);
        Assert.NotSame(context.Editor.Snapshot, first);
        await session.Controller.SeekAsync(new(1, 5));
        session.Tick();
        var second = session.PreviewDocument;
        Assert.NotSame(first, second);
        Assert.Same(second, session.PreviewDocument);
        var cueId = session.ViewModel.Timeline.TimingPreview!.CueId;
        Assert.Equal(new MediaTime(1, 5), second.Subtitles.Single(line => line.Id == cueId).End);
        context.Editor.Apply("Rename during preview", document => document with { Name = "Changed source" });
        var changed = session.PreviewDocument;
        Assert.NotSame(second, changed);
        Assert.Equal("Changed source", changed.Name);
        Assert.Same(changed, session.PreviewDocument);
    }

    [Fact]
    public async Task TimingBoundaryRecomputesAfterTheNextCueMoves()
    {
        var original = CreateDocument(1);
        var next = original.Subtitles[0] with { Start = new(1, 2), End = new(3, 4) };
        var document = original with
        {
            Subtitles = [next],
            Layers = [original.Layers[0] with { Start = next.Start, End = next.End }]
        };
        await using var context = new WorkspaceSessionTestContext(document, controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1))),
            (_, _) => new(_ => new PreviewTestSource(7, 0, 100, 200, 500, 600, 800, 1000)),
            () => new PreviewTestConverter(), Dispatch, update));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("timing-refresh-budget.mkv");
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        await session.Controller.SeekAsync(new(4, 5));
        session.Tick();
        Assert.Equal(new MediaTime(1, 2), session.ViewModel.Timeline.TimingPreview?.End);

        context.Editor.SetSubtitleTiming(next.Id, new(3, 5), new(4, 5), TimelineEditMode.CROP);

        Assert.Equal(new MediaTime(3, 5), session.ViewModel.Timeline.TimingPreview?.End);
    }

    private static ProjectDocument CreateDocument(int count)
    {
        var subtitles = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Start = new(10 + index * 2), End = new(11 + index * 2), Text = $"Subtitle {index} 字幕"
        }).ToImmutableArray();
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.GetFullPath("timing-refresh-budget.mkv"));
        return new()
        {
            Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero), Subtitles = subtitles,
            Layers = subtitles.Select(line => new ProjectLayer
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
