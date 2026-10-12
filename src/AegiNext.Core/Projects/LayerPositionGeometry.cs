namespace AegiNext.Core.Projects;

/// <summary>字幕、图片和形状共用的位置、轴心和保位运算，不依赖渲染或界面框架。</summary>
public static class LayerPositionGeometry
{
    /// <summary>把父画布归一化锚点和偏移解析为变换前的父局部位置。</summary>
    public static ScenePoint BasePosition(LayerPosition position, ScenePoint parentSize)
    {
        return new(position.Anchor.X * parentSize.X + position.Offset.X,
            position.Anchor.Y * parentSize.Y + position.Offset.Y);
    }

    /// <summary>把内容归一化轴心解析为局部像素轴心；旧图层变换中的像素轴心由调用方另外叠加。</summary>
    public static ScenePoint LocalPivot(LayerPosition position, ScenePoint origin, ScenePoint size)
    {
        return new(origin.X + position.Pivot.X * size.X, origin.Y + position.Pivot.Y * size.Y);
    }

    /// <summary>把归一化轴心改变量转换为已缩放、旋转的父局部像素位移，用于切换轴心时保位。</summary>
    public static ScenePoint PivotDisplacement(ScenePoint previous, ScenePoint next, ScenePoint size,
        LayerTransform transform)
    {
        var x = (next.X - previous.X) * size.X * transform.ScaleX;
        var y = (next.Y - previous.Y) * size.Y * transform.ScaleY;
        var angle = transform.Rotation * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return new(cosine * x - sine * y, sine * x + cosine * y);
    }

    /// <summary>取得图片或形状的逻辑尺寸；文字须使用实际排版测量而非本方法。</summary>
    public static ScenePoint GraphicSize(ProjectLayer layer)
    {
        return layer.Kind switch
        {
            LayerKind.IMAGE when layer.Image is { } image => new(image.Width, image.Height),
            LayerKind.SHAPE when layer.Shape is { } shape => new(shape.Width, shape.Height),
            _ => throw new ArgumentException("The layer has no logical image or shape dimensions.", nameof(layer))
        };
    }
}
