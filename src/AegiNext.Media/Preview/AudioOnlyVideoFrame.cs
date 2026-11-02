using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// Placeholder frame used when the bound media has no video stream; it carries timing only and has no pixels.
/// Convert it with <see cref="AudioOnlyPreviewConverter"/>, which renders an opaque black background.
/// </summary>
public sealed class AudioOnlyVideoFrame : IVideoFrame
{
    internal AudioOnlyVideoFrame(int width, int height, long timestamp, int timeBaseDenominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeBaseDenominator);
        Info = new(new NativeDecodedFrameInfo
        {
            width = (uint)width,
            height = (uint)height,
            planeCount = 1,
            componentCount = 3,
            timeBaseNum = 1,
            timeBaseDen = timeBaseDenominator,
            flags = 1 | 8,
            pts = timestamp
        }, default, []);
    }

    public VideoFrameInfo Info { get; }

    public VideoPlaneInfo GetPlaneInfo(int index)
    {
        throw new NotSupportedException("Audio-only placeholder frames have no pixel planes.");
    }

    public byte[] CopyPlane(int index)
    {
        throw new NotSupportedException("Audio-only placeholder frames have no pixel planes.");
    }

    public void Dispose()
    {
    }
}
