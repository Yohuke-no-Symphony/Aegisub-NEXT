namespace AegiNext.Export;

/// <summary>成片实际编码尺寸和编码器报告的补边；补边保留在输出像素中，不表示裁剪。</summary>
public sealed record VideoExportGeometry(int Width, int Height, int PaddingTop = 0, int PaddingBottom = 0,
    int PaddingLeft = 0, int PaddingRight = 0);
