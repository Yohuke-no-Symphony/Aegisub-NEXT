using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ClipClipboardContent? timelineClipboard;

    internal bool CanCopyTimelineClips => !closing && !IsProjectBusy && TimelineClipIds().Length > 0;
    internal bool CanPasteTimelineClips => !closing && !IsProjectBusy && timelineClipboard is { } content &&
        content.SourceProjectId == editor.Snapshot.Id && CurrentTrackId.HasValue;

    internal ImmutableArray<Guid> TimelineClipIds()
    {
        var selected = ViewModel.Effects.SelectedIds.ToHashSet();
        if (SelectedLayerId is { } primary)
        {
            selected.Add(primary);
        }

        return [.. editor.Snapshot.Layers.Where(layer => selected.Contains(layer.Id))
            .Select(layer => layer.Id)];
    }

    internal bool SelectTimelineLayers(TimelineSelectionEventArgs value)
    {
        if (IsUpdating || IsProjectBusy || closing || !TryCommitDrafts())
        {
            RefreshDocument();
            return false;
        }

        SelectLayer(value.Id, value.SelectedIds.ToArray());
        return SelectedLayerId == value.Id && ViewModel.Effects.SelectedIds.ToHashSet().SetEquals(value.SelectedIds);
    }

    internal Task CommitTimelineClipsMoveAsync(TimelineClipsMoveEventArgs value) =>
        RunCommandAsync(() => EditAsync(() =>
        {
            editor.ShiftClips(value.LayerIds, value.Offset);
            SelectLayer(value.PrimaryId, value.LayerIds.ToArray());
        }));

    internal Task CopyTimelineClipsAsync(Guid primaryId, IReadOnlyCollection<Guid> layerIds, ProjectDocument? expected = null) =>
        RunCommandAsync(() =>
        {
            if (TimelineContextIsCurrent(expected))
            {
                return EditAsync(() => timelineClipboard = ProjectEditingOperations.CaptureClips(editor.Snapshot, layerIds, primaryId, CurrentTrackId));
            }
            return Task.CompletedTask;
        });

    internal Task PasteTimelineClipsAsync(MediaTime time, ProjectDocument? expected = null) =>
        PasteTimelineClipsAsync(time, null, false, expected);

    internal Task PasteTimelineClipsAtTargetAsync(TimelineClipContextEventArgs target, ProjectDocument? expected = null) =>
        PasteTimelineClipsAsync(target.Time, target.TrackId, true, expected);

    private Task PasteTimelineClipsAsync(MediaTime time, Guid? targetTrackId, bool requiresTargetTrack, ProjectDocument? expected) =>
        RunCommandAsync(() =>
        {
            if (!CanPasteTimelineClips || !TimelineContextIsCurrent(expected))
            {
                return Task.CompletedTask;
            }

            var content = timelineClipboard!;
            return EditAsync(() =>
            {
                try
                {
                    if (requiresTargetTrack && targetTrackId is null)
                    {
                        throw new InvalidOperationException(Localization.Get("Workbench.TimelinePasteFailed"));
                    }
                    var result = editor.PasteClips(content, time < MediaTime.Zero ? MediaTime.Zero : time, targetTrackId);
                    SelectLayer(result.PrimaryId, result.LayerIds.ToArray());
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException)
                {
                    throw new InvalidOperationException(Localization.Get("Workbench.TimelinePasteFailed"), error);
                }
            });
        });

    internal Task DeleteTimelineClipsAsync(IReadOnlyCollection<Guid> layerIds, ProjectDocument? expected = null) =>
        RunCommandAsync(() =>
        {
            if (!TimelineContextIsCurrent(expected))
            {
                return Task.CompletedTask;
            }

            return EditAsync(() =>
            {
                editor.RemoveClips(layerIds);
                RefreshDocument();
            });
        });

    internal Task ClearTimelineClipAnimationTracksAsync(IReadOnlyCollection<Guid> layerIds,
        ProjectDocument? expected = null)
    {
        var selection = layerIds.ToArray();
        return RunCommandAsync(() => TimelineContextIsCurrent(expected)
            ? EditAsync(() => ClearTimelineAnimationTracks(selection, null)) : Task.CompletedTask);
    }

    private void ClearTimelineAnimationTracks(IReadOnlyCollection<Guid> layerIds, AnimationProperty? property)
    {
        ViewModel.CancelGestures();
        if (property is { } target)
        {
            editor.ClearAnimationTracks(layerIds, target);
        }
        else
        {
            editor.ClearAnimationTracks(layerIds);
        }
        ClearKeyframeSelection();
    }

    internal Task CreateTimelineSubtitleAsync(Guid trackId, MediaTime time, ProjectDocument expected) =>
        RunCommandAsync(async () =>
        {
            if (!TimelineContextIsCurrent(expected) || IsProjectBusy || !TryCommitDrafts())
            {
                return;
            }

            var start = time < MediaTime.Zero ? MediaTime.Zero : time;
            InvalidateTimingSession();
            var end = start + DefaultSubtitleDuration;
            foreach (var clip in ClipIndex.GetTrackClips(trackId))
            {
                if (clip.Start <= start && start < clip.End)
                {
                    throw new InvalidOperationException(Localization.Get("Workbench.TimelineClipCollision"));
                }
                if (clip.Start > start && clip.Start < end)
                {
                    end = clip.Start;
                }
            }
            await AddCueAsync(trackId, start, end);
        });

    internal void ClearTimelineClipboard() => timelineClipboard = null;

    private bool TimelineContextIsCurrent(ProjectDocument? expected) => !closing && !IsProjectBusy &&
        (expected is null || ReferenceEquals(expected, editor.Snapshot));
}
