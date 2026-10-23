namespace AegiNext.Koumori;

internal sealed class KoumoriDecoderDefinition
{
    public required int SchemaVersion { get; init; }
    public required string Algorithm { get; init; }
    public required double FrameSeconds { get; init; }
    public required Dictionary<string, KoumoriDecoderProfile> Profiles { get; init; }
}
