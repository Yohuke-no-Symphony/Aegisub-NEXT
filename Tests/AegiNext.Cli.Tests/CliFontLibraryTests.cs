using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Spectre.Console.Testing;

namespace AegiNext.Cli.Tests;

public sealed class CliFontLibraryTests
{
    [Fact]
    public async Task MissingManifestIgnoresUnpublishedFontFilesAndDoesNotCreateDirectories()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        var library = new CliFontLibrary(root);
        Assert.Empty(await library.LoadFamiliesAsync(CancellationToken.None));
        Assert.False(Directory.Exists(root));
        Directory.CreateDirectory(Path.Combine(root, "fonts"));
        await File.WriteAllBytesAsync(Path.Combine(root, "fonts", "unpublished.ttf"), [1, 2, 3]);

        Assert.Empty(await library.LoadFamiliesAsync(CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(root, "effect-library")));
    }

    [Fact]
    public async Task LegacyManifestIsIgnoredWithoutArchivingOrChangingItsFiles()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        var libraryPath = Path.Combine(root, "effect-library");
        Directory.CreateDirectory(Path.Combine(libraryPath, "styles"));
        var manifest = Path.Combine(libraryPath, "manifest.json");
        const string LEGACY_MANIFEST = "{\"version\":1,\"revision\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"styles\":[],\"effects\":[]}";
        await File.WriteAllTextAsync(manifest, LEGACY_MANIFEST);
        var style = Path.Combine(libraryPath, "styles", "legacy.aegistyles");
        await File.WriteAllTextAsync(style, "original style bytes");

        Assert.Empty(await new CliFontLibrary(root).LoadFamiliesAsync(CancellationToken.None));

        Assert.Equal(LEGACY_MANIFEST, await File.ReadAllTextAsync(manifest));
        Assert.Equal("original style bytes", await File.ReadAllTextAsync(style));
        Assert.Single(Directory.GetDirectories(root));
    }

    [Fact]
    public async Task OnlyManifestFamiliesAreFrozenIntoTheVideoExportRequest()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        var families = await InstallAsync(root);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: directory.File("source.mp4"));
        var project = new ProjectDocument { Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero) };
        var input = directory.File("project.aeginext");
        await ProjectStore.CreateAsync(project, input);
        await File.WriteAllBytesAsync(Path.Combine(root, "fonts", "unpublished.ttf"), [1, 2, 3]);
        var console = new TestConsole();
        var service = new RecordingVideoExportService();
        var runner = new ExportRunner(new(console, false), service, fontLibrary: new(root));

        Assert.Equal(0, await runner.RunAsync(new() { Input = input }, CancellationToken.None));

        var actual = Assert.Single(service.Request!.FontFamilies);
        Assert.Equal(families[0].FamilyName, actual.FamilyName);
        Assert.Equal(families[0].Files[0].Data.AsSpan().ToArray(), Assert.Single(actual.Files).Data.AsSpan().ToArray());
        Assert.True(File.Exists(Path.Combine(root, "fonts", "unpublished.ttf")));
    }

    [Fact]
    public async Task AssCatalogLoadsThePublishedPrivateFamilyAndIgnoresOrphans()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        using (var repository = new EffectLibraryRepository(Path.Combine(root, "effect-library"), Path.Combine(root, "fonts")))
        {
            await repository.LoadAsync();
            await repository.UpdateAsync(repository.Snapshot.Revision,
                current => current with { FontFamilies = [new("Noto Sans", [font])] });
        }
        await File.WriteAllBytesAsync(Path.Combine(root, "fonts", "unpublished.ttf"), [1, 2, 3]);

        var catalog = await new CliFontLibrary(root).LoadCatalogAsync(CancellationToken.None);

        Assert.Equal(font.Sha256, Assert.Single(Assert.Single(catalog.ApplicationFonts).Files).Sha256);
        Assert.Contains(catalog.Faces, face => face.FamilyName == "Noto Sans" && face.Variant.Weight == 900);
    }

    [Fact]
    public async Task SrtConversionDoesNotRequireAnAvailableFontLibrary()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        Directory.CreateDirectory(Path.Combine(root, "effect-library"));
        await File.WriteAllTextAsync(Path.Combine(root, "effect-library", "manifest.json"), "invalid");
        var input = await directory.InputAsync();

        var result = await new SubtitleConversionService(new(root)).ConvertAsync(input, "srt", CancellationToken.None);

        Assert.Contains("你好 😀", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssConversionRejectsInvalidActiveFontsWithoutCreatingOutput()
    {
        using var directory = new CliTestDirectory();
        var root = directory.File("preferences");
        Directory.CreateDirectory(Path.Combine(root, "effect-library"));
        await File.WriteAllTextAsync(Path.Combine(root, "effect-library", "manifest.json"), "{\"version\":2}");
        var input = await directory.InputAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => new SubtitleConversionService(new(root))
            .ConvertAsync(input, "ass", CancellationToken.None));
    }

    private static async Task<ImmutableArray<FontFamilyBundle>> InstallAsync(string root)
    {
        byte[] bytes = [1, 2, 3, 4];
        var font = new EmbeddedSubtitleFont("family.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        ImmutableArray<FontFamilyBundle> families = [new("Private Family", [font])];
        using var repository = new EffectLibraryRepository(Path.Combine(root, "effect-library"), Path.Combine(root, "fonts"));
        await repository.LoadAsync();
        await repository.UpdateAsync(repository.Snapshot.Revision, current => current with { FontFamilies = families });
        return families;
    }
}
