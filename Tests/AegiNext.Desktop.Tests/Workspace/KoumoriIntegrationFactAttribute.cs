namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class KoumoriIntegrationFactAttribute : FactAttribute
{
    public KoumoriIntegrationFactAttribute()
    {
        string[] variables = ["KOUMORI_NATIVE_MEDIA", "KOUMORI_TRAINING_PCM", "KOUMORI_PARITY_FIXTURE", "KOUMORI_INTEGRATION_TEMP_ROOT"];
        if (variables.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Requires explicit native media, training PCM, parity fixture and integration temp root; no source discovery is performed.";
        }
    }
}
