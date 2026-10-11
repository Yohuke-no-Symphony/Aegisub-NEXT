namespace AegiNext.Rendering.Images;

/// <summary>图片导入、缩略图和场景渲染共用的解码预算。</summary>
public static class ImageDecodeLimits
{
    private const long MAXIMUM_ENCODED_BYTES = 256L * 1024 * 1024;
    private const long MAXIMUM_PIXELS = 33177600;
    private const int MAXIMUM_FRAME_COUNT = 100000;

    public static long MaximumEncodedBytes => MAXIMUM_ENCODED_BYTES;
    public static long MaximumPixels => MAXIMUM_PIXELS;
    public static int MaximumFrameCount => MAXIMUM_FRAME_COUNT;
}
