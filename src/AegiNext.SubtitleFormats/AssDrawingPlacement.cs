using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssDrawingPlacement
{
    private readonly ScenePoint position;
    private readonly ScenePoint origin;
    private readonly double scaleX;
    private readonly double scaleY;
    private readonly double baseX;
    private readonly double baseY;
    private readonly double offsetY;
    private readonly double sine;
    private readonly double cosine;

    internal AssDrawingPlacement(AssDrawingGeometry geometry, AssDrawingRun run, SubtitleStyle eventStyle,
        double canvasScaleX, double canvasScaleY, ScenePoint? rotationOrigin)
    {
        if (run.DrawingScale is < 1 or > 31 || run.Scale.X <= 0 || run.Scale.Y <= 0 ||
            !double.IsFinite(run.Scale.X) || !double.IsFinite(run.Scale.Y))
        {
            throw new InvalidDataException("The drawing scale cannot produce a finite, positive glyph layout.");
        }
        var divisor = Math.Pow(2, run.DrawingScale - 1);
        scaleX = run.Scale.X * canvasScaleX / divisor;
        scaleY = run.Scale.Y * canvasScaleY / divisor;
        var sourceHeight = geometry.BoundsMaximum.Y - geometry.BoundsMinimum.Y;
        var descFixed = Math.Truncate(run.BaselineOffset * 64);
        if (!double.IsFinite(descFixed) || descFixed < int.MinValue || descFixed > int.MaxValue)
        {
            throw new InvalidDataException("The drawing baseline offset exceeds the signed glyph-metric range.");
        }
        var asc = sourceHeight - descFixed / 64;
        var desc = descFixed / 64;
        var measuredAsc = Math.Max(0, QuantizeMetric(asc * scaleY));
        var measuredDesc = Math.Max(0, QuantizeMetric(desc * scaleY));
        var advance = QuantizeMetric((geometry.BoundsMaximum.X - geometry.BoundsMinimum.X) * scaleX);
        var horizontal = (int)eventStyle.Alignment % 3;
        var vertical = (int)eventStyle.Alignment / 3;
        baseX = horizontal * advance / 2;
        baseY = vertical switch
        {
            0 => -measuredAsc,
            1 => (measuredDesc - measuredAsc) / 2,
            _ => measuredDesc
        };
        offsetY = -asc * scaleY;
        position = eventStyle.Position!.Offset;
        origin = rotationOrigin ?? position;
        (sine, cosine) = AssTransformMath.SinCos(-run.Rotation);
    }

    internal ScenePoint Transform(ScenePoint point)
    {
        var local = LocalPoint(point);
        return new(origin.X + cosine * local.X - sine * local.Y, origin.Y + sine * local.X + cosine * local.Y);
    }

    internal ScenePoint Origin => origin;

    internal ScenePoint LocalPoint(ScenePoint point) => new(point.X * scaleX + position.X - baseX - origin.X,
        point.Y * scaleY + offsetY + position.Y - baseY - origin.Y);

    private static double QuantizeMetric(double value)
    {
        var fixedValue = Math.Round(value * 64, MidpointRounding.ToEven);
        if (!double.IsFinite(fixedValue) || fixedValue < int.MinValue || fixedValue > int.MaxValue)
        {
            throw new InvalidDataException("The drawing glyph layout exceeds the signed 26.6-bit range.");
        }
        return fixedValue / 64;
    }
}
