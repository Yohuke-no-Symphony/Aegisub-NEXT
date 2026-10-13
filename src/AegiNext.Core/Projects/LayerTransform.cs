using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>将局部轴心平移至原点后缩放、旋转并定位；父组变换在外层应用。</summary>
public sealed record LayerTransform
{
    /// <summary>创建默认变换。</summary>
    [JsonConstructor]
    public LayerTransform()
    {
    }

    /// <summary>从既有分量调用创建向量变换；持久化仅保存向量。</summary>
    public LayerTransform(double X = 0, double Y = 0, double ScaleX = 1, double ScaleY = 1,
        double Rotation = 0, double AnchorX = 0, double AnchorY = 0)
    {
        Position = new(X, Y);
        Scale = new(ScaleX, ScaleY);
        Pivot = new(AnchorX, AnchorY);
        this.Rotation = Rotation;
    }

    public ScenePoint Position { get; init; }
    public ScenePoint Scale { get; init; } = new(1, 1);
    public ScenePoint Pivot { get; init; }
    public double Rotation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double RotationX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double RotationY { get; init; }
    public double PerspectiveDistance { get; init; } = SceneProjection.DEFAULT_DISTANCE;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SceneVector3 ProjectionTranslation { get; init; }

    [JsonIgnore]
    public double X
    {
        get => Position.X;
        init => Position = Position with { X = value };
    }

    [JsonIgnore]
    public double Y
    {
        get => Position.Y;
        init => Position = Position with { Y = value };
    }

    [JsonIgnore]
    public double ScaleX
    {
        get => Scale.X;
        init => Scale = Scale with { X = value };
    }

    [JsonIgnore]
    public double ScaleY
    {
        get => Scale.Y;
        init => Scale = Scale with { Y = value };
    }

    [JsonIgnore]
    public double AnchorX
    {
        get => Pivot.X;
        init => Pivot = Pivot with { X = value };
    }

    [JsonIgnore]
    public double AnchorY
    {
        get => Pivot.Y;
        init => Pivot = Pivot with { Y = value };
    }
}
