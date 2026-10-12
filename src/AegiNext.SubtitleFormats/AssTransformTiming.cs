using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssTransformTiming(MediaTime Start, MediaTime End, double Acceleration)
{
    internal static AssTransformTiming Parse(string[] arguments, MediaTime duration)
    {
        if (arguments.Length is < 1 or > 4 || !arguments[^1].StartsWith('\\'))
        {
            throw new InvalidDataException("ASS t 参数数量或标签列表非法。");
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
            throw new InvalidDataException("ASS 变换时间超出 32 位毫秒范围。");
        }
        return new((long)number, 1000);
    }
}
