namespace AegiNext.Koumori;

internal sealed class KoumoriEncoderFrontendDefinition
{
    public required int SchemaVersion { get; init; }
    public required string Algorithm { get; init; }
    public required int SampleRate { get; init; }
    public required int WindowSamples { get; init; }
    public required int FftSize { get; init; }
    public required int HopSamples { get; init; }
    public required int FrameCenterOffsetSamples { get; init; }
    public required int InputSamples { get; init; }
    public required int InputFrames { get; init; }
    public required int MelBins { get; init; }
    public required int HiddenFrames { get; init; }
    public required int HiddenSize { get; init; }
    public required int EncoderStrideSamples { get; init; }
    public required int CoreFrames { get; init; }
    public required int CoreHopSamples { get; init; }
    public required int LeftContextSamples { get; init; }
    public required int RightContextSamples { get; init; }
    public required int ContextStartFrame { get; init; }
    public required int ContextFrames { get; init; }
    public required string Padding { get; init; }
    public required string StftPadding { get; init; }
    public required bool StftCenter { get; init; }
    public required bool DropLastStftFrame { get; init; }
    public required double MelLogFloor { get; init; }
    public required double DynamicRange { get; init; }
    public required double LogOffset { get; init; }
    public required double LogScale { get; init; }
    public required float[] HannWindow { get; init; }
    public required float[][] MelFilters { get; init; }
}
