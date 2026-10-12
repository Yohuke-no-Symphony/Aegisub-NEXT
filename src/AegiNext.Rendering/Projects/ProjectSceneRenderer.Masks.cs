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

            var transform = mask.Transform;
            var matrix = SKMatrix.CreateTranslation((float)(transform.Pivot.X + transform.Position.X),
                (float)(transform.Pivot.Y + transform.Position.Y));
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateRotationDegrees((float)transform.Rotation));
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale((float)transform.Scale.X, (float)transform.Scale.Y));
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(-(float)transform.Pivot.X, -(float)transform.Pivot.Y));
            matrix = SKMatrix.Concat(SKMatrix.CreateScale(scaleX, scaleY), matrix);
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
