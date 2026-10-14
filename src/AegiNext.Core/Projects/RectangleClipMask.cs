namespace AegiNext.Core.Projects;

/// <summary>以蒙版局部坐标中的左上角及右下角定义的矩形裁切。</summary>
public sealed record RectangleClipMask : ClipMask
{
    public ScenePoint TopLeft { get; init; }
    public ScenePoint BottomRight { get; init; }
}
