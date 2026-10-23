using System.Security.Cryptography;
using System.Text.Json;

namespace AegiNext.Koumori;

internal sealed class KoumoriModelPackage
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true
    };

    private KoumoriModelPackage(KoumoriManifest manifest, KoumoriFrontendDefinition frontend, KoumoriDecoderDefinition decoder,
        byte[] model, KoumoriEncoderFrontendDefinition? encoderFrontend, byte[]? encoderModel, byte[]? snapModel)
    {
        Manifest = manifest;
        Frontend = frontend;
        Decoder = decoder;
        Model = model;
        EncoderFrontend = encoderFrontend;
        EncoderModel = encoderModel;
        SnapModel = snapModel;
    }

    public KoumoriManifest Manifest { get; }
    public KoumoriFrontendDefinition Frontend { get; }
    public KoumoriDecoderDefinition Decoder { get; }
    public byte[] Model { get; }
    public KoumoriEncoderFrontendDefinition? EncoderFrontend { get; }
    public byte[]? EncoderModel { get; }
    public byte[]? SnapModel { get; }

    public static async Task<KoumoriModelPackage> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "manifest.json");
        RejectSymbolicLink(manifestPath);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var hasSnapMetadata = false;
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The Koumori manifest must be a JSON object.");
            }
            var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                {
                    throw new InvalidDataException("Koumori manifest properties cannot be duplicated or have case aliases.");
                }
                hasSnapMetadata |= property.Name.Equals("snapModelFile", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("snapModelSha256", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid Koumori package JSON.", exception);
        }
        var manifest = Deserialize<KoumoriManifest>(manifestBytes);
        if (manifest.SchemaVersion is not (1 or 2 or 3) || string.IsNullOrWhiteSpace(manifest.ModelId)
            || manifest.InputName != "features" || manifest.OutputName != "probabilities"
            || manifest.InputFrames != 2048 || manifest.OutputFrames != 1024 || manifest.ContextFrames != 512
            || manifest.FeatureCount is { } features && features != 82 || manifest.SampleRate is { } sampleRate && sampleRate != 16000
            || manifest.Slots is < 1 or > 64 || !manifest.Channels.SequenceEqual(["coverage", "start", "end"]))
        {
            throw new InvalidDataException("Unsupported Koumori model manifest contract.");
        }

        if (manifest.SchemaVersion is 2 or 3
            ? manifest.Architecture != (manifest.SchemaVersion == 3
                    ? "whisper-small-generation-snap-v1" : "whisper-small-local-fusion-v1") || manifest.EncoderInputName != "mel"
                || manifest.FeatureCount != 82 || manifest.SampleRate != 16000
                || manifest.EncoderOutputName != "hidden" || manifest.ContextInputName != "context"
                || manifest.EncoderFile is null || manifest.EncoderSha256 is null
                || manifest.EncoderFrontendFile is null || manifest.EncoderFrontendSha256 is null
                || manifest.EncoderLicenseFile is null || manifest.EncoderLicenseSha256 is null
            : manifest.Architecture is not null || manifest.EncoderFile is not null || manifest.EncoderSha256 is not null
                || manifest.EncoderFrontendFile is not null || manifest.EncoderFrontendSha256 is not null
                || manifest.EncoderLicenseFile is not null || manifest.EncoderLicenseSha256 is not null
                || manifest.EncoderInputName is not null || manifest.EncoderOutputName is not null || manifest.ContextInputName is not null)
        {
            throw new InvalidDataException("Unsupported Koumori encoder manifest contract.");
        }

        if (manifest.SchemaVersion == 3
            ? manifest.SnapModelFile != "snap-model.onnx" || string.IsNullOrWhiteSpace(manifest.SnapModelSha256)
            : hasSnapMetadata)
        {
            throw new InvalidDataException("Unsupported Koumori snap manifest contract.");
        }

        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        foreach (var filename in new[]
                 {
                     manifest.ModelFile, manifest.FrontendFile, manifest.DecoderFile, manifest.EncoderFile,
                     manifest.EncoderFrontendFile, manifest.EncoderLicenseFile, manifest.SnapModelFile
                 })
        {
            if (filename is not null && !filenames.Add(filename))
            {
                throw new InvalidDataException("Koumori package payloads cannot alias another package file.");
            }
        }

        var model = await ReadVerifiedAsync(root, manifest.ModelFile, manifest.ModelSha256, cancellationToken).ConfigureAwait(false);
        var frontend = Deserialize<KoumoriFrontendDefinition>(await ReadVerifiedAsync(root, manifest.FrontendFile, manifest.FrontendSha256, cancellationToken).ConfigureAwait(false));
        var decoder = Deserialize<KoumoriDecoderDefinition>(await ReadVerifiedAsync(root, manifest.DecoderFile, manifest.DecoderSha256, cancellationToken).ConfigureAwait(false));
        ValidateFrontend(frontend);
        ValidateDecoder(decoder);
        KoumoriEncoderFrontendDefinition? encoderFrontend = null;
        byte[]? encoderModel = null;
        if (manifest.SchemaVersion is 2 or 3)
        {
            var license = await ReadVerifiedAsync(root, manifest.EncoderLicenseFile!, manifest.EncoderLicenseSha256!, cancellationToken).ConfigureAwait(false);
            if (license.Length == 0)
            {
                throw new InvalidDataException("The Koumori encoder license notice cannot be empty.");
            }

            encoderFrontend = Deserialize<KoumoriEncoderFrontendDefinition>(await ReadVerifiedAsync(root,
                manifest.EncoderFrontendFile!, manifest.EncoderFrontendSha256!, cancellationToken).ConfigureAwait(false));
            ValidateEncoderFrontend(encoderFrontend);
            encoderModel = await ReadVerifiedAsync(root, manifest.EncoderFile!, manifest.EncoderSha256!, cancellationToken).ConfigureAwait(false);
        }

        var snapModel = manifest.SchemaVersion == 3
            ? await ReadVerifiedAsync(root, manifest.SnapModelFile!, manifest.SnapModelSha256!, cancellationToken).ConfigureAwait(false)
            : null;
        return new(manifest, frontend, decoder, model, encoderFrontend, encoderModel, snapModel);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, jsonOptions) ?? throw new InvalidDataException("Empty Koumori package document.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid Koumori package JSON.", exception);
        }
    }

    private static async Task<byte[]> ReadVerifiedAsync(string root, string file, string expectedHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || file.Contains('\\') || file.Contains('/')
            || file.Contains(':') || file.Contains('\0') || file is "." or ".." || string.IsNullOrWhiteSpace(expectedHash)
            || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Koumori package files must be direct children with a SHA-256 digest.");
        }

        var path = Path.Combine(root, file);
        RejectSymbolicLink(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Koumori package digest mismatch: {file}.");
        }

        return bytes;
    }

    private static void RejectSymbolicLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException("Koumori package files cannot be symbolic links.");
        }
    }

    private static void ValidateEncoderFrontend(KoumoriEncoderFrontendDefinition frontend)
    {
        if (frontend.SchemaVersion != 1 || frontend.Algorithm != "whisper-logmel-v1" || frontend.SampleRate != 16000
            || frontend.WindowSamples != 400 || frontend.FftSize != 400 || frontend.HopSamples != 160
            || frontend.FrameCenterOffsetSamples != 0 || frontend.InputSamples != 480000 || frontend.InputFrames != 3000
            || frontend.MelBins != 80 || frontend.HiddenFrames != 1500 || frontend.HiddenSize != 768
            || frontend.EncoderStrideSamples != 320 || frontend.CoreFrames != 1024 || frontend.CoreHopSamples != 163840
            || frontend.LeftContextSamples != 158080 || frontend.RightContextSamples != 158080
            || frontend.ContextStartFrame != 494 || frontend.ContextFrames != 512
            || frontend.Padding != "zero_pcm_outside_media" || frontend.StftPadding != "reflect" || !frontend.StftCenter
            || !frontend.DropLastStftFrame || frontend.MelLogFloor != 1e-10 || frontend.DynamicRange != 8
            || frontend.LogOffset != 4 || frontend.LogScale != 4 || !IsValidVector(frontend.HannWindow, 400, 0)
            || frontend.HannWindow.Any(value => value > 1) || !frontend.HannWindow.Any(value => value > 0)
            || frontend.MelFilters.Length != 80 || frontend.MelFilters.Any(row => !IsValidVector(row, 201, 0)
                || !row.Any(value => value > 0)))
        {
            throw new InvalidDataException("Unsupported or invalid Koumori encoder frontend.");
        }
    }

    private static void ValidateFrontend(KoumoriFrontendDefinition frontend)
    {
        if (frontend.SchemaVersion != 1 || frontend.SampleRate != 16000 || frontend.WindowSamples != 400
            || frontend.FftSize != 512 || frontend.HopSamples != 160 || frontend.FrameCenterOffsetSamples != 0
            || frontend.InputFrames != 2048 || frontend.OutputFrames != 1024 || frontend.ContextFrames != 512
            || frontend.RightContextFrames != 512 || frontend.FeatureCount != 82 || frontend.MelBins != 80
            || frontend.MissingContext != "zero_after_normalization" || frontend.FrameCount != "ceil(sample_count/hop_samples)"
            || !frontend.FeatureOrder.SequenceEqual(["log_mel_0_to_79", "log_rms", "positive_log_mel_flux"])
            || !IsValidVector(frontend.HannWindow, 400)
            || frontend.MelFilters.Length != 80 || frontend.MelFilters.Any(row => !IsValidVector(row, 257, 0))
            || !IsValidVector(frontend.NormalizationMean, 82)
            || !IsValidVector(frontend.NormalizationStd, 82, float.Epsilon)
            || !double.IsFinite(frontend.PowerScale) || frontend.PowerScale <= 0
            || !double.IsFinite(frontend.MelLogFloor) || frontend.MelLogFloor <= 0
            || !double.IsFinite(frontend.RmsLogFloor) || frontend.RmsLogFloor <= 0)
        {
            throw new InvalidDataException("Unsupported or invalid Koumori audio frontend.");
        }
    }

    private static void ValidateDecoder(KoumoriDecoderDefinition decoder)
    {
        if (decoder.SchemaVersion != 1 || decoder.Algorithm is not ("coverage-boundary-slots-v1" or "coverage-boundary-slots-v2") || !decoder.FrameSeconds.Equals(0.01)
            || !decoder.Profiles.ContainsKey("default") || decoder.Profiles.Values.Any(profile => !IsValidProfile(profile,
                decoder.Algorithm == "coverage-boundary-slots-v2")))
        {
            throw new InvalidDataException("Unsupported or invalid Koumori decoder.");
        }
    }

    private static bool IsValidVector(float[]? values, int length, float minimum = float.NegativeInfinity)
    {
        return values is not null && values.Length == length && values.All(value => float.IsFinite(value) && value >= minimum);
    }

    private static bool IsValidProfile(KoumoriDecoderProfile? profile, bool continuationSupported)
    {
        return profile is not null && float.IsFinite(profile.CoverageThreshold) && profile.CoverageThreshold is >= 0 and <= 1
            && float.IsFinite(profile.StartThreshold) && profile.StartThreshold is >= 0 and <= 1
            && float.IsFinite(profile.EndThreshold) && profile.EndThreshold is >= 0 and <= 1
            && (profile.SnapStartThreshold is null || float.IsFinite(profile.SnapStartThreshold.Value) && profile.SnapStartThreshold.Value is >= 0 and <= 1)
            && (profile.SnapEndThreshold is null || float.IsFinite(profile.SnapEndThreshold.Value) && profile.SnapEndThreshold.Value is >= 0 and <= 1)
            && (profile.InternalStartThreshold is null || float.IsFinite(profile.InternalStartThreshold.Value) && profile.InternalStartThreshold.Value is >= 0 and <= 1)
            && (profile.InternalEndThreshold is null || float.IsFinite(profile.InternalEndThreshold.Value) && profile.InternalEndThreshold.Value is >= 0 and <= 1)
            && profile.PeakRadiusFrames is >= 0 and <= 100 && profile.EdgeRefinementFrames is >= 0 and <= 1000
            && profile.MinDurationFrames >= 1 && profile.DedupToleranceFrames is >= 0 and <= 100
            && profile.MaxCoverageGapFrames is >= 0 and <= 1000
            && profile.MaxContinuationGapFrames is >= 0 and <= 1000
            && profile.MaxContinuationSpanFrames is >= 0 and <= 10000
            && profile.MaxFragmentContinuationGapFrames is >= 0 and <= 1000
            && profile.MaxContinuationFragmentFrames is >= 0 and <= 10000
            && (profile.MaxContinuationGapFrames == 0 || profile.MaxContinuationSpanFrames >= profile.MinDurationFrames
                && profile.MaxContinuationSpanFrames > profile.MaxContinuationGapFrames)
            && (profile.MaxFragmentContinuationGapFrames == 0 && profile.MaxContinuationFragmentFrames == 0
                || profile.MaxContinuationGapFrames > 0
                && profile.MaxFragmentContinuationGapFrames > profile.MaxContinuationGapFrames
                && profile.MaxContinuationFragmentFrames >= profile.MinDurationFrames
                && profile.MaxContinuationFragmentFrames <= profile.MaxContinuationSpanFrames
                && profile.MaxContinuationSpanFrames > profile.MaxFragmentContinuationGapFrames)
            && (continuationSupported || profile.MaxContinuationGapFrames == 0 && profile.MaxContinuationSpanFrames == 0
                && profile.MaxFragmentContinuationGapFrames == 0 && profile.MaxContinuationFragmentFrames == 0
                && profile.InternalStartThreshold is null && profile.InternalEndThreshold is null);
    }
}
