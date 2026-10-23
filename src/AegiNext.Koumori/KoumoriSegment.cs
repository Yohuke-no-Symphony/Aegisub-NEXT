using AegiNext.Core.Timing;

namespace AegiNext.Koumori;

/// <summary>
/// A half-open subtitle interval predicted independently in one overlap slot.
/// </summary>
public sealed record KoumoriSegment(long StartSample, long EndSample, float Score, int Slot)
{
    public int Lane { get; init; }

    public MediaTime StartTime => new(StartSample, KoumoriAnalyzer.SAMPLE_RATE);

    public MediaTime EndTime => new(EndSample, KoumoriAnalyzer.SAMPLE_RATE);

    public double StartSeconds => (double)StartSample / KoumoriAnalyzer.SAMPLE_RATE;

    public double EndSeconds => (double)EndSample / KoumoriAnalyzer.SAMPLE_RATE;
}
