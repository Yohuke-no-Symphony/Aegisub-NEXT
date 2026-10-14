using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectLibraryPackageTests
{
    [Fact]
    public void PackagePreservesOrderPortableFontsTimingAndOriginalEffectMetadata()
    {
        var font = new byte[] { 1, 2, 3, 4 };
        var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new SubtitleStyle(),
            new("font.ttf", Convert.ToHexStringLower(SHA256.HashData(font)), font.ToImmutableArray()), new TimingPostProcessorOptions());
        var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new SubtitleStyle());
        var effect = Effect();
        var package = new EffectLibraryPackage
        {
            Styles = new() { Presets = [second, first] },
            Effects = new() { Presets = [effect] }
        };

        var bytes = EffectLibraryPackageStore.Serialize(package);
        var read = EffectLibraryPackageStore.Deserialize(bytes);

        Assert.Equal([second.Id, first.Id], read.Styles.Presets.Select(preset => preset.Id));
        Assert.Equal(first.Name, read.Styles.Presets[1].Name);
        Assert.Equal(first.TimingPostProcessor, read.Styles.Presets[1].TimingPostProcessor);
        Assert.Equal(font, read.Styles.Presets[1].Font!.Data);
        Assert.Equal(effect, Assert.Single(read.Effects.Presets));
        using var buffer = new MemoryStream(bytes);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.Equal(4, archive.Entries.Count);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("settings", StringComparison.Ordinal) ||
            entry.FullName.Contains("legacy", StringComparison.Ordinal) || entry.FullName.Contains("revision", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("missing")]
    [InlineData("version")]
    [InlineData("utf8")]
    public void UnknownDuplicateUnsafeMissingAndCorruptedEntriesAreRejected(string mutation)
    {
        var bytes = EffectLibraryPackageStore.Serialize(new() { Effects = new() { Presets = [Effect()] } });
        var malformed = Rewrite(bytes, entries =>
        {
            if (mutation == "unknown")
            {
                entries.Add(("preferences.json", "{}"u8.ToArray()));
            }
            else if (mutation == "duplicate")
            {
                entries.Add(entries[1]);
            }
            else if (mutation == "path")
            {
                entries[1] = ("../effect.aegieffect", entries[1].Data);
            }
            else if (mutation == "hash")
            {
                entries[1] = (entries[1].Path, [.. entries[1].Data, (byte)' ']);
            }
            else if (mutation == "missing")
            {
                entries.RemoveAt(1);
            }
            else if (mutation == "version")
            {
                var manifest = JsonNode.Parse(entries[0].Data)!.AsObject();
                manifest["version"] = 99;
                entries[0] = (entries[0].Path, Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            }
            else
            {
                entries[0] = (entries[0].Path, [0xFF]);
            }
        });

        Assert.Throws<InvalidDataException>(() => EffectLibraryPackageStore.Deserialize(malformed));
    }

    [Fact]
    public void EmptyPackageRoundTripsAndPersonalPackagesRejectBuiltinTemplatesAndCollectionConflicts()
    {
        var empty = EffectLibraryPackageStore.Deserialize(EffectLibraryPackageStore.Serialize(new()));
        Assert.Empty(empty.Styles.Presets);
        Assert.Empty(empty.Effects.Presets);
        var effect = Effect();
        Assert.Throws<InvalidDataException>(() => EffectLibraryPackageStore.Serialize(new()
        {
            Effects = new() { Presets = [effect, effect with { Id = Guid.NewGuid() }] }
        }));
        Assert.Throws<InvalidDataException>(() => EffectLibraryPackageStore.Serialize(new()
        {
            Effects = new() { Presets = [new(Guid.NewGuid(), "Builtin", BuiltinEffectScripts.Get("fade-in").Source)] }
        }));
    }

    [Fact]
    public void ExpansionBudgetIsCheckedBeforeReadingDeclaredLargeEntries()
    {
        var bytes = EffectLibraryPackageStore.Serialize(new()
        {
            Styles = new() { Presets = Enumerable.Range(0, 3).Select(index =>
                new SubtitleStylePreset(Guid.NewGuid(), $"Style {index}", new SubtitleStyle())).ToImmutableArray() }
        });
        var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 6, 4)));
        while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) == 0x02014B50)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32, 2));
            var name = Encoding.UTF8.GetString(bytes.AsSpan(offset + 46, nameLength));
            if (name.StartsWith("styles/", StringComparison.Ordinal))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), 48 * 1024 * 1024);
            }
            offset += 46 + nameLength + extraLength + commentLength;
        }

        var error = Assert.Throws<InvalidDataException>(() => EffectLibraryPackageStore.Deserialize(bytes));

        Assert.Contains("128 MiB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedPackageIsRejectedBeforeReadAndCanceledSaveKeepsExistingOutput()
    {
        using var directory = new TemporaryProjectDirectory();
        var oversized = Path.Combine(directory.Path, "oversized.aegilibrary");
        await using (var stream = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(EffectLibraryPackageStore.MAXIMUM_FILE_BYTES + 1L);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => EffectLibraryPackageStore.LoadAsync(oversized));
        var path = Path.Combine(directory.Path, "retained.aegilibrary");
        await EffectLibraryPackageStore.SaveAsync(new() { Effects = new() { Presets = [Effect()] } }, path);
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EffectLibraryPackageStore.SaveAsync(new(), path, cancellation.Token));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private static byte[] Rewrite(byte[] bytes, Action<List<(string Path, byte[] Data)>> mutation)
    {
        var entries = new List<(string Path, byte[] Data)>();
        using (var original = new MemoryStream(bytes))
        using (var archive = new ZipArchive(original, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                using var stream = entry.Open();
                using var content = new MemoryStream();
                stream.CopyTo(content);
                entries.Add((entry.FullName, content.ToArray()));
            }
        }
        mutation(entries);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in entries)
            {
                using var stream = archive.CreateEntry(entry.Path).Open();
                stream.Write(entry.Data);
            }
        }
        return output.ToArray();
    }

    private static EffectScriptPreset Effect()
    {
        return new(Guid.NewGuid(), "展示名称", BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", "\"personal\"", StringComparison.Ordinal));
    }
}
