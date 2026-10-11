using SkiaSharp;

namespace AegiNext.Rendering.Images;

internal sealed class ImageFrameLease(SKImage image, bool ownsImage) : IDisposable
{
    internal SKImage Image { get; } = image;

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsImage)
        {
            Image.Dispose();
        }
    }
}
