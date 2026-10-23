namespace AegiNext.Koumori;

/// <summary>
/// Reports completed central output frames, excluding inference context.
/// </summary>
public readonly record struct KoumoriAnalysisProgress(long CompletedFrames, long TotalFrames)
{
    public double Fraction => TotalFrames == 0 ? 1 : Math.Clamp((double)CompletedFrames / TotalFrames, 0, 1);
}
