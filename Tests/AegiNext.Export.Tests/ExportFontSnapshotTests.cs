using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class ExportFontSnapshotTests
{
    [Fact]
    public async Task SnapshotCopiesAndDeduplicatesRawFilesBeforeWorkerStarts()
    {
        using var directory = new ExportTestDirectory();
        var font = Font("family.ttc", [1, 2, 3, 4]);
        ImmutableArray<FontFamilyBundle> families = [new("First Family", [font]), new("Second Family", [font])];

        var index = await ExportFontSnapshot.WriteAsync(families, directory.Path, CancellationToken.None);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(directory.Path, "fonts")));
        var transmitted = JsonSerializer.Deserialize<ImmutableArray<ExportWorkerFontFamily>>(
            JsonSerializer.Serialize(index, ExportWire.Options), ExportWire.Options);
        var restored = await ExportFontSnapshot.ReadAsync(transmitted, directory.Path, CancellationToken.None);

        Assert.Equal(["First Family", "Second Family"], restored.Select(family => family.FamilyName));
        Assert.All(restored, family =>
        {
            var actual = Assert.Single(family.Files);
            Assert.Equal(font.FileName, actual.FileName);
            Assert.Equal(font.Sha256, actual.Sha256);
            Assert.Equal(font.Data.AsSpan().ToArray(), actual.Data.AsSpan().ToArray());
        });
    }

    [Fact]
    public async Task FrozenFilesDoNotFollowAnUpdatedLibrarySnapshot()
    {
        using var directory = new ExportTestDirectory();
        var original = Font("family.ttf", [1, 2, 3, 4]);
        var families = ImmutableArray.Create(new FontFamilyBundle("Shared Family", [original]));
        var index = await ExportFontSnapshot.WriteAsync(families, directory.Path, CancellationToken.None);
        families = [new("Shared Family", [Font("family.ttf", [5, 6, 7, 8])])];

        var restored = await ExportFontSnapshot.ReadAsync(index, directory.Path, CancellationToken.None);

        Assert.Equal(original.Data.AsSpan().ToArray(), Assert.Single(Assert.Single(restored).Files).Data.AsSpan().ToArray());
        Assert.NotEqual(families[0].Files[0].Sha256, restored[0].Files[0].Sha256);
    }

    [Fact]
    public async Task CorruptedOrOversizedFrozenFilesAreRejected()
    {
        using var directory = new ExportTestDirectory();
        var font = Font("family.otf", [1, 2, 3, 4]);
        var index = await ExportFontSnapshot.WriteAsync([new("Shared Family", [font])], directory.Path, CancellationToken.None);
        var path = Assert.Single(Directory.EnumerateFiles(Path.Combine(directory.Path, "fonts")));
        await File.WriteAllBytesAsync(path, [4, 3, 2, 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExportFontSnapshot.ReadAsync(index, directory.Path, CancellationToken.None));

        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(256L * 1024 * 1024 + 1);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => ExportFontSnapshot.ReadAsync(index, directory.Path, CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside", "family.ttf")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "family.ttf")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "../family.ttf")]
    public async Task InvalidIndexCannotReadOutsideSnapshotDirectory(string hash, string fileName)
    {
        using var directory = new ExportTestDirectory();
        ImmutableArray<ExportWorkerFontFamily> index = [new("Shared Family", [new(fileName, hash)])];

        await Assert.ThrowsAsync<InvalidDataException>(() => ExportFontSnapshot.ReadAsync(index, directory.Path, CancellationToken.None));
    }

    [Fact]
    public async Task EmptySnapshotDoesNotCreateFontDirectories()
    {
        using var directory = new ExportTestDirectory();

        Assert.Empty(await ExportFontSnapshot.WriteAsync([], directory.Path, CancellationToken.None));
        Assert.Empty(await ExportFontSnapshot.ReadAsync([], directory.Path, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "fonts")));
    }

    [Fact]
    public async Task InvalidSourceHashIsRejectedBeforeWriting()
    {
        using var directory = new ExportTestDirectory();
        var font = Font("family.ttf", [1, 2, 3, 4]) with { Sha256 = new('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => ExportFontSnapshot.WriteAsync([new("Shared Family", [font])], directory.Path, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "fonts")));
    }

    private static EmbeddedSubtitleFont Font(string fileName, byte[] bytes)
    {
        return new(fileName, Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
    }
}
