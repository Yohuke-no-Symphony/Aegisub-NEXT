using System.Globalization;
using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Shortcuts.TimingEnterResult? pendingTimingEntry;
    private MediaTime? pendingTimingEnd;
    private static MediaTime DefaultSubtitleDuration => new(2);

    internal async Task AddCueAsync()
    {
        if (CurrentTrackId is not { } trackId)
        {
            return;
        }
        var start = ProjectPosition < MediaTime.Zero ? MediaTime.Zero : ProjectPosition;
        await AddCueAsync(trackId, start, start + DefaultSubtitleDuration);
    }

    private async Task AddCueAsync(Guid trackId, MediaTime start, MediaTime end)
    {
        var presetId = ViewModel.Styles.Subtitle.SelectedPreset?.Id;
        var cue = new SubtitleLine { Start = start, End = end, Text = string.Empty };
        await CreateSubtitleClipsAsync([cue], trackId, presetId);

        SelectCue(cue.Id);
    }

    internal Task SetCueStartAsync() => SetCueStartAsync(ProjectPosition);

    private async Task SetCueStartAsync(MediaTime position)
    {
        if (CurrentTrackId is not { } trackId)
        {
            return;
        }
        var start = position < MediaTime.Zero ? MediaTime.Zero : position;
        await BeginTimingCueAsync(trackId, start);
    }

    internal async Task CreateSubtitleClipsAsync(IEnumerable<SubtitleLine> lines, Guid trackId, Guid? fallbackPresetId)
    {
        var imported = lines.ToArray();
        if (imported.Length == 0)
        {
            return;
        }
        var source = editor.Snapshot;
        var inputRevision = TaskInputRevision;
        if (AegiTaskExecutionContext.Current is { } parent)
        {
            await parent.RunStageAsync("Tasks.CreateSubtitleClips", context =>
                CreateSubtitleClipsCoreAsync(imported, trackId, fallbackPresetId, source, inputRevision, context),
                CreateSubtitleClipsTask.GetResources(this));
        }
        else
        {
            await applicationContext.Tasks.Submit(new CreateSubtitleClipsTask(this, imported, trackId, fallbackPresetId,
                source, inputRevision)).Completion;
        }
    }

    internal async Task CreateSubtitleClipsCoreAsync(IReadOnlyList<SubtitleLine> imported, Guid trackId,
        Guid? fallbackPresetId, ProjectDocument source, long inputRevision, AegiTaskExecutionContext context)
    {
        var prepared = await styles.PrepareCreationAsync(trackId, fallbackPresetId, source);
        using var editingLease = context.AcquireEditLease();
        context.EnterCommit(() => !closing && inputRevision == TaskInputRevision && !HasProjectDrafts &&
            ReferenceEquals(source, editor.Snapshot));
        editor.Apply("Create subtitle clips", _ => ProjectEditingOperations.CreateSubtitleClips(prepared.Project,
            imported.Select(line => line with { StyleName = prepared.StyleName, StylePresetId = prepared.StylePresetId }),
            trackId, prepared.Style));
    }

    internal void SetCueEnd()
    {
        SetCueEndAt(ProjectPosition);
    }

    private void SetCueEndAt(MediaTime end)
    {
        if (ViewModel.Timeline.TimingPreview is not { } preview || timingPreviewTrackId is not { } trackId ||
            timingSession.ActiveCueId is null)
        {
            return;
        }

        var finalEnd = ResolveTimingEnd(preview.CueId, trackId, preview.Start, end);
        var exited = timingSession.Exit(finalEnd)!;
        ClearTimingPreview();
        CommitTimingPreview(preview with { End = exited.End });
        SelectCue(exited.CueId);
        ViewModel.RefreshCommands();
    }


    internal void SplitCue()
    {
        var cue = SelectedCue ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var boundaries = StringInfo.ParseCombiningCharacters(cue.Text).Append(cue.Text.Length).ToArray();
        if (!textCarets.TryGetValue(cue.Id, out var caret))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SplitCaret"));
        }

        var offset = boundaries.MinBy(value => Math.Abs(value - caret));


        editor.Apply("Split subtitle",
            document => ProjectEditingOperations.SplitSubtitle(document, cue.Id, ProjectPosition, offset));
    }

    internal void MergeCue()
    {
        MergeCue(MergeSubtitleTargets());
    }

    private void MergeCue(Guid[] targets)
    {
        if (targets.Length < 2)
        {
            return;
        }

        var prepared = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, targets);
        var merged = prepared.Subtitles.First(line => targets.Contains(line.Id));
        editor.Apply("Merge subtitles", _ => prepared);
        SelectCue(merged.Id);
    }

}
