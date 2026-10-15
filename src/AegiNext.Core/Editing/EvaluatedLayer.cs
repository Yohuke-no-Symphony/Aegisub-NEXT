using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using System.Collections.Immutable;

namespace AegiNext.Core.Editing;

/// <summary>某一工程时刻的平面片段结果，保留片段的局部效果和蒙版。</summary>
public sealed record EvaluatedLayer(ProjectLayer Source, MediaTime LocalTime, LayerTransform Transform,
    double Opacity, SceneColor Fill, SceneColor Stroke, double StrokeWidth, double Blur,
    SubtitleLine? Subtitle)
{
    public ClipMask? Mask { get; init; }
    public bool HasFillAnimation { get; init; }
    public bool HasStrokeAnimation { get; init; }
    public bool HasStrokeWidthAnimation { get; init; }
    public double LetterSpacing { get; init; }
    public double FillBlur { get; init; }
    public double StrokeBlur { get; init; }
    public bool HasLetterSpacingAnimation { get; init; }
    public bool HasFillBlurAnimation { get; init; }
    public bool HasStrokeBlurAnimation { get; init; }
    public double FontSize { get; init; }
    public ScenePoint ShadowOffset { get; init; }
    public double ShadowBlur { get; init; }
    public SceneColor ShadowColor { get; init; }
    public bool HasFontSizeAnimation { get; init; }
    public bool HasShadowOffsetAnimation { get; init; }
    public bool HasShadowBlurAnimation { get; init; }
    public bool HasShadowColorAnimation { get; init; }
    public ImmutableDictionary<AnimationTrackTarget, AnimationValue> AnimationValues { get; init; } = ImmutableDictionary<AnimationTrackTarget, AnimationValue>.Empty;
    public ImmutableArray<SubtitleAnimationRange> AnimationRanges { get; init; } = [];
    public ImmutableArray<EffectStyleOperation> EffectStyleOperations { get; init; } = [];
}
