using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

internal sealed record EffectPlanCacheKey(Guid LayerId, Guid InstanceId, string Source, string Parameters,
    MediaTime Start, MediaTime End, MediaTime ClipDuration, MediaTime AnimationOffset, LayerKind Kind,
    Guid? SubtitleId, string Text, AnimationTrackTarget? Context, int RangeStart, int RangeLength, string MaskTopology);
