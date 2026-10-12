using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssTextAnimationChannel(AnimationValue initial)
{
    private AnimationValue initialValue = initial;
    private readonly List<AssTextAnimationOperation> operations = [];

    internal void Set(AnimationValue value, int mask = 0)
    {
        var effectiveMask = mask == 0 ? (1 << value.ComponentCount) - 1 : mask;
        for (var component = 0; component < value.ComponentCount; component++)
        {
            if ((effectiveMask & (1 << component)) != 0)
            {
                initialValue = initialValue.WithComponent(component, value.GetComponent(component));
            }
        }
        for (var index = operations.Count - 1; index >= 0; index--)
        {
            var operation = operations[index];
            var remaining = (operation.ComponentMask == 0 ? (1 << value.ComponentCount) - 1 : operation.ComponentMask) & ~effectiveMask;
            if (remaining == 0)
            {
                operations.RemoveAt(index);
            }
            else
            {
                operations[index] = operation with { ComponentMask = remaining };
            }
        }
    }

    internal void Add(AssTextAnimationOperation operation) => operations.Add(operation);

    internal AssTextAnimationSnapshot Snapshot(int excludedCandidate) => new(initialValue,
        operations.Where(operation => excludedCandidate == 0 || operation.KaraokeCandidate != excludedCandidate).ToImmutableArray());
}
