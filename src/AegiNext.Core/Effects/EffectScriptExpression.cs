using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>编译期求值表达式及其精确源位置。</summary>
public sealed record EffectScriptExpression(EffectScriptExpressionKind Kind, string Text,
    ImmutableArray<EffectScriptExpression> Arguments, int Line, int Column)
{
    internal EffectScriptRational? ExactNumber { get; init; }
    public EffectScriptParameterValue? Literal { get; init; }
}
