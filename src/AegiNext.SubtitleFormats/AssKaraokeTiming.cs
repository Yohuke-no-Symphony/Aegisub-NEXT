using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssKaraokeTiming
{
    internal const long MAX_CENTISECONDS = int.MaxValue / 10;

    internal static void ValidateCount(long count)
    {
        if (count is < -MAX_CENTISECONDS or > MAX_CENTISECONDS)
        {
            throw new InvalidDataException("ASS karaoke times exceed the 32-bit millisecond range supported by the player and cannot be converted faithfully.");
        }
    }

    internal static void ValidateClock(MediaTime time)
    {
        Quantize(time);
    }

    internal static long Quantize(MediaTime time)
    {
        try
        {
            var count = time.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value;
            ValidateCount(count);
            return count;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("ASS karaoke times exceed the convertible numeric range.", exception);
        }
    }
}
