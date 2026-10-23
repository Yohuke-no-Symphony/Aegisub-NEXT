using System.Text.Json.Nodes;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriContextualPackageTests
{
    [Fact]
    public async Task LegacyV1ManifestWithoutEncoderOrOptionalMetadataKeepsItsOriginalContract()
    {
        using var fixture = new KoumoriV2TestPackage();
        fixture.Manifest["schemaVersion"] = 1;
        foreach (var key in fixture.Manifest.Select(item => item.Key).Where(key => key.StartsWith("encoder", StringComparison.Ordinal)
                     || key is "architecture" or "contextInputName" or "featureCount" or "sampleRate").ToArray())
        {
            fixture.Manifest.Remove(key);
        }
        fixture.SaveManifest();

        var package = await KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default);

        Assert.Equal(1, package.Manifest.SchemaVersion);
        Assert.Null(package.EncoderModel);
        Assert.Null(package.EncoderFrontend);
    }

    [Fact]
    public async Task CompleteV2PackageLoadsBothVerifiedModelsAndItsEncoderContract()
    {
        using var fixture = new KoumoriV2TestPackage();

        var package = await KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default);

        Assert.Equal(2, package.Manifest.SchemaVersion);
        Assert.Equal("whisper-small-local-fusion-v1", package.Manifest.Architecture);
        Assert.Equal(new byte[] { 0 }, package.EncoderModel);
        Assert.Equal(494, package.EncoderFrontend!.ContextStartFrame);
        Assert.Equal(512, package.EncoderFrontend.ContextFrames);
    }

    [Theory]
    [InlineData("encoder.onnx")]
    [InlineData("encoder-frontend.json")]
    [InlineData("model.onnx")]
    [InlineData("WHISPER-LICENSE.txt")]
    public async Task TamperedV2FilesAreRejectedBeforeLoadingNativeSessions(string filename)
    {
        using var fixture = new KoumoriV2TestPackage();
        await File.AppendAllTextAsync(Path.Combine(fixture.DirectoryPath, filename), "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
    }

    [Theory]
    [InlineData("encoderFile", "../encoder.onnx")]
    [InlineData("encoderFile", "/tmp/encoder.onnx")]
    [InlineData("encoderFile", "C:encoder.onnx")]
    [InlineData("encoderFrontendFile", "nested/encoder-frontend.json")]
    [InlineData("encoderFrontendFile", "..\\encoder-frontend.json")]
    public async Task EncoderFileReferencesCannotEscapeThePackage(string field, string filename)
    {
        using var fixture = new KoumoriV2TestPackage();
        fixture.Manifest[field] = filename;
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData("architecture", "\"unrelated-model\"")]
    [InlineData("contextInputName", "\"another-context\"")]
    [InlineData("encoderInputName", "\"audio\"")]
    [InlineData("encoderOutputName", "null")]
    [InlineData("encoderFrontendSha256", "null")]
    [InlineData("encoderLicenseFile", "null")]
    [InlineData("encoderLicenseSha256", "null")]
    [InlineData("schemaVersion", "3")]
    [InlineData("featureCount", "81")]
    [InlineData("sampleRate", "48000")]
    [InlineData("featureCount", "null")]
    public async Task IncompleteOrUnknownV2ManifestContractsAreRejected(string field, string value)
    {
        using var fixture = new KoumoriV2TestPackage();
        fixture.Manifest[field] = JsonNode.Parse(value);
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("algorithm", "\"whisper-other-v1\"")]
    [InlineData("fftSize", "512")]
    [InlineData("leftContextSamples", "158081")]
    [InlineData("rightContextSamples", "0")]
    [InlineData("contextStartFrame", "493")]
    [InlineData("contextFrames", "511")]
    [InlineData("stftPadding", "\"constant\"")]
    [InlineData("stftCenter", "false")]
    [InlineData("dropLastStftFrame", "false")]
    [InlineData("melLogFloor", "0")]
    [InlineData("dynamicRange", "7")]
    [InlineData("hannWindow", "[]")]
    [InlineData("melFilters", "[]")]
    public async Task EncoderFrontendAlignmentAndNumericContractsAreStrict(string field, string value)
    {
        using var fixture = new KoumoriV2TestPackage();
        fixture.ChangeEncoderDefinition(field, JsonNode.Parse(value));

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Fact]
    public async Task MissingEncoderFailsExplicitlyAndCancelledPackageLoadingDoesNotCreateSessions()
    {
        using var fixture = new KoumoriV2TestPackage();
        File.Delete(Path.Combine(fixture.DirectoryPath, "encoder.onnx"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath, cancellation.Token));
    }

    [Fact]
    public async Task EncoderAndManifestSymbolicLinksAreRejected()
    {
        foreach (var filename in new[] { "encoder.onnx", "manifest.json" })
        {
            using var fixture = new KoumoriV2TestPackage();
            var path = Path.Combine(fixture.DirectoryPath, filename);
            var target = path + ".original";
            File.Move(path, target);
            File.CreateSymbolicLink(path, target);

            await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
        }
    }
}
