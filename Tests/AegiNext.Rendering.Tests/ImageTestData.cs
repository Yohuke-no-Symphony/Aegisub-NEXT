using System.Text;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

internal static class ImageTestData
{
    internal static byte[] Gif()
    {
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.ASCII, true);
        writer.Write("GIF89a"u8);
        writer.Write((ushort)4);
        writer.Write((ushort)2);
        writer.Write(new byte[] { 0x81, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 });
        writer.Write(new byte[] { 0x21, 0xff, 11 });
        writer.Write("NETSCAPE2.0"u8);
        writer.Write(new byte[] { 3, 1, 1, 0, 0 });
        GifFrame(writer, 0, 0, 4, 2, 1, 10, 1);
        GifFrame(writer, 0, 0, 1, 1, 2, 20, 3);
        GifFrame(writer, 2, 0, 1, 1, 3, 30, 2);
        GifFrame(writer, 3, 1, 1, 1, 2, 40, 1);
        writer.Write((byte)0x3b);
        return result.ToArray();
    }

    internal static byte[] Png(SKColor first, SKColor second)
    {
        using var bitmap = new SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, first);
        bitmap.SetPixel(1, 0, second);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] Webp()
    {
        using var chunks = new MemoryStream();
        using var writer = new BinaryWriter(chunks, Encoding.ASCII, true);
        WriteChunk(writer, "VP8X", [2, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        WriteChunk(writer, "ANIM", [0, 0, 0, 0, 2, 0]);
        foreach (var color in new[] { SKColors.Red, SKColors.Blue })
        {
            using var bitmap = new SKBitmap(2, 2);
            bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 100);
            var bytes = encoded.ToArray();
            using var frame = new MemoryStream();
            frame.Write(new byte[] { 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 100, 0, 0, 2 });
            for (var offset = 12; offset < bytes.Length;)
            {
                var length = BitConverter.ToInt32(bytes, offset + 4);
                var size = 8 + length + (length & 1);
                var kind = Encoding.ASCII.GetString(bytes, offset, 4);
                if (kind is "VP8 " or "VP8L" or "ALPH")
                {
                    frame.Write(bytes, offset, size);
                }
                offset += size;
            }
            WriteChunk(writer, "ANMF", frame.ToArray());
        }

        using var result = new MemoryStream();
        using var output = new BinaryWriter(result, Encoding.ASCII, true);
        output.Write("RIFF"u8);
        output.Write(checked((int)chunks.Length + 4));
        output.Write("WEBP"u8);
        output.Write(chunks.ToArray());
        return result.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string kind, byte[] bytes)
    {
        writer.Write(Encoding.ASCII.GetBytes(kind));
        writer.Write(bytes.Length);
        writer.Write(bytes);
        if (bytes.Length % 2 != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void GifFrame(BinaryWriter writer, ushort x, ushort y, ushort width, ushort height,
        byte color, ushort delay, byte disposal)
    {
        writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)((disposal << 2) | 1) });
        writer.Write(delay);
        writer.Write(new byte[] { 0, 0, 0x2c });
        writer.Write(x);
        writer.Write(y);
        writer.Write(width);
        writer.Write(height);
        writer.Write(new byte[] { 0, 2 });
        var codes = new List<int>();
        for (var index = 0; index < width * height; index++)
        {
            codes.Add(4);
            codes.Add(color);
        }
        codes.Add(5);
        var data = new byte[(codes.Count * 3 + 7) / 8];
        for (var index = 0; index < codes.Count; index++)
        {
            var bit = index * 3;
            data[bit / 8] |= (byte)(codes[index] << (bit % 8));
            if (bit % 8 > 5)
            {
                data[bit / 8 + 1] |= (byte)(codes[index] >> (8 - bit % 8));
            }
        }
        writer.Write((byte)data.Length);
        writer.Write(data);
        writer.Write((byte)0);
    }
}
