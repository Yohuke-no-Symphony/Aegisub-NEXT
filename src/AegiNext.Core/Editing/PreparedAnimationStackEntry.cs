using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal sealed record PreparedAnimationStackEntry(AnimationTrack? Track = null, PreparedEffectInstance? Instance = null);
