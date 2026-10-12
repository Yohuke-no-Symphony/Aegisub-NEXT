using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssTransformTiming(MediaTime Start, MediaTime End, double Acceleration)
{
    internal static AssTransformTiming Parse(string[] arguments, MediaTime duration)
    {
        if (arguments.Length is < 1 or > 4 || !arguments[^1].StartsWith('\\'))
        {
            throw new InvalidDataException("The ASS t argument count or tag list is invalid.");
        }
        var start = arguments.Length >= 3 ? Milliseconds(arguments[0]) : MediaTime.Zero;
        var end = arguments.Length >= 3 ? Milliseconds(arguments[1]) : duration;
        var acceleration = arguments.Length is 2 or 4 ? AssFormatValues.Number(arguments[^2]) : 1;
        return new(start, end == MediaTime.Zero ? duration : end, acceleration);
    }

    private static MediaTime Milliseconds(string value)
    {
        var number = AssFormatValues.Number(value);
        if (number < int.MinValue || number > int.MaxValue)
        {
            throw new InvalidDataException("ASS transform times exceed the 32-bit millisecond range.");
        }
        return new((long)number, 1000);
    }
}
