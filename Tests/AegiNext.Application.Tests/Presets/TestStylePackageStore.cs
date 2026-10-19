using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

internal static class TestStylePackageStore
{
    internal static FontFamilyBundle Family(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name).ToImmutableArray();
        return new(name, [new("fixture.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes)]);
    }

    internal static SubtitleStylePackage Package(SubtitleStylePresetCollection styles)
    {
        return new()
        {
            Styles = styles,
            FontFamilies = styles.Presets.Select(preset => preset.Style.FontFamily).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(Family).ToImmutableArray()
        };
    }

    internal static Task SaveAsync(SubtitleStylePresetCollection collection, string path, CancellationToken cancellationToken = default)
        => SubtitleStylePresetStore.SaveAsync(Package(collection), path, cancellationToken);

    internal static Task SaveAsync(SubtitleStylePresetCollection collection, string path, Action? beforeCommit, CancellationToken cancellationToken = default)
        => SubtitleStylePresetStore.SaveAsync(Package(collection), path, beforeCommit, cancellationToken);

    internal static async Task<SubtitleStylePresetCollection> LoadAsync(string path, CancellationToken cancellationToken = default)
        => (await SubtitleStylePresetStore.LoadAsync(path, cancellationToken)).Styles;

    internal static byte[] Serialize(SubtitleStylePresetCollection collection) => SubtitleStylePresetStore.Serialize(Package(collection));

    internal static SubtitleStylePresetCollection Deserialize(ReadOnlySpan<byte> bytes) => SubtitleStylePresetStore.Deserialize(bytes).Styles;
}
