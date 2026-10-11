using SkiaSharp;

namespace AegiNext.Rendering.Images;

/// <summary>统一图片预检及像素解码，所有方法读取当前位置起的内容并保留输入流所有权。</summary>
public interface IImageDecoder
{
    /// <summary>检测格式与预算，返回尺寸、时长和循环信息。</summary>
    ImageMetadata Probe(Stream stream);

    /// <summary>解码完整合成帧，返回图像由调用方释放。</summary>
    SKImage DecodeFrame(Stream stream, int frameIndex);

    /// <summary>生成首帧等比缩略图，返回图像由调用方释放。</summary>
    SKImage CreateThumbnail(Stream stream, int maximumDimension);
}
