using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Media;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimingPostProcessorWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCuesUseTheSelectedPresetNameForLaterTimingStyleFiltering(bool timingStart)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        var trackId = Assert.IsType<Guid>(session.CurrentTrackId);
        context.Editor.SetSubtitleTrackAutoApplyStyle(trackId, false);
        Assert.Null(context.Editor.Snapshot.Tracks.Single(track => track.Id == trackId).DefaultStyle);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Dialogue 中文 ABC 123", new() { FontSize = 41 });
        await session.Styles.UpsertAsync(preset);
        session.ViewModel.Styles.Subtitle.SelectedPreset = session.ViewModel.Styles.Subtitle.Presets.Single(value => value.Id == preset.Id);
        if (timingStart)
        {
            await session.SetCueStartAsync();
        }
        else
        {
            await session.AddCueAsync();
        }

        var created = Assert.Single(context.Editor.Snapshot.Subtitles);
        Assert.Equal(preset.Name, created.StyleName);
        Assert.Equal(preset.Id, created.StylePresetId);
        Assert.Equal(41, created.Style.FontSize);
        var changed = await session.ApplyTimingPostProcessorAsync(LeadOptions() with
        {
            LeadInEnabled = false, LeadOutMilliseconds = 100
        }, new HashSet<string> { preset.Name }, false);

        Assert.Equal(1, changed);
        Assert.Equal(created.End + new MediaTime(100, 1000), Assert.Single(context.Editor.Snapshot.Subtitles).End);
        Assert.Equal(preset.Name, Assert.Single(context.Editor.Snapshot.Subtitles).StyleName);
        Assert.False(session.IsProjectBusy);
    }

    [Fact]
    public async Task ActiveTimingPreviewFreezesBeforeTheProcessingSnapshotIsCaptured()
    {
        await using var context = new WorkspaceSessionTestContext(controllerFactory: _ => new(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(7)));
            }, (_, _) => new(_ => new PreviewTestSource(7, 0, 1000, 2000, 3000, 4000, 5000, 6000)),
            () => new PreviewTestConverter(), Dispatch, static _ => { }));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("controlled.mkv");
        await session.SetCueStartAsync();
        await session.Controller.SeekAsync(new(2));
        Assert.Equal(new MediaTime(2), session.Controller.Snapshot.Position);
        var created = Assert.Single(context.Editor.Snapshot.Subtitles);
        var preview = Assert.IsType<AegiNext.Desktop.Controls.TimelineTimingPreview>(session.ViewModel.Timeline.TimingPreview);
        session.ViewModel.Timeline.TimingPreview = preview with { End = new(2) };

        var changed = await session.ApplyTimingPostProcessorAsync(LeadOptions(), new HashSet<string> { created.StyleName }, false);

        Assert.Equal(1, changed);
        Assert.Null(session.ViewModel.Timeline.TimingPreview);
        Assert.Equal(new MediaTime(2200, 1000), Assert.Single(context.Editor.Snapshot.Subtitles).End);
        Assert.False(session.IsProjectBusy);
        Assert.True(context.Editor.Undo());
        Assert.Equal(new MediaTime(2), Assert.Single(context.Editor.Snapshot.Subtitles).End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulVideoIndexIsReusedUntilTheMediaFileOrLoadedEpochChanges(bool reopenVideo)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var mediaPath = Path.Combine(directory.Path, "source.mkv");
        await File.WriteAllBytesAsync(mediaPath, new byte[16], CancellationToken.None);
        var document = WithMedia(CreateDocument(), mediaPath, MediaTime.Zero);
        var editor = new ProjectEditor(document);
        var probeCalls = 0;
        await using var session = CreateSession(directory.Path, editor, (_, _, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            probeCalls++;
            return Task.FromResult(CreateVideoIndex());
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(mediaPath);
        var options = LeadOptions() with { KeyframeSnapEnabled = true };
        var styles = new HashSet<string> { "Default" };

        Assert.True(await session.ApplyTimingPostProcessorAsync(options, styles, false) > 0);
        Assert.Equal(1, probeCalls);
        Assert.True(editor.Undo());
        await session.WaitForProjectIdleAsync();
        Assert.True(await session.ApplyTimingPostProcessorAsync(options, styles, false) > 0);
        Assert.Equal(1, probeCalls);
        Assert.True(editor.Undo());
        await session.WaitForProjectIdleAsync();
        if (reopenVideo)
        {
            await session.Controller.OpenAsync(mediaPath);
        }
        else
        {
            await File.WriteAllBytesAsync(mediaPath, new byte[32], CancellationToken.None);
        }

        Assert.True(await session.ApplyTimingPostProcessorAsync(options, styles, false) > 0);
        Assert.Equal(2, probeCalls);
        Assert.False(session.IsProjectBusy);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task MediaFileChangeDuringIndexingRejectsTheResultAndDoesNotCacheTheFailedScan()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var mediaPath = Path.Combine(directory.Path, "source.mkv");
        await File.WriteAllBytesAsync(mediaPath, new byte[16], CancellationToken.None);
        var document = WithMedia(CreateDocument(), mediaPath, MediaTime.Zero);
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCalls = 0;
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            probeCalls++;
            if (probeCalls == 1)
            {
                entered.TrySetResult();
                return await pending.Task.WaitAsync(token);
            }

            return CreateVideoIndex();
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(mediaPath);
        var options = LeadOptions() with { KeyframeSnapEnabled = true };
        var styles = new HashSet<string> { "Default" };
        var applying = session.ApplyTimingPostProcessorAsync(options, styles, false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(mediaPath, new byte[32], CancellationToken.None);
            pending.TrySetResult(CreateVideoIndex());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsProjectBusy);

            Assert.True(await session.ApplyTimingPostProcessorAsync(options, styles, false) > 0);
            Assert.Equal(2, probeCalls);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            pending.TrySetResult(CreateVideoIndex());
            await ObserveCancelled(applying);
        }
    }

    [Fact]
    public async Task SelectedStyleScopeUpdatesOneCueAndOneUndoRestoresTheWholeDocument()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await session.Styles.Completion;
        var first = document.Subtitles[0];
        session.SelectCue(first.Id);
        var options = LeadOptions();

        var changed = await session.ApplyTimingPostProcessorAsync(options, new HashSet<string> { "Default" }, true);

        Assert.Equal(1, changed);
        var result = editor.Snapshot;
        Assert.Equal(new MediaTime(1900, 1000), result.Subtitles[0].Start);
        Assert.Equal(new MediaTime(3200, 1000), result.Subtitles[0].End);
        Assert.Same(document.Subtitles[1], result.Subtitles[1]);
        Assert.Same(document.Subtitles[2], result.Subtitles[2]);
        Assert.Equal(first.Id, session.SelectedCueId);
        Assert.False(session.IsProjectBusy);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result, editor.Snapshot);
    }

    [Fact]
    public async Task AllSubtitleScopeProcessesMatchingStylesAcrossTracks()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await session.Styles.Completion;
        session.SelectCue(document.Subtitles[1].Id);

        Assert.True(!session.IsUpdating && !session.IsProjectBusy && !session.Styles.IsBusy,
            $"Updating={session.IsUpdating}; Busy={session.IsProjectBusy}; Styles={session.Styles.IsBusy}; Drafts={session.HasProjectDrafts}");

        var changed = await session.ApplyTimingPostProcessorAsync(LeadOptions(), new HashSet<string> { "Default" }, false);

        Assert.Equal(2, changed);
        Assert.Equal(new MediaTime(1900, 1000), editor.Snapshot.Subtitles[0].Start);
        Assert.Same(document.Subtitles[1], editor.Snapshot.Subtitles[1]);
        Assert.Equal(new MediaTime(1900, 1000), editor.Snapshot.Subtitles[2].Start);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task InvalidSubtitleDraftPreventsProcessingWithoutLosingTheDraftOrHistory()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await session.Styles.Completion;
        session.SelectCue(document.Subtitles[0].Id);
        var row = session.ViewModel.Subtitles.Rows.Single(row => row.Id == document.Subtitles[0].Id);
        row.StartText = "invalid";

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyTimingPostProcessorAsync(LeadOptions(),
            new HashSet<string> { "Default" }, false));

        Assert.Equal("invalid", row.StartText);
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(session.IsProjectBusy);
        row.Accept(document.Subtitles[0]);
    }

    [Fact]
    public async Task EmptyStyleScopeIsAnUndoFreeNoOp()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await session.Styles.Completion;

        Assert.Equal(0, await session.ApplyTimingPostProcessorAsync(LeadOptions(), new HashSet<string>(), false));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(session.IsProjectBusy);
    }

    [Fact]
    public async Task PendingFrameProbeAllowsSelectionAndRejectsChangedInput()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var mediaPath = Path.Combine(directory.Path, "source.mkv");
        var document = WithMedia(CreateDocument(), mediaPath, new(5), 2);
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(directory.Path, editor, async (path, stream, origin, token) =>
        {
            Assert.Equal(mediaPath, path);
            Assert.Equal(2, stream);
            Assert.Equal(new MediaTime(5), origin);
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(mediaPath);
        var first = document.Subtitles[0];
        session.SelectCue(first.Id);
        var applying = session.ApplyTimingPostProcessorAsync(LeadOptions() with { KeyframeSnapEnabled = true },
            new HashSet<string> { "Default" }, true);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.IsProjectBusy);
            Assert.True(session.SelectSubtitleRows(document.Subtitles[1].Id, [document.Subtitles[1].Id]));
            Assert.Equal(document.Subtitles[1].Id, session.SelectedCueId);
            Assert.Equal(new[] { document.Subtitles[1].Id }, session.SelectedSubtitleIds);
            Assert.Equal(0, await session.ApplyTimingPostProcessorAsync(LeadOptions(), new HashSet<string> { "Default" }, false));
            release.TrySetResult(CreateVideoIndex());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsProjectBusy);
        }
        finally
        {
            release.TrySetResult(CreateVideoIndex());
            await ObserveCancelled(applying);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentOrVideoChangeWhileFrameProbeIsPendingRejectsTheStaleProcessingResult(bool replaceVideo)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = WithMedia(CreateDocument(), Path.Combine(directory.Path, "source.mkv"), MediaTime.Zero);
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(document.Assets[0].ExternalPath!);
        var applying = session.ApplyTimingPostProcessorAsync(LeadOptions() with { KeyframeSnapEnabled = true },
            new HashSet<string> { "Default" }, false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (replaceVideo)
            {
                await session.Controller.OpenAsync(Path.Combine(directory.Path, "different.mkv"));
            }
            else
            {
                editor.UpdateSubtitle(document.Subtitles[0].Id, cue => cue with { Text = "Changed while indexing" });
            }
            var latest = editor.Snapshot;
            release.TrySetResult(CreateVideoIndex());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(latest, editor.Snapshot);
            Assert.Equal(document.Subtitles[0].Start, editor.Snapshot.Subtitles[0].Start);
            Assert.False(session.IsProjectBusy);
            if (!replaceVideo)
            {
                Assert.True(editor.Undo());
                Assert.Same(document, editor.Snapshot);
            }
            Assert.False(editor.CanUndo);
        }
        finally
        {
            release.TrySetResult(CreateVideoIndex());
            await ObserveCancelled(applying);
        }
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("StartText")]
    public async Task PendingProbePreservesNewUncommittedRowInput(string field)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var mediaPath = Path.Combine(directory.Path, "source.mkv");
        var document = WithMedia(CreateDocument(), mediaPath, MediaTime.Zero);
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await pending.Task.WaitAsync(token);
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(mediaPath);
        var applying = session.ApplyTimingPostProcessorAsync(LeadOptions() with { KeyframeSnapEnabled = true },
            new HashSet<string> { "Default" }, false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var row = session.ViewModel.Subtitles.Rows[0];
            if (field == "Text")
            {
                row.Text = "draft changed during scan";
            }
            else
            {
                row.StartText = "invalid";
            }
            Assert.False(session.IsProjectBusy);
            pending.TrySetResult(CreateVideoIndex());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(document, editor.Snapshot);
            Assert.Same(row, session.ViewModel.Subtitles.Rows[0]);
            Assert.Equal(field == "Text" ? "draft changed during scan" : "invalid", field == "Text" ? row.Text : row.StartText);
            Assert.True(session.HasUnsavedChanges);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            pending.TrySetResult(CreateVideoIndex());
            await ObserveCancelled(applying);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrSessionReleaseDuringProbeDoesNotMutateTheProject(bool releaseSession)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var document = WithMedia(CreateDocument(), Path.Combine(directory.Path, "source.mkv"), MediaTime.Zero);
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await pending.Task.WaitAsync(token);
        });
        await session.Styles.Completion;
        await session.Controller.OpenAsync(document.Assets[0].ExternalPath!);
        var applying = session.ApplyTimingPostProcessorAsync(LeadOptions() with { KeyframeSnapEnabled = true },
            new HashSet<string> { "Default" }, false, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (releaseSession)
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                cancellation.Cancel();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsProjectBusy);
        }
        finally
        {
            cancellation.Cancel();
            pending.TrySetResult(CreateVideoIndex());
            await ObserveCancelled(applying);
        }
    }

    private static WorkbenchSession CreateSession(string directory, ProjectEditor editor,
        Func<string, int, MediaTime, CancellationToken, Task<VideoTimingIndex>>? probe = null)
    {
        return new(new WorkspaceDialogStub(),
            _ => new VideoPreviewController((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                var binding = editor.Snapshot.Media;
                return Task.FromResult(new VideoPreviewMedia(binding?.VideoStreamIndex ?? 0,
                    binding?.MediaOrigin ?? MediaTime.Zero, new(7)));
            }, (_, _) => new(_ => new PreviewTestSource(7, 0, 100, 200)),
                () => new PreviewTestConverter(), Dispatch, static _ => { }),
            Dispatch, editor, new WorkbenchPreferencesStore(directory), videoTimingProbe: probe);
    }

    private static TimingPostProcessorOptions LeadOptions() => new()
    {
        LeadInEnabled = true, LeadInMilliseconds = 100,
        LeadOutEnabled = true, LeadOutMilliseconds = 200,
        AdjacencyEnabled = false, KeyframeSnapEnabled = false
    };

    private static ProjectDocument CreateDocument()
    {
        var secondTrack = new ProjectTrack { Name = "Other track" };
        ImmutableArray<SubtitleLine> lines =
        [
            new() { Start = new(2), End = new(3), Text = "First 中文 ABC 123", StyleName = "Default" },
            new() { Start = new(4), End = new(5), Text = "Other style", StyleName = "Dialogue" },
            new() { Start = new(2), End = new(3), Text = "Other track", StyleName = "Default" }
        ];
        return new()
        {
            Tracks = [ProjectTrack.Default, secondTrack],
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Id = line.Id, TrackId = line.Id == lines[2].Id ? secondTrack.Id : ProjectTrack.DEFAULT_TRACK_ID,
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }

    private static ProjectDocument WithMedia(ProjectDocument document, string path, MediaTime origin, int stream = 0)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: path);
        return document with { Assets = [asset], Media = new(asset.Id, stream, null, origin) };
    }

    private static VideoTimingIndex CreateVideoIndex() => new(
        [new(0), new(1), new(2), new(3), new(4), new(5), new(6)], [0, 2, 3, 4, 5, 6]);

    private static async Task ObserveCancelled(Task<int> operation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
