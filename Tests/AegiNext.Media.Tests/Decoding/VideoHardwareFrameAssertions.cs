using AegiNext.Testing.Media;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

internal static class VideoHardwareFrameAssertions
{
    private static readonly string[] supportedFormats = ["nv12", "p010le", "p210le", "p410le", "p216le", "p416le", "ayuv64le"];

    internal static void AssertReferenceSamples(VideoCompatibilityFixture fixture, IVideoFrame frame, int index,
        bool allowProResRounding = false)
    {
        if (frame.Info.PixelFormat == fixture.PixelFormat)
        {
            var bytes = Enumerable.Range(0, frame.Info.PlaneCount).SelectMany(frame.CopyPlane).ToArray();
            var expected = fixture.RawFrames.AsSpan(index * fixture.FrameByteCount, fixture.FrameByteCount).ToArray();
            if (allowProResRounding && Path.GetExtension(fixture.MediaPath) == ".mov")
            {
                var planarOffset = 0;
                for (var component = 0; component < frame.Info.PlaneCount; component++)
                {
                    var plane = frame.GetPlaneInfo(component);
                    var length = plane.RowBytes * plane.Height;
                    var maximum = 0;
                    var total = 0L;
                    for (var sample = planarOffset; sample < planarOffset + length; sample += 2)
                    {
                        var error = Math.Abs(BitConverter.ToUInt16(expected, sample) - BitConverter.ToUInt16(bytes, sample));
                        maximum = Math.Max(maximum, error);
                        total += error;
                    }
                    // The existing ProRes IDCT bounds also apply when GPU readback
                    // keeps the original planar layout. Alpha must remain exact.
                    Assert.True(maximum <= (component == 3 ? 0 : 4), $"{frame.Info.PixelFormat} component {component}: maximum error {maximum}.");
                    Assert.True(total / (length / 2.0) <= (component == 3 ? 0 : 0.3),
                        $"{frame.Info.PixelFormat} component {component}: mean error {total / (length / 2.0)}.");
                    planarOffset += length;
                }
                Assert.Equal(expected.Length, planarOffset);
            }
            else
            {
                Assert.Equal(expected, bytes);
            }
            return;
        }

        var sourceDepth = fixture.PixelFormat.Contains("12", StringComparison.Ordinal) ? 12 :
            fixture.PixelFormat.Contains("10", StringComparison.Ordinal) ? 10 : 8;
        var horizontalSubsample = fixture.PixelFormat.Contains("444", StringComparison.Ordinal) ? 1 : 2;
        var verticalSubsample = fixture.PixelFormat.Contains("420", StringComparison.Ordinal) ? 2 : 1;
        var alpha = fixture.PixelFormat.StartsWith("yuva", StringComparison.Ordinal);
        var format = frame.Info.PixelFormat;
        Assert.Contains(format, supportedFormats);
        Assert.Equal(horizontalSubsample, format is "p410le" or "p416le" or "ayuv64le" ? 1 : 2);
        Assert.Equal(verticalSubsample, format is "nv12" or "p010le" ? 2 : 1);
        Assert.Equal(alpha, format == "ayuv64le");
        var planes = Enumerable.Range(0, frame.Info.PlaneCount).Select(frame.CopyPlane).ToArray();
        var offset = index * fixture.FrameByteCount;
        var sourceBytes = sourceDepth > 8 ? 2 : 1;
        var isProRes = Path.GetExtension(fixture.MediaPath) == ".mov";
        for (var component = 0; component < (alpha ? 4 : 3); component++)
        {
            Assert.True(frame.Info.ComponentDepths[component] >= sourceDepth);
            var width = fixture.Width / (component is 1 or 2 ? horizontalSubsample : 1);
            var height = fixture.Height / (component is 1 or 2 ? verticalSubsample : 1);
            var planeIndex = format == "ayuv64le" ? 0 : component == 0 ? 0 : 1;
            var plane = frame.GetPlaneInfo(planeIndex);
            var maximumError = 0.0;
            var totalError = 0.0;
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var sourceOffset = offset + (row * width + column) * sourceBytes;
                    var expected = sourceBytes == 1 ? fixture.RawFrames[sourceOffset] :
                        BitConverter.ToUInt16(fixture.RawFrames, sourceOffset);
                    var sampleOffset = format == "ayuv64le" ? column * 4 + (component == 3 ? 0 : component + 1) :
                        component == 0 ? column : column * 2 + component - 1;
                    var bytesPerSample = format == "nv12" ? 1 : 2;
                    var actualOffset = row * plane.RowBytes + sampleOffset * bytesPerSample;
                    var actual = bytesPerSample == 1 ? planes[planeIndex][actualOffset] :
                        BitConverter.ToUInt16(planes[planeIndex], actualOffset);
                    if (format is "p010le" or "p210le" or "p410le")
                    {
                        actual >>= 6;
                    }
                    var normalized = actual >> (frame.Info.ComponentDepths[component] - sourceDepth);
                    var error = Math.Abs(expected - normalized);
                    maximumError = Math.Max(maximumError, error);
                    totalError += error;
                }
            }
            Assert.True(maximumError <= (isProRes && component != 3 ? 4 : 0),
                $"{format} component {component}: maximum source-depth error {maximumError}.");
            Assert.True(totalError / (width * height) <= (isProRes && component != 3 ? 0.3 : 0),
                $"{format} component {component}: mean source-depth error {totalError / (width * height)}.");
            offset += width * height * sourceBytes;
        }
        Assert.Equal((index + 1) * fixture.FrameByteCount, offset);
    }
}
