using AegiNext.Core.Timing;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Editing;

internal static class KoumoriSnapResolver
{
    internal static TimelineSnapResult Resolve(MediaTime value, KoumoriBoundaryKind kind,
        IReadOnlyList<KoumoriBoundary> candidates, double pixelsPerSecond, int maximumMilliseconds,
        TimelineSnapResult ordinary)
    {
        var result = ordinary;
        var distance = ordinary.Boundary.HasValue ? Seconds(ordinary.Value - value) : double.PositiveInfinity;
        var limit = Math.Min(maximumMilliseconds / 1000d, 8 / pixelsPerSecond);
        foreach (var candidate in candidates)
        {
            if (candidate.Kind != kind)
            {
                continue;
            }
            var difference = Seconds(candidate.Time - value);
            if (difference <= limit && difference < distance)
            {
                result = new(candidate.Time, candidate.Time);
                distance = difference;
            }
        }
        return result;
    }

    private static double Seconds(MediaTime time) => Math.Abs((double)time.Numerator / time.Denominator);
}
