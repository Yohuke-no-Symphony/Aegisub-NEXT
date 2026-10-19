using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Rendering.Tests;

internal static class ApplicationFontFixture
{
    internal static EmbeddedSubtitleFont Read(string name = "NotoSans.ttf")
    {
        return Create(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
    }

    internal static EmbeddedSubtitleFont Create(string name, byte[] bytes)
    {
        return new(name, Convert.ToHexStringLower(SHA256.HashData(bytes)), ImmutableArray.Create(bytes));
    }

    internal static FontFamilyBundle Latin() => new("Noto Sans", [Read()]);

    internal static FontFamilyBundle Arabic() => new("Noto Sans Arabic", [Read("NotoSansArabic.ttf")]);

    internal static EmbeddedSubtitleFont Collection()
    {
        var sources = new[] { Read().Data.ToArray(), Read("NotoSansArabic.ttf").Data.ToArray() };
        var offset = 12 + sources.Length * 4;
        var offsets = new int[sources.Length];
        for (var index = 0; index < sources.Length; index++)
        {
            offset = (offset + 3) & ~3;
            offsets[index] = offset;
            offset += sources[index].Length;
        }
        var result = new byte[offset];
        "ttcf"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)sources.Length);
        for (var index = 0; index < sources.Length; index++)
        {
            var start = offsets[index];
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12 + index * 4), (uint)start);
            sources[index].CopyTo(result, start);
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(start + 4));
            for (var table = 0; table < tableCount; table++)
            {
                var field = start + 12 + table * 16 + 8;
                var original = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(field));
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(field), original + (uint)start);
            }
        }
        return Create("Fixture.ttc", result);
    }
}
