using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests.Presets;

public sealed class FontOptionalTransferTests
{
    [Theory]
    [InlineData("style", false)]
    [InlineData("style", true)]
    [InlineData("styles", false)]
    [InlineData("styles", true)]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("package", false)]
    [InlineData("package", true)]
    [InlineData("all", false)]
    [InlineData("all", true)]
    [InlineData("frozen-all", false)]
    [InlineData("frozen-all", true)]
    [InlineData("library", false)]
    [InlineData("library", true)]
    [InlineData("library-style", false)]
    [InlineData("library-style", true)]
    public async Task MetadataOnlyExportPreservesFontIdentityAndSkipsAllFontOperations(string route, bool alreadyInstalled)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var preset = Preset("Missing system font");
        if (alreadyInstalled)
        {
            var installed = TestStylePackageStore.Family(preset.Style.FontFamily);
            await library.UpsertAsync(new CapturedSubtitleStyle(preset, [installed]));
        }
        else
        {
            await library.UpsertAsync(preset);
        }
        var source = new UnavailableFontFamilySource();
        var validator = new RejectingFontFamilyValidator();
        var service = new EffectLibraryTransferService(repository, source, validator);
        var path = await ExportAsync(route, service, library, repository.Snapshot, preset, directory.Path, false);
        var exported = await LoadAsync(path);

        Assert.False(exported.IncludesFonts);
        Assert.Equal(preset, Assert.Single(exported.Styles.Presets));
        Assert.Empty(exported.FontFamilies);
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, validator.ValidateCount);
        using var archive = new ZipArchive(File.OpenRead(path));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("fonts/", StringComparison.Ordinal));
        using var manifest = archive.GetEntry("manifest.json")!.Open();
        Assert.False(JsonNode.Parse(manifest)!["includesFonts"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("style")]
    [InlineData("styles")]
    [InlineData("independent")]
    [InlineData("package")]
    [InlineData("all")]
    [InlineData("frozen-all")]
    public async Task DefaultExportStillRequiresMissingFamilyAndLeavesNoOutput(string route)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var preset = Preset("Missing system font");
        await library.UpsertAsync(preset);
        var source = new UnavailableFontFamilySource();
        var validator = new RejectingFontFamilyValidator();
        var service = new EffectLibraryTransferService(repository, source, validator);

        await Assert.ThrowsAsync<InvalidDataException>(() => ExportAsync(route, service, library, repository.Snapshot,
            preset, directory.Path, null));

        Assert.Equal(1, source.CaptureCount);
        Assert.Equal(0, validator.ValidateCount);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "output")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitMetadataOnlyPackageRoundTripsOriginalFontAndVariant(bool stylesOnly)
    {
        var preset = Preset("Original");
        var bytes = Serialize(stylesOnly, preset, false, []);
        var package = Deserialize(stylesOnly, bytes);

        Assert.False(package.IncludesFonts);
        Assert.Equal(preset, Assert.Single(package.Styles.Presets));
        Assert.Empty(package.FontFamilies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OmittingFontFlagRetainsStrictCompleteFontSemantics(bool stylesOnly)
    {
        var preset = Preset("Complete");
        var family = TestStylePackageStore.Family(preset.Style.FontFamily);
        var bytes = Serialize(stylesOnly, preset, true, [family]);
        var omitted = Rewrite(bytes, manifest => manifest.Remove("includesFonts"));
        var package = Deserialize(stylesOnly, omitted);

        Assert.True(package.IncludesFonts);
        Assert.Equal(preset, Assert.Single(package.Styles.Presets));
        Assert.Equal(family.Files[0].Sha256, Assert.Single(package.FontFamilies).Files[0].Sha256);
    }

    [Theory]
    [InlineData(true, "missing-raw")]
    [InlineData(false, "missing-raw")]
    [InlineData(true, "missing-references")]
    [InlineData(false, "missing-references")]
    [InlineData(true, "false-with-fonts")]
    [InlineData(false, "false-with-fonts")]
    [InlineData(true, "implicit-metadata")]
    [InlineData(false, "implicit-metadata")]
    [InlineData(true, "unreferenced-raw")]
    [InlineData(false, "unreferenced-raw")]
    public void FontFlagNeverPermitsDamagedOrContradictoryPackages(bool stylesOnly, string mutation)
    {
        var preset = Preset("Strict");
        var bytes = Serialize(stylesOnly, preset, true, [TestStylePackageStore.Family(preset.Style.FontFamily)]);
        var malformed = Rewrite(bytes, manifest =>
        {
            if (mutation is "false-with-fonts" or "unreferenced-raw")
            {
                manifest["includesFonts"] = false;
            }
            if (mutation is "missing-references" or "implicit-metadata" or "unreferenced-raw")
            {
                manifest["fontFamilies"] = new JsonArray();
            }
            if (mutation == "implicit-metadata")
            {
                manifest.Remove("includesFonts");
            }
        }, mutation is "missing-raw" or "missing-references" or "implicit-metadata");

        Assert.Throws<InvalidDataException>(() => Deserialize(stylesOnly, malformed));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MetadataOnlyWriterRequiresAnExplicitlyEmptyFontArray(bool stylesOnly, bool useDefault)
    {
        var preset = Preset("Metadata");
        Assert.Throws<InvalidDataException>(() => Serialize(stylesOnly, preset, false,
            useDefault ? default : [TestStylePackageStore.Family(preset.Style.FontFamily)]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataOnlyImportPreservesInstalledFamiliesAndDoesNotRequireMissingFont(bool stylesOnly)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        await library.UpsertAsync(new CapturedSubtitleStyle(Preset("Installed") with { Style = new() { FontFamily = "Installed" } },
            [TestStylePackageStore.Family("Installed")]));
        var before = repository.Snapshot.FontFamilies;
        var preset = Preset("Incoming metadata");
        var path = Path.Combine(directory.Path, "input" + (stylesOnly ? ".aegistyles" : ".aegilibrary"));
        await File.WriteAllBytesAsync(path, Serialize(stylesOnly, preset, false, []));
        var source = new UnavailableFontFamilySource();
        var validator = new RejectingFontFamilyValidator();
        var service = new EffectLibraryTransferService(repository, source, validator);

        var plan = await service.AnalyzeImportAsync([path]);
        Assert.False(Assert.Single(plan.Rows).IncludesFonts);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Contains(preset, repository.Snapshot.Styles.Presets);
        Assert.True(before.Equals(repository.Snapshot.FontFamilies));
        Assert.Single(Directory.GetFiles(repository.FontDirectoryPath, "*.font"));
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, validator.ValidateCount);
        var repeated = await service.AnalyzeImportAsync([path]);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, Assert.Single(repeated.Rows).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataOnlyImportCannotBorrowAFontFromAnotherInput(bool metadataFirst)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var metadata = Preset("Metadata");
        var complete = metadata with { Id = Guid.NewGuid(), Name = "Complete" };
        var metadataPath = Path.Combine(directory.Path, "metadata.aegistyles");
        var completePath = Path.Combine(directory.Path, "complete.aegilibrary");
        await File.WriteAllBytesAsync(metadataPath, Serialize(true, metadata, false, []));
        await File.WriteAllBytesAsync(completePath, Serialize(false, complete, true,
            [TestStylePackageStore.Family(metadata.Style.FontFamily)]));
        var service = new EffectLibraryTransferService(repository, new UnavailableFontFamilySource(), new TestFontFamilyValidator());
        var plan = await service.AnalyzeImportAsync(metadataFirst ? [metadataPath, completePath] : [completePath, metadataPath]);
        var metadataRow = plan.Rows.Single(row => row.Name == metadata.Name);
        var completeRow = plan.Rows.Single(row => row.Name == complete.Name);
        Assert.False(metadataRow.IncludesFonts);
        Assert.True(completeRow.IncludesFonts);
        Assert.Empty(plan.Entries[metadataRow.Index].FontFamilies);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>
        {
            [metadataRow.Index] = EffectLibraryImportDecision.IMPORT,
            [completeRow.Index] = EffectLibraryImportDecision.SKIP
        });

        Assert.Equal(metadata, Assert.Single(repository.Snapshot.Styles.Presets));
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
    }

    [Fact]
    public async Task SameMetadataWithAndWithoutFontsRemainDistinctInputChoices()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var preset = Preset("One identity");
        var metadataPath = Path.Combine(directory.Path, "metadata.aegistyles");
        var completePath = Path.Combine(directory.Path, "complete.aegistyles");
        await File.WriteAllBytesAsync(metadataPath, Serialize(true, preset, false, []));
        await File.WriteAllBytesAsync(completePath, Serialize(true, preset, true,
            [TestStylePackageStore.Family(preset.Style.FontFamily)]));
        var service = new EffectLibraryTransferService(repository, new UnavailableFontFamilySource(), new TestFontFamilyValidator());

        var plan = await service.AnalyzeImportAsync([completePath, metadataPath]);

        Assert.All(plan.Rows, row => Assert.Equal(EffectLibraryImportConflictReason.MULTIPLE_INPUT_VERSIONS, row.ConflictReason));
        Assert.True(plan.Rows[0].IncludesFonts);
        Assert.False(plan.Rows[1].IncludesFonts);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>
        {
            [0] = EffectLibraryImportDecision.SKIP, [1] = EffectLibraryImportDecision.IMPORT
        });
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.Equal(preset, Assert.Single(repository.Snapshot.Styles.Presets));
    }

    [Fact]
    public async Task DirectLibraryImportAcceptsMetadataOnlyFilesAndRetainsInstalledFontArray()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var installed = Preset("Installed");
        await library.UpsertAsync(new CapturedSubtitleStyle(installed, [TestStylePackageStore.Family(installed.Style.FontFamily)]));
        var families = repository.Snapshot.FontFamilies;
        var incoming = Preset("Metadata");
        var path = Path.Combine(directory.Path, "metadata.aegistyles");
        await File.WriteAllBytesAsync(path, Serialize(true, incoming, false, []));

        await library.ImportAsync([path]);

        Assert.Contains(incoming, library.Snapshot.Presets);
        Assert.True(families.Equals(repository.Snapshot.FontFamilies));
        Assert.Single(Directory.GetFiles(repository.FontDirectoryPath, "*.font"));
    }

    private static SubtitleStylePreset Preset(string name)
    {
        return new(Guid.NewGuid(), name, new()
        {
            FontFamily = "Source Han Sans SC",
            FontVariant = new() { Name = "Heavy", Weight = 900 },
            FontSize = 73,
            Bold = true
        }, new TimingPostProcessorOptions { LeadInMilliseconds = 120 });
    }

    private static async Task<string> ExportAsync(string route, EffectLibraryTransferService service,
        SubtitleStylePresetLibrary library, EffectLibrarySnapshot snapshot, SubtitleStylePreset preset, string directory, bool? includeFonts)
    {
        var output = Path.Combine(directory, "output");
        var path = Path.Combine(output, route is "package" or "all" or "frozen-all" ? "styles.aegilibrary" : "styles.aegistyles");
        switch (route)
        {
            case "style":
                if (includeFonts.HasValue)
                {
                    await service.ExportStyleAsync(preset, path, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportStyleAsync(preset, path);
                }
                break;
            case "styles":
                if (includeFonts.HasValue)
                {
                    await service.ExportStylesAsync(new() { Presets = [preset] }, path, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportStylesAsync(new() { Presets = [preset] }, path);
                }
                break;
            case "independent":
                if (includeFonts.HasValue)
                {
                    await service.ExportIndependentAsync([preset], [], output, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportIndependentAsync([preset], [], output);
                }
                path = service.GetIndependentDestinations([preset], [], output).Single();
                break;
            case "package":
                if (includeFonts.HasValue)
                {
                    await service.ExportPackageAsync([preset], [], path, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportPackageAsync([preset], [], path);
                }
                break;
            case "all":
                if (includeFonts.HasValue)
                {
                    await service.ExportAllPackageAsync(path, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportAllPackageAsync(path);
                }
                break;
            case "frozen-all":
                if (includeFonts.HasValue)
                {
                    await service.ExportAllPackageAsync(snapshot, path, includeFonts: includeFonts.Value);
                }
                else
                {
                    await service.ExportAllPackageAsync(snapshot, path);
                }
                break;
            case "library":
                await library.ExportAsync(path, includeFonts: includeFonts!.Value);
                break;
            default:
                await library.ExportAsync(preset, path, includeFonts: includeFonts!.Value);
                break;
        }
        return path;
    }

    private static byte[] Serialize(bool stylesOnly, SubtitleStylePreset preset, bool includesFonts, ImmutableArray<FontFamilyBundle> families)
    {
        return stylesOnly
            ? SubtitleStylePresetStore.Serialize(new() { Styles = new() { Presets = [preset] }, IncludesFonts = includesFonts, FontFamilies = families })
            : EffectLibraryPackageStore.Serialize(new() { Styles = new() { Presets = [preset] }, IncludesFonts = includesFonts, FontFamilies = families });
    }

    private static EffectLibraryPackage Deserialize(bool stylesOnly, byte[] bytes)
    {
        if (!stylesOnly)
        {
            return EffectLibraryPackageStore.Deserialize(bytes);
        }
        var package = SubtitleStylePresetStore.Deserialize(bytes);
        return new() { Styles = package.Styles, IncludesFonts = package.IncludesFonts, FontFamilies = package.FontFamilies };
    }

    private static async Task<EffectLibraryPackage> LoadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return Deserialize(Path.GetExtension(path) == ".aegistyles", bytes);
    }

    private static byte[] Rewrite(byte[] bytes, Action<JsonObject> changeManifest, bool removeFonts = false)
    {
        using var input = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var buffer = new MemoryStream();
        using (var output = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var entry in input.Entries)
            {
                if (removeFonts && entry.FullName.StartsWith("fonts/", StringComparison.Ordinal))
                {
                    continue;
                }
                using var source = entry.Open();
                using var destination = output.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "manifest.json")
                {
                    var manifest = JsonNode.Parse(source)!.AsObject();
                    changeManifest(manifest);
                    using var writer = new System.Text.Json.Utf8JsonWriter(destination);
                    manifest.WriteTo(writer);
                }
                else
                {
                    source.CopyTo(destination);
                }
            }
        }
        return buffer.ToArray();
    }
}
