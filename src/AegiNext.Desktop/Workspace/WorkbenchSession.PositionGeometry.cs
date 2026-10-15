using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly EffectCompilationCache geometryEffectPlans = new();

    internal ClipMask? EvaluateMask(ProjectLayer layer, MediaTime contentTime)
    {
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        return SceneEvaluator.EvaluateMask(layer, contentTime, subtitle, geometryEffectPlans);
    }

    private LayerTransform PositionGeometryTransform(ProjectLayer layer)
    {
        var localTime = AnimationTarget is { } target && target.LayerId == layer.Id
            ? target.LocalTime
            : LayerAnimationTiming.ClampTime(layer, ProjectPosition - layer.Start + layer.AnimationOffset);
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        return SceneEvaluator.EvaluateLayer(layer, subtitle, localTime, geometryEffectPlans).Transform;
    }
}
