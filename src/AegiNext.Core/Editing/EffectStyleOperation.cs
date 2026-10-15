using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>已采样的文字样式变换；每个字素在自己的上游样式上执行，保留混合富文本。</summary>
public sealed record EffectStyleOperation(AnimationTrackTarget Target, int Utf16Start, int Utf16Length,
    AnimationValue Multiplier, AnimationValue Offset, Guid InstanceId, int Line, int Column)
{
    public double NativeToCanvasScale { get; init; } = 1;

    /// <summary>在上游当前属性上应用已采样的仿射值，并验证最终属性范围。</summary>
    public AnimationValue Apply(AnimationValue input)
    {
        var nativeInput = SubtitleStyleScaling.ScaleDimensions(Target.Property, input, 1 / NativeToCanvasScale);
        var nativeOffset = SubtitleStyleScaling.ScaleDimensions(Target.Property, Offset, 1 / NativeToCanvasScale);
        var result = EffectValueEvaluation.Apply(nativeInput, Multiplier, nativeOffset, Target.Property, InstanceId, Line, Column);
        return SubtitleStyleScaling.ScaleDimensions(Target.Property, result, NativeToCanvasScale);
    }
}
