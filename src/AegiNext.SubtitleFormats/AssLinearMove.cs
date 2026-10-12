using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssLinearMove(ScenePoint First, ScenePoint Last, MediaTime Start, MediaTime End, bool Approximate = false)
{
    internal ScenePoint Evaluate(MediaTime time)
    {
        if (time <= Start)
        {
            return First;
        }
        if (time >= End)
        {
            return Last;
        }
        var elapsed = time - Start;
        var duration = End - Start;
        var fraction = ((double)elapsed.Numerator / elapsed.Denominator) / ((double)duration.Numerator / duration.Denominator);
        return new(First.X + (Last.X - First.X) * fraction, First.Y + (Last.Y - First.Y) * fraction);
    }
}
