using System.Buffers.Binary;
using System.Collections.Immutable;
using AegiNext.Core.Timing;
using SkiaSharp;

namespace AegiNext.Rendering.Images;

/// <summary>以 SKCodec 统一解码 PNG、JPEG、BMP、GIF 和 WebP；拒绝不受支持的 APNG。</summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
    /// <inheritdoc />
    public ImageMetadata Probe(Stream stream)
    {
        using var session = Open(stream);
        return session.Metadata;
    }

    /// <inheritdoc />
    public SKImage DecodeFrame(Stream stream, int frameIndex)
    {
        using var session = Open(stream);
        return session.DecodeFrame(frameIndex);
    }

    /// <inheritdoc />
    public SKImage CreateThumbnail(Stream stream, int maximumDimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDimension);
        using var session = Open(stream);
        using var frame = session.DecodeFrame(0);
        var scale = Math.Min(1, (double)maximumDimension / Math.Max(frame.Width, frame.Height));
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace))
            ?? throw new InvalidOperationException("无法创建图片缩略图。");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(frame, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
        return surface.Snapshot();
    }

    internal static ImageDecodeSession Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var data = ReadBounded(stream);
        SKCodec? codec = null;
        try
        {
            RejectAnimatedPng(data.AsSpan());
            codec = SKCodec.Create(data) ?? throw new InvalidDataException("无法识别图片内容。");
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or
                SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Webp))
            {
                throw new InvalidDataException("仅支持 PNG、JPEG、BMP、GIF 和 WebP 图片。");
            }

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > ImageDecodeLimits.MaximumPixels)
            {
                throw new InvalidDataException("图片超过像素预算。");
            }

            var count = Math.Max(1, codec.FrameCount);
            if (count > ImageDecodeLimits.MaximumFrameCount)
            {
                throw new InvalidDataException("图片帧数超过解码预算。");
            }

            var frames = codec.FrameInfo;
            var durations = ImmutableArray.CreateBuilder<MediaTime>(count);
            for (var index = 0; index < count; index++)
            {
                var milliseconds = index < frames.Length ? frames[index].Duration : 0;
                durations.Add(count == 1 ? MediaTime.Zero : new(milliseconds > 0 ? milliseconds : 100, 1000));
            }

            return new(data, codec, new(info.Width, info.Height, durations.MoveToImmutable(), codec.RepetitionCount));
        }
        catch
        {
            codec?.Dispose();
            data.Dispose();
            throw;
        }
    }

    private static SKData ReadBounded(Stream stream)
    {
        if (stream.CanSeek)
        {
            var length = stream.Length - stream.Position;
            if (length <= 0 || length > ImageDecodeLimits.MaximumEncodedBytes)
            {
                throw new InvalidDataException("图片文件为空或超过 256 MiB。");
            }

            return SKData.Create(stream) ?? throw new InvalidDataException("无法读取图片内容。");
        }

        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int count;
        while ((count = stream.Read(bytes)) != 0)
        {
            if (buffer.Length + count > ImageDecodeLimits.MaximumEncodedBytes)
            {
                throw new InvalidDataException("图片文件超过 256 MiB。");
            }
            buffer.Write(bytes, 0, count);
        }

        if (buffer.Length == 0)
        {
            throw new InvalidDataException("图片文件为空。");
        }
        return SKData.CreateCopy(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    private static void RejectAnimatedPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.StartsWith(signature))
        {
            return;
        }

        var offset = signature.Length;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12)
            {
                throw new InvalidDataException("PNG 数据块长度无效。");
            }
            var kind = bytes.Slice(offset + 4, 4);
            if (kind.SequenceEqual("acTL"u8))
            {
                throw new NotSupportedException("暂不支持 APNG 动画图片，请使用 GIF 或动画 WebP。");
            }
            if (kind.SequenceEqual("IEND"u8))
            {
                break;
            }
            offset = checked(offset + (int)length + 12);
        }
    }
}
