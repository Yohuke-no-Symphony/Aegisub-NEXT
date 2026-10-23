namespace AegiNext.Desktop.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class KoumoriNativeModelFactAttribute : FactAttribute
{
    public KoumoriNativeModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MEDIA"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_TRAINING_PCM"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MODEL")))
        {
            Skip = "Set KOUMORI_NATIVE_MEDIA, KOUMORI_TRAINING_PCM and KOUMORI_NATIVE_MODEL to run explicit compressed-audio model stability validation.";
        }
    }
}
