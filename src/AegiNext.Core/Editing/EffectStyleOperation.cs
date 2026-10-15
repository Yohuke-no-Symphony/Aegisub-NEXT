using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>按统一栈顺序采样的文字样式操作；每个字素保留自己的上游富文本样式。</summary>
public sealed record EffectStyleOperation(AnimationTrackTarget Target, int Utf16Start, int Utf16Length,
    AnimationValue Multiplier, AnimationValue Offset, Guid? InstanceId, int Line, int Column)
{
    public double NativeToCanvasScale { get; init; } = 1;
    public bool IsNativeAssignment => InstanceId is null;

    /// <summary>在上游当前属性上应用已采样的仿射值，并验证最终属性范围。</summary>
    public AnimationValue Apply(AnimationValue input)
    {
        if (IsNativeAssignment)
        {
            return Offset;
        }
        var nativeInput = SubtitleStyleScaling.ScaleDimensions(Target.Property, input, 1 / NativeToCanvasScale);
        var nativeOffset = SubtitleStyleScaling.ScaleDimensions(Target.Property, Offset, 1 / NativeToCanvasScale);
        var result = EffectValueEvaluation.Apply(nativeInput, Multiplier, nativeOffset, Target.Property, InstanceId!.Value, Line, Column);
        return SubtitleStyleScaling.ScaleDimensions(Target.Property, result, NativeToCanvasScale);
    }
}
