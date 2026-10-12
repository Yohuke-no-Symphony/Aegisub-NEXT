using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed class MaskSnapGeometry
{
    private MaskSnapGeometry(Rect localBounds, Matrix localToScreen, Matrix screenToLocal)
    {
        LocalBounds = localBounds;
        LocalToScreen = localToScreen;
        ScreenToLocal = screenToLocal;
        var xAxis = new Vector(localToScreen.M11, localToScreen.M12);
        var yAxis = new Vector(localToScreen.M21, localToScreen.M22);
        XNormal = new Vector(yAxis.Y, -yAxis.X) / yAxis.Length;
        YNormal = new Vector(-xAxis.Y, xAxis.X) / xAxis.Length;
        XDistanceScale = xAxis.X * XNormal.X + xAxis.Y * XNormal.Y;
        YDistanceScale = yAxis.X * YNormal.X + yAxis.Y * YNormal.Y;
    }

    internal Rect LocalBounds { get; }
    internal Matrix LocalToScreen { get; }
    internal Matrix ScreenToLocal { get; }
    private Vector XNormal { get; }
    private Vector YNormal { get; }
    private double XDistanceScale { get; }
    private double YDistanceScale { get; }

    internal static MaskSnapGeometry? Create(Rect localBounds, Matrix localToScreen)
    {
        if (!IsFinite(localBounds) || localBounds.Width <= 0 || localBounds.Height <= 0 ||
            !IsFinite(localToScreen) || !localToScreen.TryInvert(out var screenToLocal) || !IsFinite(screenToLocal))
        {
            return null;
        }

        return new(localBounds, localToScreen, screenToLocal);
    }

    internal MaskSnapResult SnapPoint(Point screenPoint, double tolerance)
    {
        var local = screenPoint * ScreenToLocal;
        var x = Closest([local.X], [LocalBounds.Left, LocalBounds.Center.X, LocalBounds.Right], XDistanceScale, tolerance);
        var y = Closest([local.Y], [LocalBounds.Top, LocalBounds.Center.Y, LocalBounds.Bottom], YDistanceScale, tolerance);
        return Result(x, y, tolerance);
    }

    internal MaskSnapResult SnapBounds(Rect localBounds, double tolerance)
    {
        if (!IsFinite(localBounds))
        {
            return default;
        }

        var x = Closest([localBounds.Left, localBounds.Center.X, localBounds.Right],
            [LocalBounds.Left, LocalBounds.Center.X, LocalBounds.Right], XDistanceScale, tolerance);
        var y = Closest([localBounds.Top, localBounds.Center.Y, localBounds.Bottom],
            [LocalBounds.Top, LocalBounds.Center.Y, LocalBounds.Bottom], YDistanceScale, tolerance);
        return Result(x, y, tolerance);
    }

    private MaskSnapResult Result((double? Guide, double Offset) x, (double? Guide, double Offset) y, double tolerance)
    {
        if (x.Guide.HasValue && y.Guide.HasValue)
        {
            var intersection = new Vector(x.Offset * LocalToScreen.M11 + y.Offset * LocalToScreen.M21,
                x.Offset * LocalToScreen.M12 + y.Offset * LocalToScreen.M22);
            if (intersection.Length <= tolerance * Math.Sqrt(2))
            {
                return new(intersection, x.Guide, y.Guide);
            }
            if (Math.Abs(x.Offset * XDistanceScale) <= Math.Abs(y.Offset * YDistanceScale))
            {
                y = (null, 0);
            }
            else
            {
                x = (null, 0);
            }
        }
        var offset = x.Guide.HasValue ? XNormal * (x.Offset * XDistanceScale)
            : y.Guide.HasValue ? YNormal * (y.Offset * YDistanceScale)
            : default;
        return new(offset, x.Guide, y.Guide);
    }

    private static (double? Guide, double Offset) Closest(ReadOnlySpan<double> coordinates, ReadOnlySpan<double> guides,
        double distanceScale, double tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        if (!double.IsFinite(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        double? closest = null;
        var offset = 0d;
        var minimumDistance = double.PositiveInfinity;
        foreach (var coordinate in coordinates)
        {
            foreach (var guide in guides)
            {
                var distance = Math.Abs((guide - coordinate) * distanceScale);
                if (distance <= tolerance && distance < minimumDistance)
                {
                    closest = guide;
                    offset = guide - coordinate;
                    minimumDistance = distance;
                }
            }
        }

        return (closest, offset);
    }

    private static bool IsFinite(Rect bounds) => double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) &&
        double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);

    private static bool IsFinite(Matrix matrix) => double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) && double.IsFinite(matrix.M31) && double.IsFinite(matrix.M32);
}
