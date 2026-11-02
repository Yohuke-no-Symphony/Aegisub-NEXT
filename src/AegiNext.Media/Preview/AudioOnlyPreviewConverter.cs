using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// Wraps a preview converter and renders <see cref="AudioOnlyVideoFrame"/> placeholders as an opaque black
/// background fitted to the preview bounds; every other frame is delegated unchanged.
/// </summary>
public sealed class AudioOnlyPreviewConverter(IVideoPreviewConverter inner, SdrPreviewOptions? options = null) : IVideoPreviewConverter
{
    private readonly SdrPreviewOptions options = options ?? new();
    private SdrVideoFrame? black;

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame is not AudioOnlyVideoFrame placeholder)
        {
            return inner.Convert(frame, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (width, height) = Fit(placeholder.Info.Width, placeholder.Info.Height);
        var cached = black;
        if (cached is null || cached.Width != width || cached.Height != height)
        {
            var pixels = new byte[checked(width * height * 4)];
            for (var index = 3; index < pixels.Length; index += 4)
            {
                pixels[index] = byte.MaxValue;
            }

            cached = SdrVideoFrame.FromOwnedPixels(width, height, pixels);
            black = cached;
        }

        return cached.CreateView();
    }

    public void Dispose()
    {
        inner.Dispose();
    }

    internal (int Width, int Height) Fit(int width, int height)
    {
        var scale = Math.Min(1.0, Math.Min((double)options.MaximumWidth / width, (double)options.MaximumHeight / height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
