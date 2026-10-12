using System.Collections.Immutable;

namespace AegiNext.SubtitleFormats;

internal sealed class AssNumericImportChannel(double initial)
{
    private double currentInitial = initial;
    private readonly List<AssNumericImportOperation> currentOperations = [];
    private bool observed;
    private bool hasAnimation;

    internal double Initial { get; private set; } = initial;
    internal ImmutableArray<AssNumericImportOperation> Operations { get; private set; } = [];
    internal bool Mixed { get; private set; }
    internal bool HasAnimation => hasAnimation;

    internal void Set(double value)
    {
        currentInitial = value;
        currentOperations.Clear();
    }

    internal void Add(AssTransformTiming timing, double target, int karaokeCandidate)
    {
        currentOperations.Add(new(timing, target, karaokeCandidate));
    }

    internal IEnumerable<double> Values(int excludedCandidate)
    {
        return currentOperations.Where(operation => excludedCandidate == 0 || operation.KaraokeCandidate != excludedCandidate)
            .Select(operation => operation.Value).Prepend(currentInitial);
    }

    internal void Observe(int excludedCandidate)
    {
        var operations = currentOperations.Where(operation => excludedCandidate == 0 || operation.KaraokeCandidate != excludedCandidate).ToImmutableArray();
        hasAnimation |= !operations.IsEmpty;
        if (!observed)
        {
            Initial = currentInitial;
            Operations = operations;
            observed = true;
            return;
        }
        Mixed |= !Initial.Equals(currentInitial) || Operations.Length != operations.Length ||
            Operations.Where((operation, index) => index < operations.Length &&
                (operation.Timing != operations[index].Timing || !operation.Value.Equals(operations[index].Value))).Any();
    }
}
