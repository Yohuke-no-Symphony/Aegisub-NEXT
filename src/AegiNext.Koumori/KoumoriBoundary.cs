using AegiNext.Core.Timing;

namespace AegiNext.Koumori;

/// <summary>
/// A scored subtitle boundary candidate on the media sample grid.
/// </summary>
public sealed record KoumoriBoundary(long Sample, KoumoriBoundaryKind Kind, float Score, int Slot)
{
    public MediaTime Time => new(Sample, KoumoriAnalyzer.SAMPLE_RATE);

    public double Seconds => (double)Sample / KoumoriAnalyzer.SAMPLE_RATE;
}
