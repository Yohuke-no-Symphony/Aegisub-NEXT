using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record CanvasTransformGesture(ProjectLayer Layer, LayerTransform EvaluatedTransform,
    ProjectLayerGeometry Geometry, Matrix LocalToScreen, Point Start, int HandleIndex);
