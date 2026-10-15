using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>带显式类型的不可变参数值；表达式计算使用精确有理数。</summary>
public sealed record EffectScriptParameterValue(EffectScriptParameterKind Kind)
{
    public decimal Number { get; init; }
    public MediaTime Duration { get; init; }
    public bool Boolean { get; init; }
    public string? Text { get; init; }
    public AnimationValue? Animation { get; init; }

    /// <summary>创建数值参数。</summary>
    public static EffectScriptParameterValue FromNumber(decimal value) => new(EffectScriptParameterKind.NUMBER) { Number = value };

    /// <summary>创建精确时长参数。</summary>
    public static EffectScriptParameterValue FromDuration(MediaTime value) => new(EffectScriptParameterKind.DURATION) { Duration = value };

    /// <summary>创建布尔参数。</summary>
    public static EffectScriptParameterValue FromBoolean(bool value) => new(EffectScriptParameterKind.BOOLEAN) { Boolean = value };

    /// <summary>创建文本或枚举参数。</summary>
    public static EffectScriptParameterValue FromString(string value) => new(EffectScriptParameterKind.STRING) { Text = value };

    /// <summary>创建向量或线性颜色参数。</summary>
    public static EffectScriptParameterValue FromAnimation(AnimationValue value) => new(value.Kind switch
    {
        AnimationValueKind.VECTOR => EffectScriptParameterKind.VECTOR2,
        AnimationValueKind.VECTOR3 => EffectScriptParameterKind.VECTOR3,
        AnimationValueKind.COLOR => EffectScriptParameterKind.COLOR,
        _ => throw new ArgumentException("参数动画值应为向量或颜色。", nameof(value))
    }) { Animation = value };

    /// <summary>解析不含变量的参数字面量。</summary>
    public static EffectScriptParameterValue Parse(string literal)
    {
        try
        {
            return EffectScriptV3Parser.ParseConstant(literal);
        }
        catch (Exception error) when (error is OverflowException or DivideByZeroException or InvalidOperationException or System.Text.Json.JsonException or InvalidDataException)
        {
            throw new EffectScriptException("参数字面量无效：" + error.Message, innerException: error);
        }
    }

    /// <summary>输出可重新解析的参数字面量。</summary>
    public string ToSource()
    {
        return Kind switch
        {
            EffectScriptParameterKind.NUMBER => Number.ToString("G29", CultureInfo.InvariantCulture),
            EffectScriptParameterKind.DURATION => FormatDuration(),
            EffectScriptParameterKind.BOOLEAN => Boolean ? "true" : "false",
            EffectScriptParameterKind.STRING => System.Text.Json.JsonSerializer.Serialize(Text),
            _ => (Kind == EffectScriptParameterKind.COLOR ? "rgba" : Kind == EffectScriptParameterKind.VECTOR3 ? "vec3" : "vec2") +
                "(" + string.Join(", ", Enumerable.Range(0, Animation!.Value.ComponentCount)
                    .Select(index => FormatComponent(Animation.Value.GetComponent(index)))) + ")"
        };
    }

    private string FormatDuration()
    {
        var seconds = (decimal)Duration.Numerator / Duration.Denominator;
        return EffectScriptRational.From(seconds) == EffectScriptRational.From(Duration)
            ? seconds.ToString("G29", CultureInfo.InvariantCulture) + "s"
            : string.Create(CultureInfo.InvariantCulture, $"({Duration.Numerator}s / {Duration.Denominator})");
    }

    private static string FormatComponent(double value) => value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
}
