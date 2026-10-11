namespace AegiNext.Core.Projects;

/// <summary>参数化图形或单轮廓路径；尺寸及圆角为局部像素，箭头参数为尺寸比例。</summary>
public sealed record LayerShape(ShapeKind Kind, double Width, double Height, PathGeometry? Path = null)
{
    public double CornerRadius { get; init; }
    public double ArrowHeadRatio { get; init; } = 0.3;
    public double ArrowShaftRatio { get; init; } = 0.35;
}
