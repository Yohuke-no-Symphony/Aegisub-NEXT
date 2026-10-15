using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>片段内一条时间轴动画行的稳定身份；完整原生目标与特效实例标识二选一。</summary>
public sealed record TimelineEffectRowIdentity(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AnimationTrackTarget? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? InstanceId = null)
{
    /// <summary>验证身份形状，不要求暂缺的显示目标仍然存在。</summary>
    public void Validate()
    {
        if (Target.HasValue == InstanceId.HasValue || InstanceId == Guid.Empty)
        {
            throw new InvalidDataException("动画行身份必须包含一个完整目标或一个有效特效实例标识。");
        }
        if (Target is { } target)
        {
            SubtitleAnimationTargetValidation.ValidateIdentity(target);
        }
    }
}
