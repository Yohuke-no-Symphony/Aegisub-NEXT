using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

/// <summary>编辑锚点所需的父画布、内容逻辑边界和局部变换；不持有任何渲染资源。</summary>
public sealed record LayerPositionDraftGeometry(ScenePoint ParentSize, ScenePoint GlyphOrigin,
    ScenePoint GlyphSize, LayerTransform Transform, bool HasInk = true)
{
    /// <summary>偏移参考单位到父画布像素的比例；图片与绘图使用默认值一。</summary>
    public double OffsetScale { get; init; } = 1;

    /// <summary>将规范化 pivot 的改变量转换为旋转、缩放后的偏移参考单位。</summary>
    public ScenePoint PivotDisplacement(ScenePoint previous, ScenePoint next)
    {
        var displacement = LayerPositionGeometry.PivotDisplacement(previous, next, GlyphSize, Transform);
        return new(displacement.X / OffsetScale, displacement.Y / OffsetScale);
    }
}
