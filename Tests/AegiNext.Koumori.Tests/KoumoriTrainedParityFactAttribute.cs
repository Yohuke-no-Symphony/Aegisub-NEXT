namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriTrainedParityFactAttribute : FactAttribute
{
    public KoumoriTrainedParityFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_TRAINED_PARITY")))
        {
            Skip = "Set KOUMORI_TRAINED_PARITY to generated real validation PCM and Python reference results.";
        }
    }
}
