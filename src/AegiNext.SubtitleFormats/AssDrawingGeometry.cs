using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssDrawingGeometry(ImmutableArray<MaskContour> Contours, ScenePoint BoundsMinimum,
    ScenePoint BoundsMaximum, int PointCount)
{
    internal bool HasDrawableGeometry => !Contours.IsDefaultOrEmpty;
}
