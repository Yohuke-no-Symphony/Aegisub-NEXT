using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssMaskDrawing
{
    internal static VectorClipMask? Parse(string drawing, bool inverted, double scaleX, double scaleY)
    {
        var geometry = AssDrawingParser.Parse(drawing);
        if (!geometry.HasDrawableGeometry)
        {
            return null;
        }
        var contours = geometry.Contours.Select(contour => contour with
        {
            Nodes = contour.Nodes.Select(node => node with
            {
                Position = Scale(node.Position), InHandle = Scale(node.InHandle), OutHandle = Scale(node.OutHandle)
            }).ToImmutableArray()
        }).ToImmutableArray();
        var nodes = contours.SelectMany(contour => contour.Nodes).ToArray();
        return new()
        {
            Inverted = inverted,
            Contours = contours,
            Transform = new()
            {
                Pivot = new((nodes.Min(node => node.Position.X) + nodes.Max(node => node.Position.X)) / 2,
                    (nodes.Min(node => node.Position.Y) + nodes.Max(node => node.Position.Y)) / 2)
            }
        };

        ScenePoint Scale(ScenePoint point)
        {
            var result = new ScenePoint(point.X * scaleX, point.Y * scaleY);
            if (!double.IsFinite(result.X) || !double.IsFinite(result.Y) || Math.Abs(result.X) > 1e9 || Math.Abs(result.Y) > 1e9)
            {
                throw new InvalidDataException("Resampled ASS drawing exceeds the native coordinate range.");
            }
            return result;
        }
    }
}
