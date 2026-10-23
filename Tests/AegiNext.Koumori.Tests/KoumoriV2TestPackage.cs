using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriV2TestPackage : IDisposable
{
    private static readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal KoumoriV2TestPackage()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "AegiNext.Koumori.V2.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        var frontend = new KoumoriFrontendDefinition
        {
            SchemaVersion = 1, SampleRate = 16000, WindowSamples = 400, FftSize = 512, HopSamples = 160,
            FrameCenterOffsetSamples = 0, InputFrames = 2048, OutputFrames = 1024, ContextFrames = 512,
            RightContextFrames = 512, FeatureCount = 82, MelBins = 80,
            FeatureOrder = ["log_mel_0_to_79", "log_rms", "positive_log_mel_flux"],
            HannWindow = EncoderDefinition.HannWindow, MelFilters = Enumerable.Range(0, 80).Select(_ => new float[257]).ToArray(),
            PowerScale = 1, MelLogFloor = 1e-10, RmsLogFloor = 1e-5, NormalizationMean = new float[82],
            NormalizationStd = Enumerable.Repeat(1f, 82).ToArray(), MissingContext = "zero_after_normalization",
            FrameCount = "ceil(sample_count/hop_samples)"
        };
        Write("model.onnx", [0]);
        Write("encoder.onnx", [0]);
        Write("WHISPER-LICENSE.txt", "MIT synthetic contract fixture notice"u8.ToArray());
        Write("frontend.json", JsonSerializer.SerializeToUtf8Bytes(frontend, options));
        Write("encoder-frontend.json", JsonSerializer.SerializeToUtf8Bytes(EncoderDefinition, options));
        Write("decoder.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, algorithm = "coverage-boundary-slots-v1", frameSeconds = 0.01,
            profiles = new Dictionary<string, object>
            {
                ["default"] = new
                {
                    coverageThreshold = 0.5f, startThreshold = 0.5f, endThreshold = 0.5f,
                    peakRadiusFrames = 3, edgeRefinementFrames = 20, minDurationFrames = 15, dedupToleranceFrames = 1
                }
            }
        }));
        Manifest = new()
        {
            ["schemaVersion"] = 2, ["architecture"] = "whisper-small-local-fusion-v1", ["modelId"] = "contextual-fixture",
            ["inputName"] = "features", ["contextInputName"] = "context", ["outputName"] = "probabilities",
            ["inputFrames"] = 2048, ["outputFrames"] = 1024, ["contextFrames"] = 512, ["slots"] = 3,
            ["featureCount"] = 82, ["sampleRate"] = 16000,
            ["channels"] = new JsonArray("coverage", "start", "end"), ["encoderInputName"] = "mel", ["encoderOutputName"] = "hidden"
        };
        foreach (var (prefix, filename) in new[]
                 {
                     ("model", "model.onnx"), ("frontend", "frontend.json"), ("decoder", "decoder.json"),
                     ("encoder", "encoder.onnx"), ("encoderFrontend", "encoder-frontend.json"),
                     ("encoderLicense", "WHISPER-LICENSE.txt")
                 })
        {
            Manifest[prefix + "File"] = filename;
            Manifest[prefix + "Sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, filename))));
        }
        SaveManifest();
    }

    internal string DirectoryPath { get; }
    internal JsonObject Manifest { get; }
    internal KoumoriEncoderFrontendDefinition EncoderDefinition { get; } = CreateEncoderDefinition();

    internal void SaveManifest()
    {
        File.WriteAllText(Path.Combine(DirectoryPath, "manifest.json"), Manifest.ToJsonString());
    }

    internal void ChangeEncoderDefinition(string field, JsonNode? value)
    {
        var path = Path.Combine(DirectoryPath, "encoder-frontend.json");
        var definition = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        definition[field] = value;
        var content = JsonSerializer.SerializeToUtf8Bytes(definition);
        Write("encoder-frontend.json", content);
        Manifest["encoderFrontendSha256"] = Convert.ToHexStringLower(SHA256.HashData(content));
        SaveManifest();
    }

    public void Dispose()
    {
        Directory.Delete(DirectoryPath, true);
    }

    internal static KoumoriEncoderFrontendDefinition CreateEncoderDefinition()
    {
        return new()
        {
            SchemaVersion = 1, Algorithm = "whisper-logmel-v1", SampleRate = 16000, WindowSamples = 400,
            FftSize = 400, HopSamples = 160, FrameCenterOffsetSamples = 0, InputSamples = 480000,
            InputFrames = 3000, MelBins = 80, HiddenFrames = 1500, HiddenSize = 768, EncoderStrideSamples = 320,
            CoreFrames = 1024, CoreHopSamples = 163840, LeftContextSamples = 158080, RightContextSamples = 158080,
            ContextStartFrame = 494, ContextFrames = 512, Padding = "zero_pcm_outside_media", StftPadding = "reflect",
            StftCenter = true, DropLastStftFrame = true, MelLogFloor = 1e-10, DynamicRange = 8, LogOffset = 4, LogScale = 4,
            HannWindow = Enumerable.Range(0, 400).Select(index => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * index / 400))).ToArray(),
            MelFilters = Enumerable.Range(0, 80).Select(band =>
            {
                var filter = new float[201];
                filter[band] = 1;
                return filter;
            }).ToArray()
        };
    }

    private void Write(string filename, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(DirectoryPath, filename), bytes);
    }
}
