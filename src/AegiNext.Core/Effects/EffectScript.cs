using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>声明式特效脚本；仅描述时间段和属性关键帧，不执行通用代码。</summary>
public sealed record EffectScript(string Id, EffectScriptShortClipPolicy ShortClipPolicy,
    ImmutableArray<EffectScriptSegment> Segments)
{
    public ImmutableArray<EffectScriptParameter> Parameters { get; init; } = [];
    public ImmutableArray<EffectScriptStatement> Statements { get; init; } = [];
    public int Version { get; init; } = 1;
    public ImmutableArray<EffectScriptScope> Scopes { get; init; } = [];
}
