using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssEventClock
{
    internal static MediaTime Origin(MediaTime contentStart, MediaTime eventStart, MediaTime animationOffset, MediaTime timeOffset)
    {
        return new MediaTime((eventStart + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value, 100) -
            timeOffset - contentStart + animationOffset;
    }
}
