namespace AegiNext.Core.Projects;

/// <summary>时间轴特效块的稳定身份，由所属片段与完整动画目标共同确定。</summary>
public sealed record TimelineEffectClipId(Guid LayerId, AnimationTrackTarget Target, Guid? InstanceId = null);
