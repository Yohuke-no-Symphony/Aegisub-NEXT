using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid? timingPreviewTrackId;
    private bool timingPreviewFollowing;
    private bool timingPreviewWasPlaying;
    private bool committingTimingCreation;
    private ProjectDocument? timingBoundarySource;
    private Guid timingBoundaryCueId;
    private Guid timingBoundaryTrackId;
    private MediaTime timingBoundaryStart;
    private MediaTime? timingBoundaryNextStart;
    private ProjectDocument? timingOverlaySource;
    private TimelineTimingPreview? timingOverlayPreview;
    private ProjectDocument? timingOverlayDocument;

    private MediaTime ResolveTimingEnd(Guid cueId, Guid trackId, MediaTime start, MediaTime requested)
    {
        var end = start + new MediaTime(1, 1000);
        if (requested > end)
        {
            end = requested;
        }

        var source = editor.Snapshot;
        if (!ReferenceEquals(timingBoundarySource, source) || timingBoundaryCueId != cueId ||
            timingBoundaryTrackId != trackId || timingBoundaryStart != start)
        {
            MediaTime? nextStart = null;
            foreach (var clip in ClipIndex.GetTrackClips(trackId))
            {
                if (clip.SubtitleId != cueId && clip.Start > start &&
                    (nextStart is null || clip.Start < nextStart.Value))
                {
                    nextStart = clip.Start;
                }
            }
            timingBoundarySource = source;
            timingBoundaryCueId = cueId;
            timingBoundaryTrackId = trackId;
            timingBoundaryStart = start;
            timingBoundaryNextStart = nextStart;
        }
        if (timingBoundaryNextStart is { } next && end > next)
        {
            end = next;
        }

        return end;
    }

    private void StartTimingPreview(TimelineTimingPreview preview, Guid trackId)
    {
        if (ViewModel.Timeline.TimingPreview?.CueId != preview.CueId)
        {
            NotifyTaskInputChanged();
        }
        timingPreviewTrackId = trackId;
        timingPreviewFollowing = true;
        timingPreviewWasPlaying = controller.Snapshot.State == VideoPlaybackState.PLAYING;
        ViewModel.Timeline.TimingPreview = preview;
    }

    private void ClearTimingPreview()
    {
        timingPreviewFollowing = false;
        timingPreviewWasPlaying = false;
        timingPreviewTrackId = null;
        ViewModel.Timeline.TimingPreview = null;
        timingSession = timingSession.Reset();
        timingBoundarySource = null;
        timingOverlaySource = null;
        timingOverlayPreview = null;
        timingOverlayDocument = null;
    }

    private void UpdateTimingPreview(VideoPlaybackState state, MediaTime position)
    {
        if (!timingPreviewFollowing || ViewModel.Timeline.TimingPreview is not { } preview || timingPreviewTrackId is not { } trackId)
        {
            return;
        }

        if (timingPreviewWasPlaying && state != VideoPlaybackState.PLAYING)
        {
            InvalidateTimingSession();
            return;
        }

        timingPreviewWasPlaying |= state == VideoPlaybackState.PLAYING;
        var end = ResolveTimingEnd(preview.CueId, trackId, preview.Start, position);
        if (preview.End != end)
        {
            ViewModel.Timeline.TimingPreview = preview with { End = end };
        }
    }

    private bool FreezeTimingPreview()
    {
        var preview = ViewModel.Timeline.TimingPreview;
        if (pendingTimingEntry is not null && pendingTimingEnd is null && preview is not null)
        {
            pendingTimingEnd = preview.End;
        }

        ClearTimingPreview();
        if (preview is null || pendingTimingEntry is not null || closing)
        {
            return true;
        }

        try
        {
            CommitTimingPreview(preview);
            return true;
        }
        catch (Exception error)
        {
            ShowError(error);
            return false;
        }
    }

    private bool CommitTimingPreview(TimelineTimingPreview preview)
    {
        var cue = editor.Snapshot.Subtitles.FirstOrDefault(line => line.Id == preview.CueId);
        if (cue is not null && cue.Start == preview.Start && cue.End != preview.End)
        {
            editor.SetSubtitleTiming(cue.Id, cue.Start, preview.End, TimelineEditMode.CROP);
            return true;
        }
        return false;
    }

    private ProjectDocument OverlayTimingPreview(ProjectDocument document)
    {
        if (ViewModel.Timeline.TimingPreview is not { } preview)
        {
            return document;
        }
        if (ReferenceEquals(timingOverlaySource, document) && timingOverlayPreview == preview)
        {
            return timingOverlayDocument!;
        }

        var cue = document.Subtitles.FirstOrDefault(line => line.Id == preview.CueId);
        if (cue is null || cue.Start != preview.Start)
        {
            return CacheTimingOverlay(document, preview, document);
        }

        var subtitles = cue.End == preview.End ? document.Subtitles :
            document.Subtitles.SetItem(document.Subtitles.IndexOf(cue), cue with { End = preview.End });
        var layers = OverlayTimingPreviewLayers(document.Layers, preview);
        var overlaid = subtitles == document.Subtitles && layers == document.Layers ? document :
            document with { Subtitles = subtitles, Layers = layers };
        return CacheTimingOverlay(document, preview, overlaid);
    }

    private ProjectDocument CacheTimingOverlay(ProjectDocument source, TimelineTimingPreview preview, ProjectDocument overlaid)
    {
        timingOverlaySource = source;
        timingOverlayPreview = preview;
        timingOverlayDocument = overlaid;
        return overlaid;
    }

    private static ImmutableArray<ProjectLayer> OverlayTimingPreviewLayers(ImmutableArray<ProjectLayer> layers, TimelineTimingPreview preview)
    {
        var result = layers;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var end = layer.SubtitleId == preview.CueId ? preview.End : layer.End;
            if (layer.End != end)
            {
                result = result.SetItem(index, layer with { End = end });
            }
        }

        return result;
    }

    private async Task BeginTimingCueAsync(Guid trackId, MediaTime start)
    {
        var entered = timingSession.Enter(start);
        if (entered.IsRepeated || pendingTimingEntry is not null)
        {
            return;
        }

        var source = editor.Snapshot;
        if (ClipIndex.GetTrackClips(trackId).Any(clip => clip.Start <= start && start < clip.End))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.TimelineClipCollision"));
        }

        ViewModel.Timeline.ResumePlaybackFollow();
        var initialEnd = ResolveTimingEnd(entered.CueId, trackId, start, start);
        var preview = new TimelineTimingPreview(entered.CueId, start, initialEnd);
        var generation = projectGeneration;
        var presetId = ViewModel.Styles.Subtitle.SelectedPreset?.Id;
        pendingTimingEntry = entered;
        pendingTimingEnd = null;
        StartTimingPreview(preview, trackId);
        try
        {
            await applicationContext.Tasks.Submit(new BeginTimingCueTask(this, trackId, start, entered, source,
                generation, presetId, preview, TaskInputRevision)).Completion;
        }
        finally
        {
            if (pendingTimingEntry?.CueId == entered.CueId)
            {
                ClearTimingPreview();
                pendingTimingEntry = null;
                pendingTimingEnd = null;
            }
        }
    }

    internal async Task ExecutePendingTimingCreationAsync(Guid trackId, MediaTime start, Shortcuts.TimingEnterResult entered,
        ProjectDocument source, long generation, Guid? presetId, TimelineTimingPreview preview, long inputRevision,
        AegiTaskExecutionContext context)
    {
        var initialEnd = preview.End;
        var token = context.CancellationToken;
        using var editingLease = context.AcquireEditLease();
        var created = false;
        var refreshAfterFailure = false;
        MediaTime? finalEnd = null;
        try
        {
            var prepared = await styles.PrepareCreationAsync(trackId, presetId, source);
            token.ThrowIfCancellationRequested();
            if (closing || generation != projectGeneration || inputRevision != TaskInputRevision ||
                HasProjectDrafts || !ReferenceEquals(source, editor.Snapshot))
            {
                throw new OperationCanceledException("The timing entry target changed during preparation.", token);
            }

            context.EnterCommit(() => !closing && generation == projectGeneration && inputRevision == TaskInputRevision &&
                !HasProjectDrafts && ReferenceEquals(source, editor.Snapshot));
            committingTimingCreation = true;
            try
            {
                editor.Apply("Create subtitle clips", _ => ProjectEditingOperations.CreateSubtitleClips(prepared.Project,
                    [new() { Id = entered.CueId, Start = start, End = initialEnd, Text = string.Empty,
                        StyleName = prepared.StyleName, StylePresetId = prepared.StylePresetId }],
                    trackId, prepared.Style));
            }
            catch
            {
                refreshAfterFailure = !ReferenceEquals(source, editor.Snapshot);
                throw;
            }
            finally
            {
                committingTimingCreation = false;
            }
            created = true;
            preview = ViewModel.Timeline.TimingPreview ?? preview;
            finalEnd = pendingTimingEnd;
        }
        finally
        {
            ClearTimingPreview();
            pendingTimingEntry = null;
            pendingTimingEnd = null;
            editingLease.Dispose();
            if (refreshAfterFailure && !closing)
            {
                RefreshDocument();
            }
        }

        if (!created || closing)
        {
            return;
        }

        SynchronizeCueSelection(entered.CueId);
        if (finalEnd is { } end)
        {
            if (!CommitTimingPreview(preview with { End = end }))
            {
                RefreshDocument();
            }
        }
        else
        {
            timingSession = entered.Session;
            StartTimingPreview(preview, trackId);
            RefreshDocument();
        }

        SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        ViewModel.RefreshCommands();
    }
}
