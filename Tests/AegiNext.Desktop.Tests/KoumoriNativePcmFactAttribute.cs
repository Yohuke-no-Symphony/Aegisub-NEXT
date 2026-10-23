namespace AegiNext.Desktop.Tests;

internal sealed class KoumoriNativePcmFactAttribute : FactAttribute
{
    public KoumoriNativePcmFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MEDIA"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_TRAINING_PCM")))
        {
            Skip = "Requires explicit KOUMORI_NATIVE_MEDIA and KOUMORI_TRAINING_PCM paths; original media is never discovered automatically.";
        }
    }
}
