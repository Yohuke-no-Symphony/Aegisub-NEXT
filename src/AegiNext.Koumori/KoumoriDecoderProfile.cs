namespace AegiNext.Koumori;

internal sealed class KoumoriDecoderProfile
{
    public required float CoverageThreshold { get; init; }
    public required float StartThreshold { get; init; }
    public required float EndThreshold { get; init; }
    public float? SnapStartThreshold { get; init; }
    public float? SnapEndThreshold { get; init; }
    public bool RequirePairedInternalBoundary { get; init; }
    public int MaxCoverageGapFrames { get; init; }
    public int MaxContinuationGapFrames { get; init; }
    public int MaxContinuationSpanFrames { get; init; }
    public int MaxFragmentContinuationGapFrames { get; init; }
    public int MaxContinuationFragmentFrames { get; init; }
    public float? InternalStartThreshold { get; init; }
    public float? InternalEndThreshold { get; init; }
    public required int PeakRadiusFrames { get; init; }
    public required int EdgeRefinementFrames { get; init; }
    public required int MinDurationFrames { get; init; }
    public required int DedupToleranceFrames { get; init; }
}
