using System.Text.Json.Nodes;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriGenerationSnapPackageTests
{
    [Fact]
    public async Task CompleteV3PackageLoadsThreeVerifiedGraphsAndOneSharedEncoderFrontend()
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();

        var package = await KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default);

        Assert.Equal(3, package.Manifest.SchemaVersion);
        Assert.Equal("whisper-small-generation-snap-v1", package.Manifest.Architecture);
        Assert.Equal("snap-model.onnx", package.Manifest.SnapModelFile);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryPath, "snap-model.onnx")), package.SnapModel);
        Assert.NotNull(package.EncoderModel);
        Assert.Equal(494, package.EncoderFrontend!.ContextStartFrame);
        Assert.Equal(8, Directory.GetFiles(fixture.DirectoryPath).Length);
    }

    [Theory]
    [InlineData("snapModelFile", "null")]
    [InlineData("snapModelSha256", "null")]
    [InlineData("snapModelFile", "\"\"")]
    [InlineData("snapModelSha256", "\"\"")]
    [InlineData("snapModelSha256", "\"abcd\"")]
    [InlineData("architecture", "\"whisper-small-local-fusion-v1\"")]
    [InlineData("architecture", "null")]
    [InlineData("contextInputName", "\"other\"")]
    [InlineData("encoderFile", "null")]
    [InlineData("encoderFrontendSha256", "null")]
    [InlineData("encoderLicenseFile", "null")]
    [InlineData("featureCount", "null")]
    [InlineData("sampleRate", "null")]
    [InlineData("schemaVersion", "4")]
    public async Task PartialOrUnknownV3ContractsFailBeforeNativeLoading(string field, string value)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        fixture.Manifest[field] = JsonNode.Parse(value);
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
    }

    [Theory]
    [InlineData("../snap-model.onnx")]
    [InlineData("nested/snap-model.onnx")]
    [InlineData("..\\snap-model.onnx")]
    [InlineData("/tmp/snap-model.onnx")]
    [InlineData("C:snap-model.onnx")]
    [InlineData("SNAP-MODEL.ONNX")]
    [InlineData("other.onnx")]
    [InlineData("model.onnx")]
    [InlineData("manifest.json")]
    public async Task SnapGraphRequiresItsCanonicalDirectChildFilename(string filename)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        fixture.Manifest["snapModelFile"] = filename;
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData("modelFile", "snap-model.onnx")]
    [InlineData("encoderFile", "snap-model.onnx")]
    [InlineData("modelFile", "SNAP-MODEL.ONNX")]
    [InlineData("encoderFile", "MODEL.ONNX")]
    [InlineData("frontendFile", "manifest.json")]
    public async Task PayloadAliasesAreRejectedWithCaseInsensitiveIdentityOnBothPlatforms(string field, string filename)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        fixture.Manifest[field] = filename;
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData(1, "snapModelFile", false)]
    [InlineData(1, "snapModelSha256", false)]
    [InlineData(1, "both", false)]
    [InlineData(1, "snapModelFile", true)]
    [InlineData(1, "snapModelSha256", true)]
    [InlineData(1, "both", true)]
    [InlineData(2, "snapModelFile", false)]
    [InlineData(2, "snapModelSha256", false)]
    [InlineData(2, "both", false)]
    [InlineData(2, "snapModelFile", true)]
    [InlineData(2, "snapModelSha256", true)]
    [InlineData(2, "both", true)]
    public async Task LegacySchemaCannotSilentlyIgnoreSnapMetadata(int schemaVersion, string fields, bool explicitNull)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        var filename = fixture.Manifest["snapModelFile"]!.GetValue<string>();
        var hash = fixture.Manifest["snapModelSha256"]!.GetValue<string>();
        if (schemaVersion == 1)
        {
            fixture.UseLegacyGenerationOnly();
        }
        else
        {
            fixture.UseGenerationOnly();
        }
        if (fields is "snapModelFile" or "both")
        {
            fixture.Manifest["snapModelFile"] = explicitNull ? null : JsonValue.Create(filename);
        }
        if (fields is "snapModelSha256" or "both")
        {
            fixture.Manifest["snapModelSha256"] = explicitNull ? null : JsonValue.Create(hash);
        }
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData(1, "SnapModelFile")]
    [InlineData(1, "SNAPMODELSHA256")]
    [InlineData(2, "SnapModelFile")]
    [InlineData(2, "SNAPMODELSHA256")]
    public async Task LegacySchemaCannotIgnoreAnExplicitNullSnapPropertyWithAnotherCase(int schemaVersion, string field)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        if (schemaVersion == 1)
        {
            fixture.UseLegacyGenerationOnly();
        }
        else
        {
            fixture.UseGenerationOnly();
        }
        fixture.Manifest[field] = null;
        fixture.SaveManifest();

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData(1, "modelId")]
    [InlineData(1, "MODELID")]
    [InlineData(2, "modelId")]
    [InlineData(2, "MODELID")]
    [InlineData(3, "modelId")]
    [InlineData(3, "MODELID")]
    [InlineData(3, "snapModelFile")]
    [InlineData(3, "SNAPMODELFILE")]
    [InlineData(3, "snapModelSha256")]
    [InlineData(3, "SNAPMODELSHA256")]
    [InlineData(3, "fixtureMetadata")]
    [InlineData(3, "FIXTUREMETADATA")]
    public async Task DuplicateOrCaseAliasedTopLevelPropertiesCannotShadowTheManifest(int schemaVersion, string field)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        if (schemaVersion == 1)
        {
            fixture.UseLegacyGenerationOnly();
        }
        else if (schemaVersion == 2)
        {
            fixture.UseGenerationOnly();
        }
        fixture.Manifest["fixtureMetadata"] = "synthetic";
        var original = fixture.Manifest.First(property => property.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        var json = fixture.Manifest.ToJsonString();
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "manifest.json"),
            json[..^1] + ",\"" + field + "\":" + original.Value!.ToJsonString() + "}");

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"manifest\"")]
    [InlineData("{")]
    public async Task InvalidManifestRootOrJsonRemainsAnExplicitPackageError(string json)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "manifest.json"), json);

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(fixture.DirectoryPath, default));
    }

    [Fact]
    public async Task MissingCorruptAndLinkedSnapFilesCannotLoadNativeSessions()
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        var path = Path.Combine(fixture.DirectoryPath, "snap-model.onnx");
        var bytes = await File.ReadAllBytesAsync(path);
        await File.AppendAllTextAsync(path, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
        File.Delete(path);
        await Assert.ThrowsAsync<FileNotFoundException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
        var target = path + ".original";
        await File.WriteAllBytesAsync(target, bytes);
        File.CreateSymbolicLink(path, target);

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
    }
}
