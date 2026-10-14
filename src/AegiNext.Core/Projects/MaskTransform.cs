namespace AegiNext.Core.Projects;

/// <summary>叠加在蒙版原生定位上的独立动画平移、缩放及角度旋转。</summary>
public sealed record MaskTransform
{
    public ScenePoint Position { get; init; }
    public ScenePoint Scale { get; init; } = new(1, 1);
    public double Rotation { get; init; }
}
