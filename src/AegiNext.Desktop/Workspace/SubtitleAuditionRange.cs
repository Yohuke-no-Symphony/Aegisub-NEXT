using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Workspace;

internal static class SubtitleAuditionRange
{
    internal static MediaTimeRange? Resolve(SubtitleLine cue, VideoPreviewMedia media, WorkbenchCommand command,
        int milliseconds = 500)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds);
        var auditionLength = new MediaTime(milliseconds, 1000);
        var origin = media.Start ?? MediaTime.Zero;
        var start = origin + cue.Start;
        var end = origin + cue.End;
        switch (command)
        {
            case WorkbenchCommand.AUDITION_BEFORE_SUBTITLE:
                end = start;
                start -= auditionLength;
                break;
            case WorkbenchCommand.AUDITION_AFTER_SUBTITLE:
                start = end;
                end += auditionLength;
                break;
            case WorkbenchCommand.AUDITION_SUBTITLE_BEGIN:
                end = start + auditionLength < end ? start + auditionLength : end;
                break;
            case WorkbenchCommand.AUDITION_SUBTITLE_END:
                start = end - auditionLength > start ? end - auditionLength : start;
                break;
            case WorkbenchCommand.AUDITION_SUBTITLE:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        start = start > origin ? start : origin;
        if (media.Duration is { } duration && end > origin + duration)
        {
            end = origin + duration;
        }

        return start < end ? new(start, end) : null;
    }
}
