using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>由一个实例编译的只读临时范围和仿射通道；不保存到原生手工轨道。</summary>
public sealed record CompiledEffectScript(ImmutableArray<SubtitleAnimationRange> Ranges,
    ImmutableArray<CompiledEffectChannel> Channels);
