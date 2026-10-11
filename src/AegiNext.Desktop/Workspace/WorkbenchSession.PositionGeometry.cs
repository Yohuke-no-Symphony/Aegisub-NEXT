using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private LayerTransform PositionGeometryTransform(ProjectLayer layer)
    {
        var localTime = AnimationTarget is { } target && target.LayerId == layer.Id
            ? target.LocalTime
            : LayerAnimationTiming.ClampTime(layer, ProjectPosition - layer.Start + layer.AnimationOffset);
        return SceneEvaluator.EvaluateLayer(layer, null, localTime).Transform;
    }
}
