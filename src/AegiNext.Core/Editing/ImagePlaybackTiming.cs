using System.Globalization;
using System.Numerics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>以独立源偏移和播放速度映射图片内容时间，不使用通用图层动画偏移。</summary>
public static class ImagePlaybackTiming
{
    /// <summary>计算工程时刻对应的有符号图片源时间；静态图片始终返回零。</summary>
    public static MediaTime GetSourceTime(ProjectLayer layer, MediaTime projectTime)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return layer.Image?.Playback is { } playback
            ? playback.SourceOffset + Scale(projectTime - layer.Start, playback.Speed) : MediaTime.Zero;
    }

    /// <summary>裁剪左边界时移动源偏移；保持未裁掉部分在相同工程时间的图片相位。</summary>
    public static ImagePlayback Crop(ImagePlayback playback, MediaTime startOffset)
    {
        ArgumentNullException.ThrowIfNull(playback);
        return startOffset == MediaTime.Zero ? playback : playback with { SourceOffset = playback.SourceOffset + Scale(startOffset, playback.Speed) };
    }

    /// <summary>按速度的往返十进制表示精确缩放时间；不可表示的结果拒绝提交。</summary>
    public static MediaTime Scale(MediaTime time, double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }
        if (time == MediaTime.Zero || speed == 1)
        {
            return time;
        }
        var parts = speed.ToString("R", CultureInfo.InvariantCulture).Split('E', 'e');
        var significand = parts[0];
        var point = significand.IndexOf('.');
        var fractionalDigits = point < 0 ? 0 : significand.Length - point - 1;
        var exponent = parts.Length == 1 ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture);
        var numerator = (BigInteger)time.Numerator * BigInteger.Parse(significand.Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
        var denominator = new BigInteger(time.Denominator);
        var power = exponent - fractionalDigits;
        if (power >= 0)
        {
            numerator *= BigInteger.Pow(10, power);
        }
        else
        {
            denominator *= BigInteger.Pow(10, -power);
        }
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }
}
