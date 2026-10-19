using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests.Presets;

public sealed class SubtitleStylePresetTests
{
    [Fact]
    public async Task MissingSystemFontCanBeCapturedAndSavedButExportFailsWithoutCreatingFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var source = new UnavailableFontFamilySource();
        var style = new SubtitleStyle
        {
            FontFamily = "Source Han Sans SC", FontVariant = new() { Name = "Heavy", PostScriptName = "SourceHanSansSC-Heavy", Weight = 900 },
            Bold = true
        };

        var captured = await SubtitleStylePresetService.CaptureAsync("Current appearance", style, new(), directory.Path, source);

        Assert.Equal(style, captured.Preset.Style);
        Assert.False(captured.FontFamilies.IsDefault);
        Assert.Empty(captured.FontFamilies);
        Assert.Equal(0, source.CaptureCount);
        await library.UpsertAsync(captured);
        var beforeExport = repository.Snapshot;
        var transfer = new EffectLibraryTransferService(repository, source, new TestFontFamilyValidator());
        var outputDirectory = Path.Combine(directory.Path, "export");
        var outputPath = Path.Combine(outputDirectory, "missing.aegistyles");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => transfer.ExportStyleAsync(captured.Preset, outputPath));

        Assert.Contains(style.FontFamily, error.Message, StringComparison.Ordinal);
        Assert.Equal(1, source.CaptureCount);
        Assert.False(File.Exists(outputPath));
        Assert.False(Directory.Exists(outputDirectory));
        Assert.Same(beforeExport, repository.Snapshot);
        Assert.Equal(style, Assert.Single(library.Snapshot.Presets).Style);
        Assert.Empty(repository.Snapshot.FontFamilies);
    }

    [Fact]
    public async Task SavingCapturedMetadataWithAnEmptyFontCollectionPreservesInstalledFamilies()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var retained = SystemPreset("Installed style");
        var family = TestStylePackageStore.Family(retained.Style.FontFamily);
        await library.UpsertAsync(new CapturedSubtitleStyle(retained, [family]));
        var installed = repository.Snapshot.FontFamilies;
        var captured = new CapturedSubtitleStyle(SystemPreset("Metadata only") with { Style = new() { FontFamily = "Missing family" } }, []);

        await library.UpsertAsync(captured);

        Assert.True(installed.Equals(repository.Snapshot.FontFamilies));
        Assert.Same(family, Assert.Single(repository.Snapshot.FontFamilies));
        Assert.Contains(captured.Preset, library.Snapshot.Presets);
        Assert.True(File.Exists(Path.Combine(repository.FontDirectoryPath, family.Files[0].Sha256 + ".font")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DefaultOrMismatchedCapturedFontCollectionsAreRejectedWithoutChangingTheLibrary(bool useDefault)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        await library.UpsertAsync(SystemPreset("Retained"));
        var before = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var preset = SystemPreset("Invalid capture");
        var captured = new CapturedSubtitleStyle(preset, useDefault ? default : [TestStylePackageStore.Family("Other family")]);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(captured));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
    }

    [Fact]
    public async Task PrepareUsesInstalledFamilyWithoutAddingProjectAssets()
    {
        using var directory = new TemporaryProjectDirectory();
        var preset = SystemPreset("Future subtitles");
        var project = new ProjectDocument();
        var prepared = await SubtitleStylePresetService.PrepareAsync(preset, project, directory.Path);
        Assert.Same(project, prepared.Project);
        Assert.Null(prepared.Style.FontAssetId);
        Assert.Equal(preset.Style, prepared.Style);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "assets")));
    }

    [Fact]
    public async Task CapturePackageInstallAndApplyKeepFamilyAndProjectUndoIndependent()
    {
        using var source = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var bytes = new byte[] { 0, 1, 0, 0, 11, 22, 33, 44 };
        var path = Path.Combine(source.Path, "font.ttf");
        await File.WriteAllBytesAsync(path, bytes);
        var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.FONT, source.Path);
        var project = new ProjectDocument { Assets = [asset] };
        var font = new EmbeddedSubtitleFont("font.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.ToImmutableArray());
        var siblingBytes = ImmutableArray.Create<byte>(9, 8, 7);
        var sibling = new EmbeddedSubtitleFont("font-bold.ttf", Convert.ToHexStringLower(SHA256.HashData(siblingBytes.AsSpan())), siblingBytes);
        var family = new FontFamilyBundle("Portable", [font, sibling]);
        var fontSource = new TestFontFamilySource([family]);
        var originalStyle = new SubtitleStyle { FontFamily = family.FamilyName, FontAssetId = asset.Id, FontSize = 42 };
        var captured = await SubtitleStylePresetService.CaptureAsync("Shared", originalStyle, project, source.Path,
            fontSource);
        Assert.Null(captured.Preset.Style.FontAssetId);
        Assert.Equal(1, fontSource.CaptureCount);
        var primary = Assert.IsType<EmbeddedSubtitleFont>(fontSource.LastPrimaryFont);
        Assert.Equal(Path.GetFileName(ProjectAssetLocation.Resolve(asset, source.Path)), primary.FileName);
        Assert.Equal(font.Sha256, primary.Sha256);
        Assert.Equal(bytes, primary.Data);
        Assert.Equal(new[] { font.Sha256, sibling.Sha256 }, Assert.Single(captured.FontFamilies).Files.Select(file => file.Sha256));
        var packagePath = Path.Combine(source.Path, "shared.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = new() { Presets = [captured.Preset] }, FontFamilies = captured.FontFamilies }, packagePath);
        using var repository = new EffectLibraryRepository(Path.Combine(destination.Path, "library"), Path.Combine(destination.Path, "fonts"));
        using var library = new SubtitleStylePresetLibrary(repository);
        await library.ImportAsync(packagePath);
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(1), "Keep text");
        var before = editor.Snapshot;
        var applied = await SubtitleStylePresetService.ApplyAsync(Assert.Single(library.Snapshot.Presets), before, destination.Path, [id]);
        Assert.Empty(applied.Assets);
        Assert.Equal(originalStyle with { FontAssetId = null }, Assert.Single(applied.Subtitles).Style);
        Assert.Equal("Keep text", Assert.Single(applied.Subtitles).Text);
        editor.Apply("Apply shared style", _ => applied);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
        Assert.Single(repository.Snapshot.FontFamilies);
    }

    [Fact]
    public async Task MissingOrTamperedProjectFontFailsBeforeFamilyCapture()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "font.ttf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.FONT, directory.Path);
        var project = new ProjectDocument { Assets = [asset] };
        var style = new SubtitleStyle { FontAssetId = asset.Id };
        await File.WriteAllBytesAsync(ProjectAssetLocation.Resolve(asset, directory.Path), [9, 8, 7]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.CaptureAsync("Bad", style, project, directory.Path, new TestFontFamilySource()));
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.CaptureAsync("Missing", style with { FontAssetId = Guid.NewGuid() }, project, directory.Path, new TestFontFamilySource()));
    }

    [Fact]
    public async Task FailedOrCancelledLibraryChangesKeepManifestAndSnapshot()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        await library.UpsertAsync(SystemPreset("Keep"));
        var before = repository.Snapshot;
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);
        var invalid = Path.Combine(directory.Path, "old.aegistyles");
        await File.WriteAllTextAsync(invalid, "{\"version\":7,\"presets\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(invalid));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.UpsertAsync(SystemPreset("Cancelled"), cancellation.Token));
        Assert.Same(before, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
    }

    [Fact]
    public async Task ConcurrentEditsSubsetExportRemoveAndReopenPreserveOtherContent()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "library");
        using var repository = new EffectLibraryRepository(root);
        using var library = new SubtitleStylePresetLibrary(repository);
        var first = SystemPreset("First");
        var second = SystemPreset("Second");
        var family = TestStylePackageStore.Family(first.Style.FontFamily);
        await Task.WhenAll(library.UpsertAsync(new CapturedSubtitleStyle(first, [family])), library.UpsertAsync(second));
        await library.UpsertAsync(first with { Name = "Renamed", Style = first.Style with { FontSize = 99 } });
        var output = Path.Combine(directory.Path, "selected.aegistyles");
        await library.ExportAsync(output, [first.Id]);
        Assert.Equal(first.Id, Assert.Single((await SubtitleStylePresetStore.LoadAsync(output)).Styles.Presets).Id);
        await library.RemoveAsync(second.Id);
        using var reopened = new EffectLibraryRepository(root);
        await reopened.LoadAsync();
        Assert.Equal("Renamed", Assert.Single(reopened.Snapshot.Styles.Presets).Name);
        Assert.Single(reopened.Snapshot.FontFamilies);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(output));
    }

    [Theory]
    [InlineData("\"version\": {version}", "\"version\": 99")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"version\": {version},")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"unknown\": true,")]
    [InlineData("\"fontSize\": 64,", "\"fontSize\": 0,")]
    [InlineData("\"fontSize\": 64,", "")]
    [InlineData("\"fontSize\": 64,", "\"fontSize\": 64, \"fontSize\": 65,")]
    [InlineData("\"alignment\": \"BOTTOM_CENTER\"", "\"alignment\": 7")]
    public void MetadataRejectsUnsupportedDuplicateUnknownMissingAndInvalidFields(string old, string replacement)
    {
        var json = Encoding.UTF8.GetString(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [SystemPreset("Valid")] }));
        var version = SubtitleStylePresetCollection.CURRENT_VERSION.ToString(CultureInfo.InvariantCulture);
        old = old.Replace("{version}", version, StringComparison.Ordinal);
        replacement = replacement.Replace("{version}", version, StringComparison.Ordinal);
        Assert.Contains(old, json, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(json.Replace(old, replacement, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task OversizedPackageIsRejectedBeforeReadingAndUnknownSubtitleCannotCreateAssets()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "oversized.aegistyles");
        await using (var file = File.Create(path))
        {
            file.SetLength(SubtitleStylePresetStore.MAXIMUM_FILE_BYTES + 1L);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetStore.LoadAsync(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.ApplyAsync(SystemPreset("Style"), new(), directory.Path, [Guid.NewGuid()]));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "assets")));
    }

    [Fact]
    public async Task InvalidEditManagedExportAndDisposalPreserveTheRepository()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var library = new SubtitleStylePresetLibrary(repository);
        await library.UpsertAsync(SystemPreset("First"));
        var before = repository.Snapshot;
        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(SystemPreset("FIRST")));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(Path.Combine(repository.DirectoryPath, "bad.aegistyles"), []));
        Assert.Same(before, repository.Snapshot);
        library.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.LoadAsync());
        await repository.LoadAsync();
    }

    [Fact]
    public async Task ApplyingOneSubtitleLeavesOtherStylesAndProjectResourcesUntouched()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(1), "One");
        var second = editor.AddSubtitle(new(1), new(2), "Two");
        var preset = SystemPreset("New") with { Style = new() { FontFamily = "New Family", Bold = true } };
        var applied = await SubtitleStylePresetService.ApplyAsync(preset, editor.Snapshot, directory.Path, [first]);
        Assert.Equal(preset.Style, applied.Subtitles.Single(line => line.Id == first).Style);
        Assert.Equal(editor.Snapshot.Subtitles.Single(line => line.Id == second).Style, applied.Subtitles.Single(line => line.Id == second).Style);
        Assert.Empty(applied.Assets);
    }

    private static SubtitleStylePreset SystemPreset(string name) => new(Guid.NewGuid(), name, new());
}
