using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriWhisperFixtureTests(ITestOutputHelper output)
{
    [KoumoriWhisperFixtureFact]
    public void FullMelAndWhisperCoreContextMatchThePythonCpuReferences()
    {
        var directory = Environment.GetEnvironmentVariable("KOUMORI_WHISPER_ENCODER_FIXTURE")!;
        var fixtures = Path.Combine(directory, "fixtures");
        using var reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures, "frontend-reference.json")));
        using var info = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "encoder-info.json")));
        var root = reference.RootElement;
        var pcm = ReadFloats(fixtures, root.GetProperty("pcmFile").GetString()!, root.GetProperty("pcmSha256").GetString()!);
        Assert.Equal(root.GetProperty("sampleCount").GetInt32(), pcm.Length);
        var frontendBytes = ReadVerified(directory, info.RootElement, "encoderFrontend");
        var definition = KoumoriModelPackage.Deserialize<KoumoriEncoderFrontendDefinition>(frontendBytes);
        var frontend = new KoumoriEncoderFrontend(definition);
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        options.AddSessionConfigEntry("optimization.enable_gelu_approximation", "0");
        options.AddSessionConfigEntry("mlas.enable_gemm_fastmath_arm64_bfloat16", "0");
        using var encoder = new InferenceSession(ReadVerified(directory, info.RootElement, "encoder"), options);
        Assert.Equal(new[] { 1, 80, 3000 }, encoder.InputMetadata["mel"].Dimensions);
        Assert.Equal(new[] { 1, 1500, 768 }, encoder.OutputMetadata["hidden"].Dimensions);
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            var origin = item.GetProperty("chunkOriginSample").GetInt64();
            Assert.Equal(item.GetProperty("coreBegin").GetInt64() * 160 - 158080, origin);
            var chunk = new float[480000];
            var sourceStart = Math.Max(0, origin);
            var offset = checked((int)(sourceStart - origin));
            var length = checked((int)Math.Max(0, Math.Min(pcm.Length - sourceStart, chunk.Length - offset)));
            pcm.AsSpan(checked((int)sourceStart), length).CopyTo(chunk.AsSpan(offset));
            var mel = frontend.ExtractInput(chunk, default);
            var expectedMel = ReadFloats(fixtures, item.GetProperty("melFile").GetString()!, item.GetProperty("melSha256").GetString()!);
            Assert.Equal(expectedMel.Length, mel.Length);
            var melMaximum = 0f;
            for (var index = 0; index < mel.Length; index++)
            {
                Assert.True(float.IsFinite(mel[index]));
                melMaximum = Math.Max(melMaximum, Math.Abs(expectedMel[index] - mel[index]));
            }

            Assert.InRange(melMaximum, 0, root.GetProperty("melAbsoluteTolerance").GetSingle());
            using var input = OrtValue.CreateTensorValueFromMemory(mel, [1, 80, 3000]);
            using var run = new RunOptions();
            using var values = encoder.Run(run, ["mel"], [input], ["hidden"]);
            var hidden = values[0].GetTensorDataAsSpan<float>();
            var expected = ReadFloats(fixtures, item.GetProperty("contextFile").GetString()!, item.GetProperty("contextSha256").GetString()!);
            Assert.Equal(768 * 512, expected.Length);
            var squaredError = 0d;
            var referenceEnergy = 0d;
            var actualEnergy = 0d;
            var dot = 0d;
            var maximum = 0f;
            for (var feature = 0; feature < 768; feature++)
            {
                for (var frame = 0; frame < 512; frame++)
                {
                    var actual = hidden[(494 + frame) * 768 + feature];
                    Assert.True(float.IsFinite(actual));
                    var target = expected[feature * 512 + frame];
                    var error = (double)actual - target;
                    squaredError += error * error;
                    referenceEnergy += (double)target * target;
                    actualEnergy += (double)actual * actual;
                    dot += (double)actual * target;
                    maximum = Math.Max(maximum, Math.Abs(actual - target));
                }
            }

            var rms = Math.Sqrt(squaredError / expected.Length);
            var relativeRms = Math.Sqrt(squaredError / referenceEnergy);
            var cosine = dot / Math.Sqrt(referenceEnergy * actualEnergy);
            output.WriteLine($"core {item.GetProperty("coreBegin").GetInt32()}: mel max {melMaximum:R}, context max {maximum:R}, RMS {rms:R}, relative RMS {relativeRms:R}, cosine {cosine:R}.");
            Assert.InRange(rms, 0, root.GetProperty("encoderRmsTolerance").GetDouble());
            Assert.InRange(relativeRms, 0, 1e-4);
            Assert.InRange(cosine, 0.9999999, 1.00000001);
        }
    }

    private static byte[] ReadVerified(string directory, JsonElement manifest, string prefix)
    {
        var path = Path.Combine(directory, manifest.GetProperty(prefix + "File").GetString()!);
        var content = File.ReadAllBytes(path);
        Assert.Equal(manifest.GetProperty(prefix + "Sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(content)));
        return content;
    }

    private static float[] ReadFloats(string directory, string filename, string hash)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, filename));
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(0, bytes.Length % sizeof(float));
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
