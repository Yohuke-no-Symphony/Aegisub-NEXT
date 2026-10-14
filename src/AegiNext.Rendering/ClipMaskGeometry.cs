using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering;

internal static class ClipMaskGeometry
{
    internal static SKPath Create(ClipMask mask, ScenePoint canvasSize, float scaleX = 1, float scaleY = 1)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        try
        {
            switch (mask)
            {
                case RectangleClipMask rectangle:
                    path.AddRect(new((float)rectangle.TopLeft.X, (float)rectangle.TopLeft.Y,
                        (float)rectangle.BottomRight.X, (float)rectangle.BottomRight.Y));
                    break;
                case VectorClipMask vector:
                    foreach (var contour in vector.Contours)
                    {
                        var nodes = contour.Nodes;
                        path.MoveTo((float)nodes[0].Position.X, (float)nodes[0].Position.Y);
                        for (var index = 0; index < nodes.Length; index++)
                        {
                            var source = nodes[index];
                            var destination = nodes[(index + 1) % nodes.Length];
                            if (source.OutHandle == default && destination.InHandle == default)
                            {
                                path.LineTo((float)destination.Position.X, (float)destination.Position.Y);
                            }
                            else
                            {
                                path.CubicTo((float)(source.Position.X + source.OutHandle.X),
                                    (float)(source.Position.Y + source.OutHandle.Y),
                                    (float)(destination.Position.X + destination.InHandle.X),
                                    (float)(destination.Position.Y + destination.InHandle.Y),
                                    (float)destination.Position.X, (float)destination.Position.Y);
                            }
                        }
                        path.Close();
                    }
                    break;
                default:
                    throw new InvalidDataException("Unknown subtitle mask shape.");
            }
            var placement = ClipMaskPlacementGeometry.CreateMatrix(mask, canvasSize);
            var matrix = new SKMatrix(
                (float)(placement.M11 * scaleX), (float)(placement.M12 * scaleX), (float)(placement.M13 * scaleX),
                (float)(placement.M21 * scaleY), (float)(placement.M22 * scaleY), (float)(placement.M23 * scaleY),
                (float)placement.M31, (float)placement.M32, (float)placement.M33);
            path.Transform(matrix);
            return path;
        }
        catch
        {
            path.Dispose();
            throw;
        }
    }
}
