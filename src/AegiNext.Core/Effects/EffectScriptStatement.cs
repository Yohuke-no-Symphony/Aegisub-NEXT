using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>保留源位置及表达式的模板声明；控制流仅在实例编译时展开。</summary>
public sealed record EffectScriptStatement(EffectScriptStatementKind Kind, string Name, string Text,
    ImmutableArray<EffectScriptExpression> Expressions, ImmutableArray<EffectScriptStatement> Statements,
    int Line, int Column)
{
    public ImmutableArray<EffectScriptStatement> Alternatives { get; init; } = [];
    public bool HasCycle { get; init; }
    public bool HasRepeat { get; init; }
    public bool PingPong { get; init; }
    public EffectScriptValueKind ValueKind { get; init; }
}
