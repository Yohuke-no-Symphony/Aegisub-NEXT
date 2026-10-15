using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>片段附属的可调参特效实例；时段使用片段内容时钟，脚本保存独立源码快照。</summary>
public sealed record EffectInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Effect";
    public string Source { get; init; } = string.Empty;
    public ImmutableDictionary<string, EffectScriptParameterValue> Parameters { get; init; } =
        ImmutableDictionary<string, EffectScriptParameterValue>.Empty;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; } = new(10);
    public bool IsEnabled { get; init; } = true;
    public AnimationTrackTarget? TargetContext { get; init; }
}
