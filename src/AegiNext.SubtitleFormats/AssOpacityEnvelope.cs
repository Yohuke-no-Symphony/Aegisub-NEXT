using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssOpacityEnvelope(double InitialOpacity, ImmutableArray<AssOpacityTransition> Transitions)
{
    internal bool Approximate => Transitions.Any(transition => transition.Approximate);

    internal bool HasPartialOpacity => InitialOpacity is > 0 and < 1 ||
        Transitions.Any(transition => transition.First is > 0 and < 1 || transition.Last is > 0 and < 1 ||
            transition.Start < transition.End && !transition.First.Equals(transition.Last));

    internal double Evaluate(MediaTime time)
    {
        var value = InitialOpacity;
        foreach (var transition in Transitions)
        {
            if (time < transition.Start)
            {
                return value;
            }
            value = transition.Evaluate(time);
            if (time < transition.End)
            {
                return value;
            }
        }
        return value;
    }

    internal AssOpacityEnvelope Clip(MediaTime start, MediaTime end)
    {
        var transitions = ImmutableArray.CreateBuilder<AssOpacityTransition>();
        foreach (var transition in Transitions)
        {
            if (transition.End <= start || transition.Start >= end)
            {
                continue;
            }
            var clippedStart = transition.Start < start ? start : transition.Start;
            var clippedEnd = transition.End > end ? end : transition.End;
            transitions.Add(new(clippedStart - start, clippedEnd - start,
                transition.Start == transition.End ? transition.First : transition.Evaluate(clippedStart),
                transition.Evaluate(clippedEnd), transition.Approximate));
        }
        return new(Evaluate(start), transitions.ToImmutable());
    }
}
