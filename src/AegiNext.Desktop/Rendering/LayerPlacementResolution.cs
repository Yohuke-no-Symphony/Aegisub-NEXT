using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Rendering;

internal sealed record LayerPlacementResolution(ScenePoint? BasePosition, SubtitlePosition? Position,
    Exception? Error = null, LayerPositionDraftGeometry? Geometry = null);
