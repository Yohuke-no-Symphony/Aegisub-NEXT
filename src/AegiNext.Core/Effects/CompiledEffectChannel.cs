using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>覆盖区间内的上游仿射操作：结果为 Multiplier(t) × 上游当前值 + Offset(t)。</summary>
public sealed record CompiledEffectChannel(AnimationTrack Multiplier, AnimationTrack Offset,
    MediaTime Start, MediaTime End, int Line, int Column)
{
    public AnimationTrackTarget Target => Multiplier.Target;
}
