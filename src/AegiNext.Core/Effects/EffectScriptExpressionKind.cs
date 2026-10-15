namespace AegiNext.Core.Effects;

/// <summary>无副作用表达式节点类型。</summary>
public enum EffectScriptExpressionKind
{
    LITERAL,
    NAME,
    UNARY,
    BINARY,
    CALL
}
