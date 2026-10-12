using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssFadeDefinition(double First, double Middle, double Last,
    MediaTime FirstStart, MediaTime FirstEnd, MediaTime LastStart, MediaTime LastEnd)
{
    internal double Evaluate(MediaTime time, bool before = false)
    {
        if (before ? time <= FirstStart : time < FirstStart)
        {
            return First;
        }
        if (before ? time <= FirstEnd : time < FirstEnd)
        {
            return Interpolate(First, Middle, time - FirstStart, FirstEnd - FirstStart);
        }
        if (before ? time <= LastStart : time < LastStart)
        {
            return Middle;
        }
        if (before ? time <= LastEnd : time < LastEnd)
        {
            return Interpolate(Middle, Last, time - LastStart, LastEnd - LastStart);
        }
        return Last;
    }

    private static double Interpolate(double first, double last, MediaTime elapsed, MediaTime duration)
    {
        if (elapsed <= MediaTime.Zero)
        {
            return first;
        }
        if (elapsed >= duration)
        {
            return last;
        }
        var fraction = ((double)elapsed.Numerator / elapsed.Denominator) / ((double)duration.Numerator / duration.Denominator);
        return first + (last - first) * fraction;
    }
}
