using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>矩形按顺时针顺序保存的四角局部像素半径。</summary>
public sealed record ShapeCornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    [JsonIgnore]
    public bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

    /// <summary>创建四角相同的半径。</summary>
    public static ShapeCornerRadii Uniform(double radius) => new(radius, radius, radius, radius);
}
