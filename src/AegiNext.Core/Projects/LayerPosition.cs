namespace AegiNext.Core.Projects;

/// <summary>以父画布归一化锚点、内容归一化轴心和像素偏移定位图层，不改变内容尺寸。</summary>
public record LayerPosition
{
    public ScenePoint Anchor { get; init; }
    public ScenePoint Pivot { get; init; }
    public ScenePoint Offset { get; init; }
}
