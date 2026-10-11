using System.Buffers.Binary;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Images;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class ImageDecodingTests
{
    [Fact]
    public void GifReportsUnevenTimingAndRandomSeekRestoresPreviousAndBackground()
    {
        using var stream = new MemoryStream(ImageTestData.Gif());
        var decoder = new SkiaImageDecoder();
        var metadata = decoder.Probe(stream);
        Assert.Equal(4, metadata.FrameCount);
        Assert.Equal(new MediaTime(1), metadata.Duration);
        Assert.Equal(1, metadata.RepetitionCount);
        Assert.Equal(new MediaTime[] { new(1, 10), new(1, 5), new(3, 10), new(2, 5) }, metadata.FrameDurations.ToArray());

        foreach (var frame in new[] { 2, 0, 3, 1, 2 })
        {
            stream.Position = 0;
            using var image = decoder.DecodeFrame(stream, frame);
            using var bitmap = SKBitmap.FromImage(image);
            Assert.Equal(frame == 1 ? SKColors.Lime : SKColors.Red, bitmap.GetPixel(0, 0));
            if (frame == 3)
            {
                Assert.Equal(0, bitmap.GetPixel(2, 0).Alpha);
            }
            else
            {
                Assert.Equal(frame == 2 ? SKColors.Blue : SKColors.Red, bitmap.GetPixel(2, 0));
            }
            Assert.Equal(frame == 3 ? SKColors.Lime : SKColors.Red, bitmap.GetPixel(3, 1));
        }
    }

    [Fact]
    public void AnimatedWebpIsDecodedAsFramesInsteadOfSilentlyUsingFirstFrame()
    {
        using var stream = new MemoryStream(ImageTestData.Webp());
        var decoder = new SkiaImageDecoder();
        var metadata = decoder.Probe(stream);
        Assert.True(metadata.IsAnimated);
        Assert.Equal(2, metadata.FrameCount);
        Assert.Equal(new MediaTime(1, 5), metadata.Duration);
        stream.Position = 0;
        using var frame = decoder.DecodeFrame(stream, 1);
        using var bitmap = SKBitmap.FromImage(frame);
        Assert.True(bitmap.GetPixel(0, 0).Blue > 240);
        Assert.True(bitmap.GetPixel(0, 0).Red < 15);
    }

    [Fact]
    public void FrameSelectionHandlesExactBoundariesLoopsFiniteRepeatsAndNegativeOffsets()
    {
        var metadata = new ImageMetadata(4, 2, [new(1, 10), new(1, 5), new(3, 10), new(2, 5)], 1);
        Assert.Equal(0, metadata.GetFrameIndex(new(-1), ImagePlaybackMode.LOOP));
        Assert.Equal(1, metadata.GetFrameIndex(new(1, 10), ImagePlaybackMode.LOOP));
        Assert.Equal(2, metadata.GetFrameIndex(new(3, 10), ImagePlaybackMode.LOOP));
        Assert.Equal(0, metadata.GetFrameIndex(new(1), ImagePlaybackMode.LOOP));
        Assert.Equal(3, metadata.GetFrameIndex(new(1), ImagePlaybackMode.ONCE));
        Assert.Equal(0, metadata.GetFrameIndex(new(1), ImagePlaybackMode.FILE));
        Assert.Equal(3, metadata.GetFrameIndex(new(2), ImagePlaybackMode.FILE));
        Assert.Equal(2, metadata.GetFrameIndex(new(23, 10), ImagePlaybackMode.LOOP));
        Assert.Equal(0, (metadata with { RepetitionCount = -1 }).GetFrameIndex(new(1000), ImagePlaybackMode.FILE));
    }

    [Fact]
    public void ApngIsRejectedBeforeStaticPngDecode()
    {
        var png = ImageTestData.Png(SKColors.Red, SKColors.Blue);
        using var bytes = new MemoryStream();
        bytes.Write(png, 0, 33);
        bytes.Write(new byte[] { 0, 0, 0, 8 });
        bytes.Write("acTL"u8);
        bytes.Write(new byte[12]);
        bytes.Write(png, 33, png.Length - 33);
        bytes.Position = 0;
        var error = Assert.Throws<NotSupportedException>(() => new SkiaImageDecoder().Probe(bytes));
        Assert.Contains("APNG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PixelBudgetIsCheckedBeforeAllocatingDecodedPixels()
    {
        var png = ImageTestData.Png(SKColors.Red, SKColors.Blue);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 32768);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 32768);
        using var stream = new MemoryStream(png);
        Assert.Throws<InvalidDataException>(() => new SkiaImageDecoder().Probe(stream));
    }

    [Fact]
    public void ThumbnailPreservesAspectRatioAndLeavesInputOpen()
    {
        using var stream = new MemoryStream(ImageTestData.Png(SKColors.Red, SKColors.Transparent));
        using var thumbnail = new SkiaImageDecoder().CreateThumbnail(stream, 20);
        Assert.Equal(2, thumbnail.Width);
        Assert.Equal(1, thumbnail.Height);
        Assert.True(stream.CanRead);
        using var bitmap = SKBitmap.FromImage(thumbnail);
        Assert.Equal(0, bitmap.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void CacheBoundsDecodedFramesByBytesAndIncludesAcceptedContentVersion()
    {
        var resolver = new MemoryImageAssetResolver(ImageTestData.Gif());
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/test.gif");
        using var cache = new ImageFrameCache(resolver, 32, 4096);
        using (cache.GetFrame(new(asset, 0)))
        {
            Assert.Equal(32, cache.RetainedFrameBytes);
        }
        using (cache.GetFrame(new(asset, 1)))
        {
            Assert.Equal(32, cache.RetainedFrameBytes);
        }
        using (cache.GetFrame(new(asset, 0)))
        {
            Assert.Equal(3UL, cache.DecodedFrames);
        }
        using (cache.GetFrame(new(asset with { Sha256 = new('a', 64) }, 0)))
        {
            Assert.Equal(4UL, cache.DecodedFrames);
            Assert.Equal(2, resolver.OpenCount);
        }
    }
}
