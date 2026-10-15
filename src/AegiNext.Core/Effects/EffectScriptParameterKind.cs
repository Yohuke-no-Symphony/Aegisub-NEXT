namespace AegiNext.Core.Effects;

/// <summary>模板参数的持久化值类型。</summary>
public enum EffectScriptParameterKind
{
    NUMBER,
    DURATION,
    BOOLEAN,
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "DSL 参数类型与持久化标记一致。")]
    STRING,
    VECTOR2,
    VECTOR3,
    COLOR
}
