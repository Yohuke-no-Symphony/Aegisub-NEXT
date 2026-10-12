using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>
/// 拥有离屏 F16 表面的绘制上下文。GPU 上下文由调用方持有；实例限同一线程和当前上下文使用。
/// </summary>
public sealed class LinearRenderSurface : IDisposable
{
    private readonly SKColorSpace colorSpace;
    private readonly SKSurface surface;
    private readonly GRContext? graphicsContext;
    private SKSurface? srgbReadback;
    private GpuLinearColorShader? extendedColorShader;
    private bool isDisposed;

    /// <summary>
    /// 创建透明黑表面；不创建窗口，也不执行显示器映射或 tone mapping。
    /// </summary>
    public LinearRenderSurface(RenderSurfaceInfo info, GRContext? graphicsContext = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        this.graphicsContext = graphicsContext;
        colorSpace = SKColorSpace.CreateSrgbLinear();
        SKSurface? createdSurface = null;
        try
        {
            var imageInfo = new SKImageInfo(info.Width, info.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace);
            createdSurface = (graphicsContext is null
                ? SKSurface.Create(imageInfo, info.RowBytes)
                : SKSurface.Create(graphicsContext, true, imageInfo))
                ?? throw new InvalidOperationException("The F16 offscreen surface could not be created.");
            createdSurface.Canvas.Clear(SKColors.Transparent);
            surface = createdSurface;
        }
        catch
        {
            createdSurface?.Dispose();
            colorSpace.Dispose();
            throw;
        }
    }

    public RenderSurfaceInfo Info { get; }

    internal SKCanvas Canvas
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return surface.Canvas;
        }
    }

    internal SKColorSpace ColorSpace => colorSpace;

    internal SKImage Snapshot()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return surface.Snapshot();
    }

    internal void ReplacePixels(ReadOnlySpan<Half> pixels)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        using var image = SKImage.FromPixelCopy(new(Info.Width, Info.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace),
            MemoryMarshal.AsBytes(pixels), Info.RowBytes) ?? throw new InvalidOperationException("The F16 pixels could not be replaced.");
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        var saved = surface.Canvas.Save();
        try
        {
            surface.Canvas.ResetMatrix();
            surface.Canvas.DrawImage(image, 0, 0, paint);
        }
        finally
        {
            surface.Canvas.RestoreToCount(saved);
        }
    }

    /// <summary>复制紧密排列的预乘 sRGB BGRA 像素，保留透明度；调用方拥有返回的缓冲。</summary>
    public byte[] CopySrgbBgra()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        using var srgb = SKColorSpace.CreateSrgb();
        if (graphicsContext is not null)
        {
            return CopyGpuSrgbBgra(srgb);
        }
        using var bitmap = new SKBitmap(new SKImageInfo(Info.Width, Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        if (!surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            throw new InvalidOperationException("The sRGB preview pixels could not be read.");
        }

        var result = new byte[checked(Info.Width * Info.Height * 4)];
        for (var row = 0; row < Info.Height; row++)
        {
            Marshal.Copy(bitmap.GetPixels() + row * bitmap.RowBytes, result, row * Info.Width * 4, Info.Width * 4);
        }

        return result;
    }

    private unsafe byte[] CopyGpuSrgbBgra(SKColorSpace srgb)
    {
        var imageInfo = new SKImageInfo(Info.Width, Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb);
        srgbReadback ??= SKSurface.Create(graphicsContext!, true, imageInfo)
            ?? throw new InvalidOperationException("The GPU sRGB readback surface could not be created.");
        using var image = surface.Snapshot();
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        srgbReadback.Canvas.DrawImage(image, 0, 0, paint);
        var result = new byte[checked(Info.Width * Info.Height * 4)];
        fixed (byte* pixels = result)
        {
            if (!srgbReadback.ReadPixels(imageInfo, (nint)pixels, Info.Width * 4, 0, 0))
            {
                throw new InvalidOperationException("The GPU sRGB preview pixels could not be read.");
            }
        }
        return result;
    }

    /// <summary>
    /// 清除为透明黑，可供下一帧复用。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        surface.Canvas.Clear(SKColors.Transparent);
    }

    /// <summary>
    /// 按未预乘线性颜色及 source-over 规则填充矩形，坐标单位为像素。
    /// </summary>
    public void FillRectangle(Vector2 origin, Vector2 size, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var bounds = GetBounds(origin, size);
        using var paint = CreatePaint(color);
        surface.Canvas.DrawRect(bounds, paint);
    }

    /// <summary>
    /// 以灰度抗锯齿填充指定边界内的椭圆。
    /// </summary>
    public void FillEllipse(Vector2 origin, Vector2 size, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var bounds = GetBounds(origin, size);
        using var paint = CreatePaint(color);
        surface.Canvas.DrawOval(bounds, paint);
    }

    /// <summary>
    /// 在指定基线原点绘制已塑形文字；不接管文字资源所有权。
    /// </summary>
    public void DrawText(ShapedTextRun text, Vector2 baselineOrigin, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(text);
        RenderValidation.Point(baselineOrigin, nameof(baselineOrigin));
        using var paint = CreatePaint(color);
        surface.Canvas.DrawText(text.GetBlob(), baselineOrigin.X, baselineOrigin.Y, paint);
    }

    /// <summary>
    /// 以 source-over 合成源表面的快照；透明度作用于整体图层，两个表面必须使用相同亮度标度。
    /// </summary>
    public void Composite(LinearRenderSurface source, Vector2 origin, float opacity)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.isDisposed, source);
        RenderValidation.Point(origin, nameof(origin));
        RenderValidation.UnitInterval(opacity, nameof(opacity));
        if (!source.Info.ReferenceWhiteNits.Equals(Info.ReferenceWhiteNits))
        {
            throw new ArgumentException("Reference-white luminance scales must match before composition.", nameof(source));
        }

        using var snapshot = source.surface.Snapshot();
        using var paint = CreatePaint(new(1, 1, 1, opacity));
        surface.Canvas.DrawImage(snapshot, origin.X, origin.Y, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    /// <summary>
    /// 将原始预乘 RGBA Half 像素逐行复制到调用方缓冲；上到下紧密排列，不解预乘或转换为 8 位。
    /// </summary>
    public void CopyPixels(Span<Half> destination)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (destination.Length < Info.ChannelCount)
        {
            throw new ArgumentException("The destination buffer cannot contain all pixels.", nameof(destination));
        }

        using var pixels = surface.PeekPixels();
        if (pixels is null)
        {
            CopyGpuPixels(destination);
            return;
        }
        var bytes = pixels.GetPixelSpan();
        var rowChannels = Info.Width * 4;
        for (var row = 0; row < Info.Height; row++)
        {
            MemoryMarshal.Cast<byte, Half>(bytes.Slice(row * pixels.RowBytes, Info.RowBytes))
                .CopyTo(destination.Slice(row * rowChannels, rowChannels));
        }
    }

    private unsafe void CopyGpuPixels(Span<Half> destination)
    {
        fixed (Half* buffer = destination)
        {
            if (!surface.ReadPixels(new(Info.Width, Info.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace),
                    (nint)buffer, Info.RowBytes, 0, 0))
            {
                throw new InvalidOperationException("The GPU offscreen pixels could not be read.");
            }
        }
    }

    /// <summary>
    /// 将 F16 样本转换为紧密排列的预乘线性 RGBA float，保留负值与扩展亮度；调用方拥有目标缓冲。
    /// </summary>
    public unsafe void CopyPixels(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (destination.Length < Info.ChannelCount)
        {
            throw new ArgumentException("The destination buffer cannot contain all pixels.", nameof(destination));
        }

        var info = new SKImageInfo(Info.Width, Info.Height, SKColorType.RgbaF32, SKAlphaType.Premul, colorSpace);
        fixed (float* pixels = destination)
        {
            if (!surface.ReadPixels(info, (nint)pixels, checked(Info.Width * 4 * sizeof(float)), 0, 0))
            {
                throw new InvalidOperationException("The linear float pixels could not be read.");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        srgbReadback?.Dispose();
        extendedColorShader?.Dispose();
        surface.Dispose();
        colorSpace.Dispose();
        isDisposed = true;
    }

    private SKPaint CreatePaint(LinearColor color)
    {
        var paint = new SKPaint();
        try
        {
            paint.IsAntialias = true;
            paint.BlendMode = SKBlendMode.SrcOver;
            paint.SetColor(new(color.Red, color.Green, color.Blue, color.Alpha), colorSpace);
            if (graphicsContext is not null && (color.Red is < 0 or > 1 || color.Green is < 0 or > 1 || color.Blue is < 0 or > 1))
            {
                extendedColorShader ??= new();
                extendedColorShader.Apply(paint, color.Red, color.Green, color.Blue, color.Alpha);
            }
            return paint;
        }
        catch
        {
            paint.Dispose();
            throw;
        }
    }

    private static SKRect GetBounds(Vector2 origin, Vector2 size)
    {
        RenderValidation.Point(origin, nameof(origin));
        RenderValidation.Point(size, nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.X, nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Y, nameof(size));
        var end = origin + size;
        RenderValidation.Point(end, nameof(size));
        return new(origin.X, origin.Y, end.X, end.Y);
    }
}
