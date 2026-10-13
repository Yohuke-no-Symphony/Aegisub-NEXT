using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>单个动画属性的标量、二维或三维向量以及直通线性 RGBA 值。</summary>
[JsonConverter(typeof(AnimationValueJsonConverter))]
public readonly record struct AnimationValue
{
    private readonly double scalar;
    private readonly ScenePoint vector;
    private readonly SceneVector3 vector3;
    private readonly SceneColor color;

    private AnimationValue(double scalar, ScenePoint vector, SceneVector3 vector3, SceneColor color, AnimationValueKind kind)
    {
        this.scalar = scalar;
        this.vector = vector;
        this.vector3 = vector3;
        this.color = color;
        Kind = kind;
    }

    public AnimationValueKind Kind { get; }
    public bool IsVector => Kind == AnimationValueKind.VECTOR;
    public bool IsVector3 => Kind == AnimationValueKind.VECTOR3;
    public bool IsColor => Kind == AnimationValueKind.COLOR;
    public int ComponentCount => Kind switch
    {
        AnimationValueKind.SCALAR => 1,
        AnimationValueKind.VECTOR => 2,
        AnimationValueKind.VECTOR3 => 3,
        AnimationValueKind.COLOR => 4,
        _ => throw new InvalidOperationException("Unknown animation value dimensions.")
    };
    public double Scalar => AsScalar();
    public ScenePoint Vector => AsVector();
    public SceneVector3 Vector3 => AsVector3();
    public SceneColor Color => AsColor();

    /// <summary>创建标量动画值。</summary>
    public static AnimationValue FromScalar(double value) => new(value, default, default, default, AnimationValueKind.SCALAR);

    /// <summary>创建二维向量动画值。</summary>
    public static AnimationValue FromVector(ScenePoint value) => new(default, value, default, default, AnimationValueKind.VECTOR);

    /// <summary>创建完整三维向量动画值。</summary>
    public static AnimationValue FromVector3(SceneVector3 value) => new(default, default, value, default, AnimationValueKind.VECTOR3);

    /// <summary>创建完整直通线性 RGBA 动画值。</summary>
    public static AnimationValue FromColor(SceneColor value) => new(default, default, default, value, AnimationValueKind.COLOR);

    /// <summary>读取标量；读取错误维度明确失败。</summary>
    public double AsScalar() => Kind == AnimationValueKind.SCALAR ? scalar : throw new InvalidOperationException("The animation value is not a scalar.");

    /// <summary>读取二维向量；读取错误维度明确失败。</summary>
    public ScenePoint AsVector() => IsVector ? vector : throw new InvalidOperationException("The animation value is not a two-dimensional vector.");

    /// <summary>读取三维向量；读取错误维度明确失败。</summary>
    public SceneVector3 AsVector3() => IsVector3 ? vector3 : throw new InvalidOperationException("The animation value is not a three-dimensional vector.");

    /// <summary>读取直通线性 RGBA；读取错误维度明确失败。</summary>
    public SceneColor AsColor() => IsColor ? color : throw new InvalidOperationException("The animation value is not a color.");

    /// <summary>读取维度范围内的一个分量。</summary>
    public double GetComponent(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ComponentCount);
        return Kind switch
        {
            AnimationValueKind.SCALAR => scalar,
            AnimationValueKind.VECTOR => index == 0 ? vector.X : vector.Y,
            AnimationValueKind.VECTOR3 => index switch { 0 => vector3.X, 1 => vector3.Y, _ => vector3.Z },
            _ => index switch { 0 => color.Red, 1 => color.Green, 2 => color.Blue, _ => color.Alpha }
        };
    }

    /// <summary>替换一个分量并保留其余分量和完整维度。</summary>
    public AnimationValue WithComponent(int index, double value)
    {
        _ = GetComponent(index);
        return Kind switch
        {
            AnimationValueKind.SCALAR => FromScalar(value),
            AnimationValueKind.VECTOR => FromVector(index == 0 ? vector with { X = value } : vector with { Y = value }),
            AnimationValueKind.VECTOR3 => FromVector3(index switch
            {
                0 => vector3 with { X = value },
                1 => vector3 with { Y = value },
                _ => vector3 with { Z = value }
            }),
            _ => FromColor(index switch
            {
                0 => color with { Red = value },
                1 => color with { Green = value },
                2 => color with { Blue = value },
                _ => color with { Alpha = value }
            })
        };
    }

    /// <summary>将标量转换为带维度的动画值。</summary>
    public static implicit operator AnimationValue(double value) => FromScalar(value);

    /// <summary>将二维向量转换为带维度的动画值。</summary>
    public static implicit operator AnimationValue(ScenePoint value) => FromVector(value);

    /// <summary>将三维向量转换为带维度的动画值。</summary>
    public static implicit operator AnimationValue(SceneVector3 value) => FromVector3(value);

    /// <summary>将线性 RGBA 转换为带维度的动画值。</summary>
    public static implicit operator AnimationValue(SceneColor value) => FromColor(value);

    /// <summary>按相同维度直接插值；颜色不预乘、不编码为 sRGB、不裁掉 HDR 范围。</summary>
    public static AnimationValue Lerp(AnimationValue first, AnimationValue second, double fraction)
    {
        if (first.Kind != second.Kind)
        {
            throw new InvalidOperationException("Animation value dimensions do not match.");
        }

        var result = first;
        for (var index = 0; index < first.ComponentCount; index++)
        {
            var a = first.GetComponent(index);
            result = result.WithComponent(index, a + (second.GetComponent(index) - a) * fraction);
        }

        return result;
    }
}
