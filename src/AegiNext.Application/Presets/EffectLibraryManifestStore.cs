using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AegiNext.Application.Presets;

internal static class EffectLibraryManifestStore
{
    internal const int MAXIMUM_FILE_BYTES = 256 * 1024;
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly SearchValues<char> hexadecimal = SearchValues.Create("0123456789abcdef");
    private static readonly JsonSerializerOptions options = CreateOptions();

    internal static byte[] Serialize(EffectLibraryManifest manifest)
    {
        Validate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库清单超过容量预算。");
        }
        return bytes;
    }

    internal static EffectLibraryManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库清单超过容量预算。");
        }
        try
        {
            _ = strictUtf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 3 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("效果库清单必须为对象。");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new JsonException("效果库清单字段重复。");
                }
            }
            var manifest = document.RootElement.Deserialize<EffectLibraryManifest>(options) ??
                throw new JsonException("效果库清单不能为 null。");
            Validate(manifest);
            return manifest;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("效果库清单不是有效的 UTF-8 JSON。", error);
        }
    }

    internal static Guid EntryIdentity(string? name, string extension)
    {
        if (name is null || name.Length != 97 + extension.Length || name[32] != '-' ||
            !name.EndsWith(extension, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name.AsSpan(0, 32), "N", out var id) || id == Guid.Empty ||
            name.AsSpan(0, 32).ContainsAnyExcept(hexadecimal) ||
            name.AsSpan(33, 64).ContainsAnyExcept(hexadecimal))
        {
            throw new InvalidDataException("效果库条目文件名必须包含规范的身份和 SHA-256。");
        }
        return id;
    }

    private static void Validate(EffectLibraryManifest manifest)
    {
        if (manifest.Version != 1 || manifest.Revision == Guid.Empty || manifest.Styles.IsDefault || manifest.Effects.IsDefault ||
            manifest.Styles.Length > 256 || manifest.Effects.Length > 512)
        {
            throw new InvalidDataException("效果库清单版本、修订或条目数量无效。");
        }
        var styles = new HashSet<Guid>();
        foreach (var name in manifest.Styles)
        {
            if (!styles.Add(EntryIdentity(name, ".aegistyles")))
            {
                throw new InvalidDataException("效果库清单包含重复样式身份。");
            }
        }
        var effects = new HashSet<Guid>();
        foreach (var name in manifest.Effects)
        {
            if (!effects.Add(EntryIdentity(name, ".aegieffect")))
            {
                throw new InvalidDataException("效果库清单包含重复特效身份。");
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(EffectLibraryManifest))
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = true;
                }
            }
        });
        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 3,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
    }
}
