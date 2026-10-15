namespace AegiNext.Core.Projects;

/// <summary>通过所属片段和实例稳定标识定位一个可调参特效。</summary>
public sealed record EffectInstanceId(Guid LayerId, Guid InstanceId);
