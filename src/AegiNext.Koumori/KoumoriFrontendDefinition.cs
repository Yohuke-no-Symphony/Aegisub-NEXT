namespace AegiNext.Koumori;

internal sealed class KoumoriFrontendDefinition
{
    public required int SchemaVersion { get; init; }
    public required int SampleRate { get; init; }
    public required int WindowSamples { get; init; }
    public required int FftSize { get; init; }
    public required int HopSamples { get; init; }
    public required int FrameCenterOffsetSamples { get; init; }
    public required int InputFrames { get; init; }
    public required int OutputFrames { get; init; }
    public required int ContextFrames { get; init; }
    public required int RightContextFrames { get; init; }
    public required int FeatureCount { get; init; }
    public required int MelBins { get; init; }
    public required string[] FeatureOrder { get; init; }
    public required float[] HannWindow { get; init; }
    public required float[][] MelFilters { get; init; }
    public required double PowerScale { get; init; }
    public required double MelLogFloor { get; init; }
    public required double RmsLogFloor { get; init; }
    public required float[] NormalizationMean { get; init; }
    public required float[] NormalizationStd { get; init; }
    public required string MissingContext { get; init; }
    public required string FrameCount { get; init; }
}
