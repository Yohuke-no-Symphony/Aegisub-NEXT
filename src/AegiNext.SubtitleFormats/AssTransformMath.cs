namespace AegiNext.SubtitleFormats;

internal static class AssTransformMath
{
    internal static (double Sine, double Cosine) SinCos(double degrees)
    {
        var angle = degrees % 360;
        return angle switch
        {
            0 => (0, 1),
            90 or -270 => (1, 0),
            180 or -180 => (0, -1),
            270 or -90 => (-1, 0),
            _ => Math.SinCos(angle * Math.PI / 180)
        };
    }
}
