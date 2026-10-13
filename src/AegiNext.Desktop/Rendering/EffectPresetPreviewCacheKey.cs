using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewCacheKey(string SourceHash, EffectPresetPreviewOptions Options,
    int Width, int Height, SystemFontCatalog? FontCatalog)
{
    internal static EffectPresetPreviewCacheKey Create(EffectPresetPreviewOptions options, EffectPresetPreviewRequest request)
    {
        var size = GetOutputSize(options, request.RenderScaling);
        return new(request.SourceHash, options, size.Width, size.Height, request.FontCatalog);
    }

    internal static (int Width, int Height) GetOutputSize(EffectPresetPreviewOptions options, double renderScaling)
    {
        var width = Math.Ceiling(options.CanvasWidth * renderScaling);
        var height = Math.Ceiling(options.CanvasHeight * renderScaling);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > int.MaxValue || height > int.MaxValue ||
            width * height > options.MaximumOutputPixels || width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScaling), "特效预览输出超过像素上限。");
        }
        _ = new SdrPreviewOptions((int)width, (int)height);
        return ((int)width, (int)height);
    }
}
