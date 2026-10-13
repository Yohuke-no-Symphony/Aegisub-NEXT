using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private static bool EquivalentMask(ClipMask? first, ClipMask? second)
    {
        if (first == second)
        {
            return true;
        }

        if (first is null || second is null || first.Inverted != second.Inverted || first.Transform != second.Transform)
        {
            return false;
        }

        if (first is RectangleClipMask a && second is RectangleClipMask b)
        {
            return a.TopLeft == b.TopLeft && a.BottomRight == b.BottomRight;
        }

        if (first is not VectorClipMask av || second is not VectorClipMask bv || av.Contours.Length != bv.Contours.Length)
        {
            return false;
        }

        for (var index = 0; index < av.Contours.Length; index++)
        {
            if (av.Contours[index].Id != bv.Contours[index].Id || !av.Contours[index].Nodes.SequenceEqual(bv.Contours[index].Nodes))
            {
                return false;
            }
        }

        return true;
    }

    private static SKPath ClipMaskPath(ClipMask mask, float scaleX, float scaleY)
    {
        return ClipMaskGeometry.Create(mask, scaleX, scaleY);
    }
}
