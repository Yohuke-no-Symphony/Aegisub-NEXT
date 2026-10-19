using System.Buffers;
using AegiNext.Application.Fonts;
using AegiNext.Core.Fonts;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AegiNext.Application.Presets;

internal static class EffectLibraryManifestStore
{
    internal const int MAXIMUM_FILE_BYTES = 4 * 1024 * 1024;
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

    internal static EffectLibraryManifest? DeserializeCurrent(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库清单超过容量预算。");
        }
        try
        {
            _ = strictUtf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("效果库清单必须为对象。");
            }
            RejectDuplicateKeys(document.RootElement);
            if (!document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number is not (1 or 2))
            {
                throw new InvalidDataException("效果库清单版本无效。");
            }
            if (number == 1)
            {
                return null;
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
        if (manifest.Version != 2 || manifest.Revision == Guid.Empty || manifest.Styles.IsDefault || manifest.Effects.IsDefault || manifest.FontFamilies.IsDefault ||
            manifest.Styles.Length > 256 || manifest.Effects.Length > 512)
        {
            throw new InvalidDataException("效果库清单版本、修订或条目数量无效。");
        }
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in manifest.FontFamilies)
        {
            if (family is null || family.Files.IsDefaultOrEmpty || !families.Add(family.FamilyName))
            {
                throw new InvalidDataException("字体族清单无效或重复。");
            }
            FontFamilyBundleValidator.ValidateFamilyName(family.FamilyName);
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in family.Files)
            {
                if (file is null || !files.Add(PrivateFontFiles.Name(file.Sha256)) || string.IsNullOrWhiteSpace(file.FileName))
                {
                    throw new InvalidDataException("字体文件清单无效或重复。");
                }
            }
        }
        var styles = new HashSet<Guid>();
        foreach (var name in manifest.Styles)
        {
            if (!styles.Add(EntryIdentity(name, ".json")))
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

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("效果库清单字段重复。");
                }
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateKeys(item);
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
            MaxDepth = 8,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
    }
}
