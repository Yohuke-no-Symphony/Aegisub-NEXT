using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal async Task<Guid?> AdvanceSubtitleRowAsync(Guid sourceId)
    {
        if (IsUpdating || IsProjectBusy || closing)
        {
            return null;
        }

        var position = ProjectPosition;
        Guid? targetId = null;
        await RunCommandAsync(async () =>
        {
            if (!TryCommitDrafts())
            {
                return;
            }

            if (CurrentTrackId is not { } trackId)
            {
                return;
            }
            var filter = ViewModel.Subtitles.ColorTagFilter;
            var lines = ClipIndex.GetTrackSubtitles(trackId).Where(filter.Matches).ToArray();
            var index = Array.FindIndex(lines, line => line.Id == sourceId);
            if (index < 0)
            {
                return;
            }

            if (index + 1 < lines.Length)
            {
                var nextId = lines[index + 1].Id;
                if (SelectSubtitleRows(nextId, selectedSubtitleIds.Contains(nextId) ? selectedSubtitleIds : [nextId]))
                {
                    targetId = nextId;
                }
                return;
            }

            if (!filter.IsAll)
            {
                targetId = sourceId;
                return;
            }

            var start = lines[index].End;
            if (position <= start)
            {
                throw new InvalidDataException(Localization.Get("Workbench.SubtitleContinuationTimeRequired"));
            }

            var cue = new SubtitleLine { Start = start, End = position, Text = string.Empty };
            var presetId = ViewModel.Styles.Subtitle.SelectedPreset?.Id;
            InvalidateTimingSession();
            await CreateSubtitleClipsAsync([cue], trackId, presetId);

            if (!closing && CurrentTrackId == trackId && SelectSubtitleRows(cue.Id, [cue.Id]))
            {
                targetId = cue.Id;
            }
        });
        return targetId;
    }
}
