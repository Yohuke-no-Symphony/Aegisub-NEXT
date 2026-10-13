using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record CanvasSpatialGesture(ProjectDocument Source, Guid LayerId, AnimationTrackTarget Target,
    AnimationProperty HandleProperty, MediaTime LocalTime, bool IsKeyframe,
    LayerTransform Transform, Matrix Fit, Point Center, Point Start, AnimationValue OriginalValue, AnimationValue Value,
    double LastAngle, double AccumulatedAngle);
