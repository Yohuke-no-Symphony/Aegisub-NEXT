namespace AegiNext.Core.Projects;

/// <summary>蒙版创建时捕获的固定局部定位框；节点编辑和形变动画不改变此参考框。</summary>
public sealed record MaskReferenceFrame
{
    public ScenePoint Origin { get; init; }
    public ScenePoint Size { get; init; } = new(1, 1);
}
