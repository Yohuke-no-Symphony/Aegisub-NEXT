namespace AegiNext.Core.Projects;

/// <summary>稳定蒙版节点；位置使用蒙版局部坐标，两个控制柄保存相对节点的偏移。</summary>
public sealed record MaskNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ScenePoint Position { get; init; }
    public ScenePoint InHandle { get; init; }
    public ScenePoint OutHandle { get; init; }
}
