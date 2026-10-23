namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriFixtureFactAttribute : FactAttribute
{
    public KoumoriFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_PARITY_FIXTURE")))
        {
            Skip = "Set KOUMORI_PARITY_FIXTURE to the training repository's generated contract-v1 fixture directory.";
        }
    }
}
