using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private TimelineViewState timelineViewState = new();
    private TimelineViewState savedTimelineViewState = new();

    internal TimelineViewState TimelineViewState => timelineViewState;
    internal bool HasUnsavedChanges => HasProjectDrafts || editor.HasUnsavedChanges ||
        !TimelineViewStatesEqual(timelineViewState, savedTimelineViewState);

    internal void SetTimelineEffectClipCollapsed(TimelineEffectClipId id, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (closing || IsProjectBusy || id.InstanceId.HasValue || !HasTimelineEffectClip(DocumentSnapshot, id))
        {
            return;
        }

        var clips = timelineViewState.ExpandedAnimationClips;
        if (clips.Contains(id) != isCollapsed)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            ExpandedAnimationClips = isCollapsed ? clips.Remove(id) : clips.Add(id)
        });
    }

    internal void SetTimelineTrackCollapsed(Guid id, bool isCollapsed)
    {
        if (closing || IsProjectBusy || id == Guid.Empty || !GetTimelineTrackIds().Contains(id))
        {
            return;
        }

        var ids = timelineViewState.CollapsedTrackIds;
        if (ids.Contains(id) == isCollapsed)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            CollapsedTrackIds = isCollapsed ? ids.Add(id) : ids.Remove(id)
        });
    }

    internal void SetAllTimelineTracksCollapsed(bool isCollapsed)
    {
        if (closing || IsProjectBusy)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            CollapsedTrackIds = isCollapsed
                ? [.. timelineViewState.CollapsedTrackIds.Union(GetTimelineTrackIds())] : []
        });
    }

    internal IEnumerable<Guid> GetTimelineTrackIds() => DocumentSnapshot.Tracks.Select(track => track.Id);

    private void ApplyTimelineViewState(TimelineViewState state)
    {
        var next = NormalizeTimelineViewState(state);
        if (TimelineViewStatesEqual(timelineViewState, next))
        {
            return;
        }

        timelineViewState = next;
        ViewModel.Timeline.TimelineViewState = next;
        RefreshTitle();
        ViewModel.RefreshCommands();
    }

    internal void ResetTimelineViewState(ProjectDocument document)
    {
        var state = NormalizeTimelineViewState(document.TimelineViewState);
        ViewModel.Timeline.ResetEffectSelection();
        timelineViewState = state;
        savedTimelineViewState = state;
        ViewModel.Timeline.TimelineViewState = state;
    }

    internal ProjectDocument CreatePersistenceSnapshot(ProjectDocument contentSnapshot)
    {
        return TimelineViewStatesEqual(contentSnapshot.TimelineViewState, timelineViewState)
            ? contentSnapshot : contentSnapshot with { TimelineViewState = timelineViewState };
    }

    internal void AcceptProjectSave(ProjectDocument contentSnapshot, ProjectDocument persistedSnapshot, bool resetContent = false)
    {
        var savedState = NormalizeTimelineViewState(persistedSnapshot.TimelineViewState);
        var persistedContent = persistedSnapshot with { TimelineViewState = contentSnapshot.TimelineViewState };
        if (persistedContent == contentSnapshot)
        {
            persistedContent = contentSnapshot;
        }

        if (resetContent)
        {
            editor.Reset(persistedContent);
        }
        else
        {
            editor.MarkSaved(contentSnapshot, persistedContent);
        }

        savedTimelineViewState = savedState;
        RefreshTitle();
        ViewModel.RefreshCommands();
    }

    internal void AcceptRelocatedProjectSave(ProjectDocument expectedCurrent, ProjectDocument relocatedCurrent,
        ProjectDocument originalSaveSnapshot, ProjectDocument relocatedSaveSnapshot)
    {
        var savedState = NormalizeTimelineViewState(relocatedSaveSnapshot.TimelineViewState);
        var persistedContent = relocatedSaveSnapshot with { TimelineViewState = originalSaveSnapshot.TimelineViewState };
        var currentContent = relocatedCurrent with { TimelineViewState = expectedCurrent.TimelineViewState };
        if (ReferenceEquals(expectedCurrent, originalSaveSnapshot))
        {
            currentContent = persistedContent;
        }
        editor.AcceptRelocatedSave(expectedCurrent, currentContent, originalSaveSnapshot, persistedContent);
        savedTimelineViewState = savedState;
        RefreshTitle();
        ViewModel.RefreshCommands();
    }

    private void RefreshRelocatedDocument()
    {
        MaskEditing.RebindRelocatedSource(editor.Snapshot);
        ViewModel.Effects.RebindRelocatedSource(editor.Snapshot);
        RefreshDocument(preserveDrafts: true);
    }

    private static TimelineViewState NormalizeTimelineViewState(TimelineViewState state)
    {
        state.Validate();
        var sorted = state.ExpandedAnimationClips.OrderBy(clip => clip.LayerId)
            .ThenBy(clip => clip.Target.Property).ThenBy(clip => clip.Target.TextRangeId)
            .ThenBy(clip => clip.Target.State).ThenBy(clip => clip.Target.NodeId).ToArray();
        var sortedTracks = state.CollapsedTrackIds.Order().ToArray();
        return state.CollapsedAnimationRows.IsEmpty && state.ExpandedAnimationClips.SequenceEqual(sorted) &&
            state.CollapsedTrackIds.SequenceEqual(sortedTracks)
            ? state : state with { CollapsedAnimationRows = [], ExpandedAnimationClips = [.. sorted], CollapsedTrackIds = [.. sortedTracks] };
    }

    private static bool TimelineViewStatesEqual(TimelineViewState first, TimelineViewState second) =>
        first.ExpandedAnimationClips.SequenceEqual(second.ExpandedAnimationClips) &&
        first.CollapsedTrackIds.SequenceEqual(second.CollapsedTrackIds);
}
