using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Export.Worker;

namespace AegiNext.Export;

internal static class ExportFontSnapshot
{
    internal static async Task<ImmutableArray<ExportWorkerFontFamily>> WriteAsync(ImmutableArray<FontFamilyBundle> families,
        string temporaryDirectory, CancellationToken cancellationToken)
    {
        if (families.IsDefault)
        {
            throw new InvalidDataException("The export font snapshot is uninitialized.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        FontFamilyBundleValidator.ValidateLibrary(families);

        var files = new Dictionary<string, EmbeddedSubtitleFont>(StringComparer.Ordinal);
        var index = ImmutableArray.CreateBuilder<ExportWorkerFontFamily>();
        foreach (var family in families)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = ImmutableArray.CreateBuilder<ExportWorkerFontFile>();
            foreach (var font in family.Files)
            {
                files.TryAdd(font.Sha256, font);
                entries.Add(new(font.FileName, font.Sha256));
            }
            index.Add(new(family.FamilyName, entries.ToImmutable()));
        }

        if (files.Count != 0)
        {
            var directory = Path.Combine(temporaryDirectory, "fonts");
            Directory.CreateDirectory(directory);
            foreach (var font in files.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(FilePath(temporaryDirectory, font.Sha256), FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await stream.WriteAsync(font.Data.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        return index.ToImmutable();
    }

    internal static async Task<ImmutableArray<FontFamilyBundle>> ReadAsync(ImmutableArray<ExportWorkerFontFamily> index,
        string temporaryDirectory, CancellationToken cancellationToken)
    {
        if (index.IsDefault)
        {
            throw new InvalidDataException("The export font snapshot index is uninitialized.");
        }

        var families = ImmutableArray.CreateBuilder<FontFamilyBundle>();
        var files = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (family is null)
            {
                throw new InvalidDataException("The export font family index entry is missing.");
            }
            ValidateFamily(family.FamilyName, family.Files.IsDefaultOrEmpty, names);
            var restored = ImmutableArray.CreateBuilder<EmbeddedSubtitleFont>();
            foreach (var font in family.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (font is null)
                {
                    throw new InvalidDataException("The export font file index entry is missing.");
                }
                ValidateFile(font.FileName, font.Sha256);
                if (!files.TryGetValue(font.Sha256, out var data))
                {
                    var path = FilePath(temporaryDirectory, font.Sha256);
                    RejectSymbolicLink(Path.GetDirectoryName(path)!);
                    RejectSymbolicLink(path);
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        65536, FileOptions.Asynchronous);
                    var length = stream.Length;
                    if (length is <= 0 or > FontFamilyBundleValidator.MAXIMUM_FONT_BYTES)
                    {
                        throw new InvalidDataException("The frozen export font file is empty or oversized.");
                    }
                    var bytes = new byte[(int)length];
                    await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                    if (stream.ReadByte() != -1 ||
                        !font.Sha256.AsSpan().SequenceEqual(Convert.ToHexStringLower(SHA256.HashData(bytes))))
                    {
                        throw new InvalidDataException("The frozen export font file does not match its SHA-256.");
                    }
                    data = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes);
                    files.Add(font.Sha256, data);
                }
                restored.Add(new(font.FileName, font.Sha256, data));
            }
            var bundle = new FontFamilyBundle(family.FamilyName, restored.ToImmutable());
            FontFamilyBundleValidator.Validate(bundle);
            families.Add(bundle);
        }
        return families.ToImmutable();
    }

    private static string FilePath(string temporaryDirectory, string hash)
    {
        return Path.Combine(temporaryDirectory, "fonts", hash + ".font");
    }

    private static void ValidateFamily(string name, bool filesEmpty, HashSet<string> names)
    {
        FontFamilyBundleValidator.ValidateFamilyName(name);
        if (filesEmpty || !names.Add(name))
        {
            throw new InvalidDataException("The export font family is missing, duplicated, or has no files.");
        }
    }

    private static void ValidateFile(string name, string hash)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".." ||
            name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Any(char.IsControl) ||
            hash is null || hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new InvalidDataException("The export font filename or SHA-256 is invalid.");
        }
    }

    private static void RejectSymbolicLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The frozen export font snapshot cannot contain symbolic links.");
        }
    }
}
