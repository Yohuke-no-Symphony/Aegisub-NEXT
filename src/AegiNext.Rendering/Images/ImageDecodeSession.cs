using SkiaSharp;

namespace AegiNext.Rendering.Images;

internal sealed class ImageDecodeSession(SKData data, SKCodec codec, ImageMetadata metadata) : IDisposable
{
    internal ImageMetadata Metadata { get; } = metadata;
    internal long EncodedBytes => data.Size;
    internal long RetainedBytes => EncodedBytes + (long)Metadata.FrameCount * 32;

    internal SKImage DecodeFrame(int frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameIndex, Metadata.FrameCount);
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(Metadata.Width, Metadata.Height, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Transparent);
        var result = codec.GetPixels(info, bitmap.GetPixels(), bitmap.RowBytes, new SKCodecOptions(frameIndex, -1));
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"Image frame {frameIndex + 1} could not be decoded: {result}.");
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap) ?? throw new InvalidDataException("The image frame could not be created.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        codec.Dispose();
        data.Dispose();
    }
}
