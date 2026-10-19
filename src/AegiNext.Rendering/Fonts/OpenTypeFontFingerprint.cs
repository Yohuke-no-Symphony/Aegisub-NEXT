using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal static class OpenTypeFontFingerprint
{
    private const int BUFFER_BYTES = 64 * 1024;

    internal static string Read(Stream stream, int collectionIndex, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_BYTES);
        try
        {
            foreach (var table in OpenTypeFontFileReader.ReadTables(stream, collectionIndex).OrderBy(table => table.Key))
            {
                AppendHeader(hash, table.Key, table.Value.Length);
                stream.Position = table.Value.Offset;
                var remaining = table.Value.Length;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = Math.Min(remaining, BUFFER_BYTES);
                    stream.ReadExactly(buffer.AsSpan(0, length));
                    hash.AppendData(buffer.AsSpan(0, length));
                    remaining -= length;
                }
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static unsafe string Read(SKTypeface typeface, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_BYTES);
        try
        {
            fixed (byte* destination = buffer)
            {
                foreach (var tag in typeface.GetTableTags().Order())
                {
                    var size = typeface.GetTableSize(tag);
                    if (size < 0)
                    {
                        throw new InvalidDataException("A system OpenType table has an invalid size.");
                    }
                    AppendHeader(hash, tag, size);
                    var offset = 0;
                    while (offset < size)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var length = Math.Min(size - offset, BUFFER_BYTES);
                        if (!typeface.TryGetTableData(tag, offset, length, (nint)destination))
                        {
                            throw new InvalidDataException("The installed OpenType table could not be read in full.");
                        }
                        hash.AppendData(buffer.AsSpan(0, length));
                        offset += length;
                    }
                }
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendHeader(IncrementalHash hash, uint tag, int size)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, tag);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], size);
        hash.AppendData(header);
    }
}
