using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>把声明默认值及应用参数绑定为经过验证的不可变输入。</summary>
public static class EffectScriptParameterBinder
{
    /// <summary>填充默认值，拒绝未知参数、类型不匹配及不属于严格选项的值。</summary>
    public static ImmutableDictionary<string, EffectScriptParameterValue> Bind(EffectScript script,
        IReadOnlyDictionary<string, EffectScriptParameterValue>? values = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        var result = ImmutableDictionary.CreateBuilder<string, EffectScriptParameterValue>(StringComparer.Ordinal);
        foreach (var parameter in script.Parameters)
        {
            var value = values is not null && values.TryGetValue(parameter.Name, out var supplied) ? supplied : parameter.DefaultValue;
            Validate(value, parameter.Line, parameter.Column);
            if (value.Kind != parameter.Kind || !parameter.Options.IsEmpty && !parameter.Options.Any(option => option.Value == value))
            {
                throw new EffectScriptException($"参数 {parameter.Name} 类型不匹配或不属于 options。", parameter.Line, parameter.Column);
            }
            result.Add(parameter.Name, value);
        }
        if (values is not null && values.Keys.Any(key => !result.ContainsKey(key)))
        {
            throw new EffectScriptException("存在未声明的参数。");
        }
        return result.ToImmutable();
    }

    /// <summary>验证独立参数值的标签、维度及有限性。</summary>
    public static void Validate(EffectScriptParameterValue value, int line = 0, int column = 1)
    {
        if (value is null || !Enum.IsDefined(value.Kind))
        {
            throw new EffectScriptException("参数值或类型无效。", line, column);
        }
        var animationKind = value.Kind switch
        {
            EffectScriptParameterKind.VECTOR2 => AnimationValueKind.VECTOR,
            EffectScriptParameterKind.VECTOR3 => AnimationValueKind.VECTOR3,
            EffectScriptParameterKind.COLOR => AnimationValueKind.COLOR,
            _ => (AnimationValueKind?)null
        };
        if (animationKind is { } expected && (value.Animation is not { } animation || animation.Kind != expected ||
            Enumerable.Range(0, animation.ComponentCount).Any(index => !double.IsFinite(animation.GetComponent(index)))) ||
            animationKind is null && value.Animation.HasValue || value.Kind == EffectScriptParameterKind.STRING && value.Text is null ||
            value.Kind != EffectScriptParameterKind.STRING && value.Text is not null ||
            value.Kind != EffectScriptParameterKind.NUMBER && value.Number != 0 ||
            value.Kind != EffectScriptParameterKind.DURATION && value.Duration != default ||
            value.Kind != EffectScriptParameterKind.BOOLEAN && value.Boolean)
        {
            throw new EffectScriptException("参数载荷与声明类型不匹配。", line, column);
        }
        if (value.Text is { } text)
        {
            try
            {
                ProjectValidator.ValidateText(text);
            }
            catch (InvalidDataException error)
            {
                throw new EffectScriptException("参数文本包含无效 Unicode。", line, column, error);
            }
        }
    }
}
