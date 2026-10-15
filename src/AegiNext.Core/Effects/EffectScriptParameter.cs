using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>源码声明的参数默认值和选项；Options 限定输入，Suggestions 允许同类型自定义值。</summary>
public sealed record EffectScriptParameter(string Name, EffectScriptParameterValue DefaultValue,
    ImmutableArray<EffectScriptParameterChoice> Options, ImmutableArray<EffectScriptParameterChoice> Suggestions,
    int Line = 0, int Column = 1)
{
    public string? Label { get; init; }
    public EffectScriptParameterKind Kind => DefaultValue.Kind;
}
