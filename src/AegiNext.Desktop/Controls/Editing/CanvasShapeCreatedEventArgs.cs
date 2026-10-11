using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>工程坐标中的新形状范围；自由路径使用相对于 Position 的局部轮廓。</summary>
public sealed class CanvasShapeCreatedEventArgs(ShapeKind kind, ScenePoint position, double width, double height,
    PathGeometry? path = null) : EventArgs
{
    public ShapeKind Kind { get; } = kind;
    public ScenePoint Position { get; } = position;
    public double Width { get; } = width;
    public double Height { get; } = height;
    public PathGeometry? Path { get; } = path;
}
