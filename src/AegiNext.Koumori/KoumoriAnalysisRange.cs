namespace AegiNext.Koumori;

/// <summary>
/// Identifies a half-open range on the 16 kHz media sample grid.
/// </summary>
public readonly record struct KoumoriAnalysisRange(long StartSample, long EndSample);
