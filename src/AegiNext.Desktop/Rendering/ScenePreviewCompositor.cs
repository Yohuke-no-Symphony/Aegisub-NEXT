using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;
using SkiaSharp;
using System.Runtime.InteropServices;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed class ScenePreviewCompositor : IDisposable
{
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private SystemFontCatalog? fontCatalog;
    internal Exception? ImageWarning => renderer?.PreviewImageFailures.Values.FirstOrDefault();

    internal SdrVideoFrame Compose(ScenePreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (renderer is null || directory != request.Directory || !ReferenceEquals(fontCatalog, request.FontCatalog))
        {
            renderer?.Dispose();
            fontCatalog = request.FontCatalog;
            renderer = new(new DirectoryProjectAssetResolver(request.Directory), fontCatalog, skipUnavailableImagesInPreview: true);
            directory = request.Directory;
        }
        var background = request.Background ?? new(1, 1, new byte[] { 0, 0, 0, 255 });
        var pixels = renderer.ComposePreview(request.Document, request.TargetTime, background.Pixels.Span,
            background.Width, background.Height, background.Width * 4, request.Width, request.Height, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(request.Width, request.Height, pixels);
    }

    internal static SdrVideoFrame Fallback(ScenePreviewRequest request)
    {
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(request.Width, request.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            if (request.Background is { } background)
            {
                using var image = SKImage.FromPixelCopy(new(background.Width, background.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb), background.Pixels.Span, background.Width * 4);
                var scale = Math.Min((double)request.Width / background.Width, (double)request.Height / background.Height);
                var width = (float)(background.Width * scale);
                var height = (float)(background.Height * scale);
                var left = (request.Width - width) / 2;
                var top = (request.Height - height) / 2;
                canvas.DrawImage(image, new SKRect(left, top, left + width, top + height), new SKSamplingOptions(SKFilterMode.Linear));
            }
        }
        var pixels = new byte[checked(request.Width * request.Height * 4)];
        for (var row = 0; row < request.Height; row++)
        {
            Marshal.Copy(bitmap.GetPixels() + row * bitmap.RowBytes, pixels, row * request.Width * 4, request.Width * 4);
        }
        return new(request.Width, request.Height, pixels);
    }

    public void Dispose() => renderer?.Dispose();
}
