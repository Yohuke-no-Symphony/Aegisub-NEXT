namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriTestProgress(Action<KoumoriAnalysisProgress> report) : IProgress<KoumoriAnalysisProgress>
{
    public void Report(KoumoriAnalysisProgress value)
    {
        report(value);
    }
}
