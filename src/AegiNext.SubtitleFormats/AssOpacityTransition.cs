using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssOpacityTransition(MediaTime Start, MediaTime End, double First, double Last, bool Approximate = false)
{
    internal double Evaluate(MediaTime time)
    {
        if (time >= End)
        {
            return Last;
        }
        if (time <= Start)
        {
            return First;
        }
        var elapsed = time - Start;
        var duration = End - Start;
        var fraction = ((double)elapsed.Numerator / elapsed.Denominator) /
            ((double)duration.Numerator / duration.Denominator);
        return First + (Last - First) * fraction;
    }
}
