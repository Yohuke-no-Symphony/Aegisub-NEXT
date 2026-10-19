using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Rendering.Fonts;

internal static class OpenTypeFontFileReader
{
    private const uint COLLECTION_TAG = 0x74746366;
    private const int MAXIMUM_FACES = 4096;
    private const int MAXIMUM_TABLES = 4096;

    internal static ImmutableArray<OpenTypeFontFileFace> Read(EmbeddedSubtitleFont font)
    {
        using var stream = new MemoryStream(ImmutableCollectionsMarshal.AsArray(font.Data)!, false);
        return Read(stream);
    }

    internal static ImmutableArray<OpenTypeFontFileFace> Read(Stream stream)
    {
        if (!stream.CanSeek || stream.Length > FontFamilyBundleValidator.MAXIMUM_FONT_BYTES)
        {
            throw new InvalidDataException("The OpenType file is not seekable or exceeds the font byte budget.");
        }
        var header = ReadBytes(stream, 0, 12);
        var signature = BinaryPrimitives.ReadUInt32BigEndian(header);
        var offsets = new List<long>();
        if (signature == COLLECTION_TAG)
        {
            var version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
            var count = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
            if (version is not 0x00010000 and not 0x00020000 || count is 0 or > MAXIMUM_FACES)
            {
                throw new InvalidDataException("The OpenType collection version or face count is invalid.");
            }
            var entries = ReadBytes(stream, 12, checked((int)count * 4));
            for (var index = 0; index < count; index++)
            {
                offsets.Add(BinaryPrimitives.ReadUInt32BigEndian(entries.AsSpan(index * 4)));
            }
        }
        else
        {
            offsets.Add(0);
        }
        var result = ImmutableArray.CreateBuilder<OpenTypeFontFileFace>(offsets.Count);
        for (var index = 0; index < offsets.Count; index++)
        {
            result.Add(new(index, ReadFace(stream, offsets[index])));
        }
        return result.MoveToImmutable();
    }

    private static OpenTypeFontMetadata ReadFace(Stream stream, long offset)
    {
        var tables = ReadTables(stream, offset);
        var metadata = OpenTypeFontMetadataReader.Read(ReadMetadataTable(stream, tables, OpenTypeFontMetadataReader.NAME_TAG),
            ReadMetadataTable(stream, tables, OpenTypeFontMetadataReader.VARIATIONS_TAG),
            ReadMetadataTable(stream, tables, OpenTypeFontMetadataReader.OS2_TAG));
        if (metadata.FamilyName is null)
        {
            throw new InvalidDataException("The OpenType face has no usable family identifier.");
        }
        return metadata;
    }

    internal static Dictionary<uint, (long Offset, int Length)> ReadTables(Stream stream, int collectionIndex)
    {
        var header = ReadBytes(stream, 0, 12);
        var collection = BinaryPrimitives.ReadUInt32BigEndian(header) == COLLECTION_TAG;
        var offset = collection ? BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(stream, 12L + collectionIndex * 4L, 4)) : 0L;
        if (!collection && collectionIndex != 0)
        {
            throw new InvalidDataException("A standalone font cannot contain another collection face.");
        }
        return ReadTables(stream, offset);
    }

    private static Dictionary<uint, (long Offset, int Length)> ReadTables(Stream stream, long offset)
    {
        var header = ReadBytes(stream, offset, 12);
        var signature = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (signature is not 0x00010000 and not 0x4F54544F and not 0x74727565 and not 0x74797031)
        {
            throw new InvalidDataException("The file does not contain a supported OpenType face.");
        }
        var count = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        if (count is 0 or > MAXIMUM_TABLES)
        {
            throw new InvalidDataException("The OpenType table count is invalid.");
        }
        var entries = ReadBytes(stream, offset + 12, count * 16);
        var tables = new Dictionary<uint, (long Offset, int Length)>();
        for (var index = 0; index < count; index++)
        {
            var entry = entries.AsSpan(index * 16, 16);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var start = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            RequireRange(stream, start, length);
            if (length > int.MaxValue || !tables.TryAdd(tag, (start, (int)length)))
            {
                throw new InvalidDataException("The OpenType table directory contains duplicate or oversized tables.");
            }
        }
        return tables;
    }

    private static byte[] ReadMetadataTable(Stream stream, Dictionary<uint, (long Offset, int Length)> tables, uint tag)
    {
        if (!tables.TryGetValue(tag, out var table))
        {
            return [];
        }
        OpenTypeFontMetadataReader.ValidateNativeTableSize(table.Length);
        return ReadBytes(stream, table.Offset, table.Length);
    }

    private static byte[] ReadBytes(Stream stream, long offset, int length)
    {
        RequireRange(stream, offset, length);
        var result = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(result);
        return result;
    }

    private static void RequireRange(Stream stream, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
        {
            throw new InvalidDataException("The OpenType file contains a truncated or out-of-range table.");
        }
    }
}
