using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleScalingProjection(SubtitleLine Source, SubtitleLine Subtitle, double Scale)
{
    internal ImmutableDictionary<AnimationTrackTarget, AnimationValue> Values { get; init; } = ImmutableDictionary<AnimationTrackTarget, AnimationValue>.Empty;
    internal ImmutableDictionary<AnimationTrackTarget, AnimationValue> CanvasValues { get; init; } = ImmutableDictionary<AnimationTrackTarget, AnimationValue>.Empty;
}
