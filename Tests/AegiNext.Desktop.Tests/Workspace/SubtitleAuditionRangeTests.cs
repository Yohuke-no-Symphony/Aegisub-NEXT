using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class SubtitleAuditionRangeTests
{
    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, 1, 4999, 5000)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, 1, 7000, 7001)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, 1, 5000, 5001)]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, 750, 4250, 5000)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, 750, 7000, 7750)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, 750, 5000, 5750)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_END, 1, 6999, 7000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_END, 750, 6250, 7000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_END, 5000, 5000, 7000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, 1, 5000, 7000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, 750, 5000, 7000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, int.MaxValue, 5000, 7000)]
    public void ConfiguredMillisecondsChangeQweWhileWholeCueRemainsIndependent(WorkbenchCommand command,
        int milliseconds, long start, long end)
    {
        var cue = new SubtitleLine { Start = new(2), End = new(4) };
        var media = new VideoPreviewMedia(0, new(3), new(20), 1);

        var range = SubtitleAuditionRange.Resolve(cue, media, command, milliseconds);

        Assert.Equal(new MediaTimeRange(new(start, 1000), new(end, 1000)), range);
    }

    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, 3000, 5000)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, 7000, 23000)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, 5000, 7000)]
    public void MaximumIntLengthClipsOnlyToTheCueAndAvailableMedia(WorkbenchCommand command, long start, long end)
    {
        var cue = new SubtitleLine { Start = new(2), End = new(4) };
        var media = new VideoPreviewMedia(0, new(3), new(20), 1);

        Assert.Equal(new MediaTimeRange(new(start, 1000), new(end, 1000)),
            SubtitleAuditionRange.Resolve(cue, media, command, int.MaxValue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveLengthIsRejectedBeforeProducingAPlaybackRange(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleAuditionRange.Resolve(
            new() { Start = new(2), End = new(4) }, new(0, MediaTime.Zero, new(20), 1),
            WorkbenchCommand.AUDITION_SUBTITLE, milliseconds));
    }

    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, 4500, 5000)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, 7000, 7500)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, 5000, 5500)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, 5000, 7000)]
    public void OriginalKeysUsePrimaryCueAndOriginalMediaOrigin(WorkbenchCommand command, long start, long end)
    {
        var cue = new SubtitleLine { Start = new(2), End = new(4) };
        var media = new VideoPreviewMedia(0, new(3), new(20), 1);
        var range = SubtitleAuditionRange.Resolve(cue, media, command);
        Assert.Equal(new MediaTimeRange(new(start, 1000), new(end, 1000)), range);
    }

    [Fact]
    public void ShortCueKeepsRationalEndAndMediaBoundsClipOnlyTheUnavailableAudio()
    {
        var media = new VideoPreviewMedia(0, new(3), new(1), 1);
        var cue = new SubtitleLine { Start = new(1, 10), End = new(1, 3) };
        Assert.Equal(new MediaTimeRange(new(31, 10), new(10, 3)),
            SubtitleAuditionRange.Resolve(cue, media, WorkbenchCommand.AUDITION_SUBTITLE_BEGIN));
        Assert.Equal(new MediaTimeRange(new(3), new(31, 10)),
            SubtitleAuditionRange.Resolve(cue, media, WorkbenchCommand.AUDITION_BEFORE_SUBTITLE));
        cue = cue with { Start = new(1, 2), End = new(9, 10) };
        Assert.Equal(new MediaTimeRange(new(39, 10), new(4)),
            SubtitleAuditionRange.Resolve(cue, media, WorkbenchCommand.AUDITION_AFTER_SUBTITLE));
    }

    [Fact]
    public void MediaEdgesAndOutOfMediaCuesProduceNoEmptyPlaybackRequest()
    {
        var media = new VideoPreviewMedia(0, new(-2), new(1), 1);
        var cue = new SubtitleLine { Start = MediaTime.Zero, End = new(1) };
        Assert.Null(SubtitleAuditionRange.Resolve(cue, media, WorkbenchCommand.AUDITION_BEFORE_SUBTITLE));
        Assert.Null(SubtitleAuditionRange.Resolve(cue, media, WorkbenchCommand.AUDITION_AFTER_SUBTITLE));
        Assert.Null(SubtitleAuditionRange.Resolve(cue with { Start = new(2), End = new(3) }, media,
            WorkbenchCommand.AUDITION_SUBTITLE));
    }
}
