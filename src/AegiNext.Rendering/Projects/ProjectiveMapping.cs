using System.Collections.Immutable;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed class ProjectiveMapping
{
    private readonly ImmutableArray<ProjectionClipPlane> clipPlanes;

    private ProjectiveMapping(ProjectionMatrix3x3 matrix, SKMatrix canvasMatrix,
        ImmutableArray<ProjectionClipPlane> clipPlanes, bool isProjective)
    {
        Matrix = matrix;
        CanvasMatrix = canvasMatrix;
        this.clipPlanes = clipPlanes;
        IsProjective = isProjective;
    }

    internal static ProjectiveMapping Identity { get; } = Affine(SKMatrix.Identity);
    internal ProjectionMatrix3x3 Matrix { get; }
    internal SKMatrix CanvasMatrix { get; }
    internal bool IsProjective { get; }

    internal static bool RequiresProjection(LayerTransform transform) => Math.IEEERemainder(transform.RotationX, 360) != 0 ||
        Math.IEEERemainder(transform.RotationY, 360) != 0 || transform.ProjectionTranslation != default;

    internal static ProjectiveMapping Create(LayerTransform transform, SKPoint pivot, SKPoint center, SKMatrix affine)
    {
        if (!RequiresProjection(transform))
        {
            return Affine(affine);
        }
        var matrix = SceneProjection.CreateMatrix(transform, new(pivot.X, pivot.Y), new(center.X, center.Y));
        return new(matrix, ToSkia(matrix), [NearPlane(matrix)], true);
    }

    internal static ProjectiveMapping Affine(SKMatrix matrix) => new(FromSkia(matrix), matrix, [], false);

    internal ProjectiveMapping Then(ProjectiveMapping outer)
    {
        if (!IsProjective && !outer.IsProjective)
        {
            return Affine(SKMatrix.Concat(outer.CanvasMatrix, CanvasMatrix));
        }
        var result = outer.Matrix * Matrix;
        var planes = clipPlanes.ToBuilder();
        foreach (var plane in outer.clipPlanes)
        {
            planes.Add(plane.Transform(Matrix));
        }
        return new(result, ToSkia(result), planes.ToImmutable(), true);
    }

    internal bool EquivalentTo(ProjectiveMapping other) => CanvasMatrix == other.CanvasMatrix &&
        IsProjective == other.IsProjective && clipPlanes.AsSpan().SequenceEqual(other.clipPlanes.AsSpan());

    internal bool TryMap(SKPoint point, out SKPoint result)
    {
        if (!IsProjective)
        {
            result = CanvasMatrix.MapPoint(point);
            return float.IsFinite(result.X) && float.IsFinite(result.Y);
        }
        result = default;
        var source = new ScenePoint(point.X, point.Y);
        if (!IsVisible(source) || !Matrix.TryMap(source, out var mapped))
        {
            return false;
        }
        return TryPoint(mapped, out result);
    }

    internal bool TryUnmap(SKPoint point, out SKPoint result)
    {
        if (!IsProjective)
        {
            if (CanvasMatrix.TryInvert(out var affineInverse))
            {
                result = affineInverse.MapPoint(point);
                return float.IsFinite(result.X) && float.IsFinite(result.Y);
            }
            result = default;
            return false;
        }
        result = default;
        if (!Matrix.TryInvert(out var inverse) || !inverse.TryMap(new(point.X, point.Y), out var local))
        {
            return false;
        }
        if (!IsVisible(local) || Matrix.W(local) <= 0)
        {
            return false;
        }
        return TryPoint(local, out result);
    }

    internal IReadOnlyList<SKPoint> MapBounds(SKRect bounds)
    {
        if (!IsProjective)
        {
            return
            [
                CanvasMatrix.MapPoint(bounds.Left, bounds.Top), CanvasMatrix.MapPoint(bounds.Right, bounds.Top),
                CanvasMatrix.MapPoint(bounds.Right, bounds.Bottom), CanvasMatrix.MapPoint(bounds.Left, bounds.Bottom)
            ];
        }
        if (!HasFiniteMatrix())
        {
            return [];
        }
        IReadOnlyList<ScenePoint> polygon =
        [
            new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top),
            new(bounds.Right, bounds.Bottom), new(bounds.Left, bounds.Bottom)
        ];
        foreach (var plane in clipPlanes)
        {
            polygon = Clip(polygon, plane);
        }
        var result = new List<SKPoint>(polygon.Count);
        foreach (var point in polygon)
        {
            if (!TryPoint(Matrix.Map(point), out var mapped))
            {
                return [];
            }
            result.Add(mapped);
        }
        return result;
    }

    internal SKRect MapRect(SKRect bounds) => !IsProjective ? CanvasMatrix.MapRect(bounds) : Bounds(MapBounds(bounds));

    internal bool ApplyToCanvas(SKCanvas canvas)
    {
        if (!IsProjective)
        {
            canvas.SetMatrix(CanvasMatrix);
            return true;
        }
        if (!HasFiniteMatrix() || !Matrix.TryInvert(out var inverse))
        {
            return false;
        }
        var viewport = canvas.DeviceClipBounds;
        IReadOnlyList<ScenePoint> polygon =
        [
            new(viewport.Left, viewport.Top), new(viewport.Right, viewport.Top),
            new(viewport.Right, viewport.Bottom), new(viewport.Left, viewport.Bottom)
        ];
        polygon = Clip(polygon, new(inverse.M31, inverse.M32, inverse.M33));
        foreach (var plane in clipPlanes)
        {
            polygon = Clip(polygon, plane.Transform(inverse));
        }
        if (polygon.Count < 3)
        {
            return false;
        }
        var scale = Math.Max(1, Math.ScaleB(SceneProjection.NEAR_PLANE_RATIO * polygon.Max(inverse.W), 1));
        var canvasMatrix = ToSkia(Matrix, scale);
        if (!Finite(canvasMatrix))
        {
            return false;
        }
        var points = new SKPoint[polygon.Count];
        for (var index = 0; index < polygon.Count; index++)
        {
            if (!TryPoint(polygon[index], out points[index]))
            {
                return false;
            }
        }
        using var path = new SKPath();
        path.AddPoly(points, true);
        canvas.ResetMatrix();
        canvas.ClipPath(path, SKClipOperation.Intersect, false);
        canvas.SetMatrix(canvasMatrix);
        return true;
    }

    internal static SKRect Bounds(IReadOnlyList<SKPoint> polygon)
    {
        if (polygon.Count == 0)
        {
            return SKRect.Empty;
        }
        var left = polygon[0].X;
        var top = polygon[0].Y;
        var right = left;
        var bottom = top;
        foreach (var point in polygon)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
        return new(left, top, right, bottom);
    }

    internal static bool HasArea(IReadOnlyList<SKPoint> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }
        var area = 0d;
        for (var index = 1; index + 1 < polygon.Count; index++)
        {
            area += Cross(polygon[0], polygon[index], polygon[index + 1]);
        }
        return double.IsFinite(area) && area != 0;
    }

    internal static IReadOnlyList<SKPoint> ConvexHull(IEnumerable<SKPoint> points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (sorted.Length <= 2)
        {
            return sorted;
        }
        var hull = new List<SKPoint>();
        foreach (var point in sorted)
        {
            Add(point);
        }
        var lowerCount = hull.Count;
        for (var index = sorted.Length - 2; index >= 0; index--)
        {
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], sorted[index]) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(sorted[index]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;

        void Add(SKPoint point)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }
    }

    private bool IsVisible(ScenePoint point) => HasFiniteMatrix() && double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        clipPlanes.All(plane => plane.Evaluate(point) >= 0);

    private bool HasFiniteMatrix() => Matrix.IsFinite && Finite(CanvasMatrix);

    private static bool Finite(SKMatrix matrix) => float.IsFinite(matrix.ScaleX) && float.IsFinite(matrix.SkewX) &&
        float.IsFinite(matrix.TransX) && float.IsFinite(matrix.SkewY) && float.IsFinite(matrix.ScaleY) &&
        float.IsFinite(matrix.TransY) && float.IsFinite(matrix.Persp0) && float.IsFinite(matrix.Persp1) && float.IsFinite(matrix.Persp2);

    private static ProjectionClipPlane NearPlane(ProjectionMatrix3x3 matrix) =>
        new(matrix.M31, matrix.M32, matrix.M33 - SceneProjection.NEAR_PLANE_RATIO);

    private static IReadOnlyList<ScenePoint> Clip(IReadOnlyList<ScenePoint> polygon, ProjectionClipPlane plane)
    {
        if (polygon.Count == 0)
        {
            return polygon;
        }
        var result = new List<ScenePoint>(polygon.Count + 1);
        var previous = polygon[^1];
        var previousDistance = plane.Evaluate(previous);
        foreach (var point in polygon)
        {
            var distance = plane.Evaluate(point);
            if ((distance >= 0) != (previousDistance >= 0))
            {
                var amount = previousDistance / (previousDistance - distance);
                result.Add(new(previous.X + (point.X - previous.X) * amount,
                    previous.Y + (point.Y - previous.Y) * amount));
            }
            if (distance >= 0)
            {
                result.Add(point);
            }
            previous = point;
            previousDistance = distance;
        }
        return result;
    }

    private static bool TryPoint(ScenePoint point, out SKPoint result)
    {
        result = new((float)point.X, (float)point.Y);
        return float.IsFinite(result.X) && float.IsFinite(result.Y);
    }

    private static double Cross(SKPoint origin, SKPoint first, SKPoint second) =>
        ((double)first.X - origin.X) * ((double)second.Y - origin.Y) -
        ((double)first.Y - origin.Y) * ((double)second.X - origin.X);

    private static ProjectionMatrix3x3 FromSkia(SKMatrix matrix) => new(matrix.ScaleX, matrix.SkewX, matrix.TransX,
        matrix.SkewY, matrix.ScaleY, matrix.TransY, matrix.Persp0, matrix.Persp1, matrix.Persp2);

    private static SKMatrix ToSkia(ProjectionMatrix3x3 matrix, double scale = 1) => new(
        (float)(matrix.M11 * scale), (float)(matrix.M12 * scale), (float)(matrix.M13 * scale),
        (float)(matrix.M21 * scale), (float)(matrix.M22 * scale), (float)(matrix.M23 * scale),
        (float)(matrix.M31 * scale), (float)(matrix.M32 * scale), (float)(matrix.M33 * scale));
}
