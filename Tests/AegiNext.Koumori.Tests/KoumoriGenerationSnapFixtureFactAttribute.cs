namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriGenerationSnapFixtureFactAttribute : FactAttribute
{
    public KoumoriGenerationSnapFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_GENERATION_SNAP_FIXTURE")))
        {
            Skip = "Requires an explicitly generated generation/snap shared-encoder contract fixture.";
        }
    }
}
