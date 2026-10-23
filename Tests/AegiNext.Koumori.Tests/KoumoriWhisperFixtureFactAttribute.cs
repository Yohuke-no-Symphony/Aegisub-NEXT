namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriWhisperFixtureFactAttribute : FactAttribute
{
    public KoumoriWhisperFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_WHISPER_ENCODER_FIXTURE")))
        {
            Skip = "Requires an explicit Whisper encoder package with synthetic frontend and context references.";
        }
    }
}
