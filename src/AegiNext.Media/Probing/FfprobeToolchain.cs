using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace AegiNext.Media.Probing;

internal static class FfprobeToolchain
{
    internal static FfprobeToolIdentity ReadIdentity(string json, string executablePath, string sha256)
    {
        try
        {
            using var resource = typeof(FfprobeToolchain).Assembly.GetManifestResourceStream("AegiNext.Media.Probing.ffmpeg-toolchain.json")
                ?? throw new InvalidOperationException("The embedded FFmpeg tool version contract is missing.");
            using var manifest = JsonDocument.Parse(resource);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("program_version").GetProperty("version").GetString()
                ?? throw new InvalidDataException("ffprobe did not report its program version.");
            if (!manifest.RootElement.GetProperty("acceptedVersionStrings").EnumerateArray().Any(item => item.GetString() == version))
            {
                throw new InvalidDataException($"Unsupported ffprobe version {version}; a supported build of {manifest.RootElement.GetProperty("version").GetString()} is required.");
            }

            var libraries = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var library in root.GetProperty("library_versions").EnumerateArray())
            {
                var name = library.GetProperty("name").GetString() ?? throw new InvalidDataException("The media library name is missing.");
                var compiledVersion = string.Create(CultureInfo.InvariantCulture,
                    $"{library.GetProperty("major").GetInt32()}.{library.GetProperty("minor").GetInt32()}.{library.GetProperty("micro").GetInt32()}");
                var packedVersion = library.GetProperty("version").GetUInt32();
                var libraryVersion = string.Create(CultureInfo.InvariantCulture,
                    $"{packedVersion >> 16}.{(packedVersion >> 8) & 255}.{packedVersion & 255}");
                if (compiledVersion != libraryVersion)
                {
                    throw new InvalidDataException($"Media library {name} has mismatched compile-time version {compiledVersion} and runtime version {libraryVersion}.");
                }

                if (!libraries.TryAdd(name, libraryVersion))
                {
                    throw new InvalidDataException($"ffprobe reported {name} more than once.");
                }
            }

            foreach (var expected in manifest.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (!libraries.TryGetValue(expected.Name, out var actual) || actual != expected.Value.GetString())
                {
                    throw new InvalidDataException($"Media library {expected.Name} version mismatch: {actual ?? "not reported"}; {expected.Value.GetString()} is required.");
                }
            }

            return new(executablePath, sha256, version, libraries.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The ffprobe tool identity report is invalid.", exception);
        }
    }
}
