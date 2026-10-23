using System.Text.Json;

namespace AegiNext.Koumori;

internal sealed class KoumoriManifest
{
    public required int SchemaVersion { get; init; }
    public required string ModelId { get; init; }
    public required string ModelFile { get; init; }
    public required string ModelSha256 { get; init; }
    public required string FrontendFile { get; init; }
    public required string FrontendSha256 { get; init; }
    public required string DecoderFile { get; init; }
    public required string DecoderSha256 { get; init; }
    public required string InputName { get; init; }
    public required string OutputName { get; init; }
    public required int InputFrames { get; init; }
    public required int OutputFrames { get; init; }
    public required int ContextFrames { get; init; }
    public required int Slots { get; init; }
    public required string[] Channels { get; init; }
    public int? FeatureCount { get; init; }
    public int? SampleRate { get; init; }
    public string? Architecture { get; init; }
    public string? EncoderFile { get; init; }
    public string? EncoderSha256 { get; init; }
    public string? EncoderFrontendFile { get; init; }
    public string? EncoderFrontendSha256 { get; init; }
    public string? EncoderLicenseFile { get; init; }
    public string? EncoderLicenseSha256 { get; init; }
    public string? EncoderInputName { get; init; }
    public string? EncoderOutputName { get; init; }
    public string? ContextInputName { get; init; }
    public string? SnapModelFile { get; init; }
    public string? SnapModelSha256 { get; init; }
    public JsonElement Training { get; init; }
}
