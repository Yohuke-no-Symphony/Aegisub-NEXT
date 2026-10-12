using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssTextAnimationOperation(AssTransformTiming Timing, AnimationValue Value,
    int ComponentMask = 0, AnimationTransformMode Mode = AnimationTransformMode.INTERPOLATE_TO, int KaraokeCandidate = 0,
    bool ClampNonNegative = false, double? Maximum = null, double? NonPositiveFallback = null);
