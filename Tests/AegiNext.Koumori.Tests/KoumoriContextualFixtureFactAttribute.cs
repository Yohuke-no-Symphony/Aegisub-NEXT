namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriContextualFixtureFactAttribute : FactAttribute
{
    public KoumoriContextualFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_CONTEXTUAL_FIXTURE")))
        {
            Skip = "Requires an explicitly generated contextual encoder/head contract fixture.";
        }
    }
}
