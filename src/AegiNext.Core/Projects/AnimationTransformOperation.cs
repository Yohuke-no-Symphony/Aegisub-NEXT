using AegiNext.Core.Timing;
using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>按轨道数组顺序求值的原生变换操作；稳定标识用于编辑，时间使用内容时钟。</summary>
public sealed record AnimationTransformOperation(Guid Id, MediaTime Start, MediaTime End, AnimationValue Value, double Acceleration = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ComponentMask { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AnimationTransformMode Mode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnimationCurve? Curve { get; init; }
}
