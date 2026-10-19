using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests.Presets;

public sealed class SubtitleStyleOverwriteTests
{
    [Fact]
    public async Task OverwritePreservesCurrentIdentityNameTimingOrderAndInstalledFonts()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var originalTiming = new TimingPostProcessorOptions { LeadInMilliseconds = 20 };
        var target = new SubtitleStylePreset(Guid.NewGuid(), "Original name", new() { FontFamily = "Installed" }, originalTiming);
        var family = Family("Installed", 1);
        await library.UpsertAsync(new CapturedSubtitleStyle(target, [family]));
        var other = new SubtitleStylePreset(Guid.NewGuid(), "Other style", new());
        await library.UpsertAsync(other);
        var captured = new CapturedSubtitleStyle(target with
        {
            Style = new() { FontFamily = "Source Han Sans SC", FontSize = 73, Bold = true }, TimingPostProcessor = null
        }, []);
        var currentTiming = originalTiming with { LeadInMilliseconds = 120 };
        await library.UpsertAsync(target with { Name = "Renamed while capturing", TimingPostProcessor = currentTiming });
        var installed = repository.Snapshot.FontFamilies;

        await library.OverwriteAsync(captured);

        Assert.Equal(new[] { target.Id, other.Id }, library.Snapshot.Presets.Select(preset => preset.Id));
        var overwritten = library.Snapshot.Presets[0];
        Assert.Equal(target.Id, overwritten.Id);
        Assert.Equal("Renamed while capturing", overwritten.Name);
        Assert.Equal(currentTiming, overwritten.TimingPostProcessor);
        Assert.Equal(captured.Preset.Style, overwritten.Style);
        Assert.Same(other, library.Snapshot.Presets[1]);
        Assert.True(installed.Equals(repository.Snapshot.FontFamilies));
        Assert.Same(family, Assert.Single(repository.Snapshot.FontFamilies));
    }

    [Fact]
    public async Task DeletedTargetCannotBeRecreatedOrInstallCapturedFontFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var target = new SubtitleStylePreset(Guid.NewGuid(), "Deleted target", new());
        var other = new SubtitleStylePreset(Guid.NewGuid(), "Retained", new());
        await library.UpsertAsync(target);
        await library.UpsertAsync(other);
        var captured = new CapturedSubtitleStyle(target with { Style = new() { FontFamily = "Incoming" } }, [Family("Incoming", 1, 2)]);
        await library.RemoveAsync(target.Id);
        var before = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.OverwriteAsync(captured));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Equal(other, Assert.Single(library.Snapshot.Presets));
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverwriteCommitsStyleAndWholeFontFamilyTogetherOrRollsBothBack(bool failCommit)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var original = Family("Shared", 1);
        var incoming = Family("Shared", 2, 3);
        var target = new SubtitleStylePreset(Guid.NewGuid(), "Target", new() { FontFamily = "Shared", FontSize = 31 },
            new TimingPostProcessorOptions { LeadOutMilliseconds = 220 });
        await library.UpsertAsync(new CapturedSubtitleStyle(target, [original]));
        var before = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var captured = new CapturedSubtitleStyle(target with { Name = "Ignored name", Style = target.Style with { FontSize = 73 } }, [incoming]);
        if (failCommit)
        {
            var backup = Path.Combine(directory.Path, "original-manifest.json");
            File.Move(repository.ManifestPath, backup);
            Directory.CreateDirectory(repository.ManifestPath);

            await Assert.ThrowsAsync<IOException>(() => library.OverwriteAsync(captured));

            Assert.Same(before, repository.Snapshot);
            Assert.Equal(target, Assert.Single(library.Snapshot.Presets));
            Assert.Same(original, Assert.Single(repository.Snapshot.FontFamilies));
            Assert.Equal(manifest, await File.ReadAllBytesAsync(backup));
            Assert.Single(Directory.GetFiles(repository.FontDirectoryPath, "*.font"));
            Assert.True(File.Exists(Path.Combine(repository.FontDirectoryPath, original.Files[0].Sha256 + ".font")));
            Assert.All(incoming.Files, font => Assert.False(File.Exists(Path.Combine(repository.FontDirectoryPath, font.Sha256 + ".font"))));
        }
        else
        {
            await library.OverwriteAsync(captured);

            Assert.NotEqual(before.Revision, repository.Snapshot.Revision);
            Assert.Equal(target with { Style = captured.Preset.Style }, Assert.Single(library.Snapshot.Presets));
            Assert.Equal(incoming.Files.Select(font => font.Sha256), Assert.Single(repository.Snapshot.FontFamilies).Files.Select(font => font.Sha256));
            Assert.Equal(2, Directory.GetFiles(repository.FontDirectoryPath, "*.font").Length);
            Assert.False(File.Exists(Path.Combine(repository.FontDirectoryPath, original.Files[0].Sha256 + ".font")));
        }
        Assert.Empty(Directory.GetDirectories(repository.DirectoryPath, ".staging-*"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DefaultOrMismatchedFamiliesRejectOverwriteWithoutChangingAnything(bool useDefault)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        using var library = new SubtitleStylePresetLibrary(repository);
        var target = new SubtitleStylePreset(Guid.NewGuid(), "Target", new() { FontFamily = "Expected" });
        await library.UpsertAsync(target);
        var before = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var captured = new CapturedSubtitleStyle(target, useDefault ? default : [Family("Other", 1)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.OverwriteAsync(captured));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
    }

    private static FontFamilyBundle Family(string name, params byte[] values)
    {
        return new(name, values.Select(value =>
        {
            var data = ImmutableArray.Create(value);
            return new EmbeddedSubtitleFont($"font-{value}.ttf", Convert.ToHexStringLower(SHA256.HashData(data.AsSpan())), data);
        }).ToImmutableArray());
    }
}
