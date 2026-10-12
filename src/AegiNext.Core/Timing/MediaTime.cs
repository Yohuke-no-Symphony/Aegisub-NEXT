using System.Globalization;
using System.Numerics;

namespace AegiNext.Core.Timing;

/// <summary>
/// 以规范化有理秒表示精确时间；默认值是零，超出 long 表示范围的结果抛出溢出异常。
/// </summary>
public readonly record struct MediaTime : IComparable<MediaTime>
{
    private readonly long denominatorMinusOne;

    /// <summary>
    /// 使用分子和严格为正的分母创建精确时间。
    /// </summary>
    public MediaTime(long numerator, long denominator = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        var divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        denominatorMinusOne = denominator / divisor - 1;
    }

    private MediaTime(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = checked((long)(numerator / divisor));
        denominatorMinusOne = checked((long)(denominator / divisor)) - 1;
    }

    public static MediaTime Zero => default;

    public long Numerator { get; }

    public long Denominator => denominatorMinusOne + 1;

    /// <summary>
    /// 精确转换 TimeSpan，不引入浮点秒数。
    /// </summary>
    public static MediaTime FromTimeSpan(TimeSpan value)
    {
        return new(value.Ticks, TimeSpan.TicksPerSecond);
    }

    /// <summary>
    /// 按显式取整策略量化为 TimeSpan 刻度。
    /// </summary>
    public TimeSpan ToTimeSpan(MediaTimeRounding rounding)
    {
        return TimeSpan.FromTicks(ToTimestamp(MediaTimeBase.TimeSpanTicks, rounding).Value);
    }

    /// <summary>
    /// 按指定时基和取整策略产生新的媒体时间戳。
    /// </summary>
    public MediaTimestamp ToTimestamp(MediaTimeBase timeBase, MediaTimeRounding rounding)
    {
        ArgumentNullException.ThrowIfNull(timeBase);

        var numerator = (BigInteger)Numerator * timeBase.Denominator;
        var denominator = (BigInteger)Denominator * timeBase.Numerator;
        return new(Round(numerator, denominator, rounding), timeBase);
    }

    /// <inheritdoc />
    public int CompareTo(MediaTime other)
    {
        return ((BigInteger)Numerator * other.Denominator).CompareTo((BigInteger)other.Numerator * Denominator);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator} s");
    }

    /// <summary>
    /// 精确相加，并在约分后检查表示范围。
    /// </summary>
    public static MediaTime operator +(MediaTime left, MediaTime right)
    {
        return new(
            (BigInteger)left.Numerator * right.Denominator + (BigInteger)right.Numerator * left.Denominator,
            (BigInteger)left.Denominator * right.Denominator);
    }

    /// <summary>
    /// 精确相减，避免先对 long 最小值取反。
    /// </summary>
    public static MediaTime operator -(MediaTime left, MediaTime right)
    {
        return new(
            (BigInteger)left.Numerator * right.Denominator - (BigInteger)right.Numerator * left.Denominator,
            (BigInteger)left.Denominator * right.Denominator);
    }

    /// <summary>
    /// 返回相反的时间。
    /// </summary>
    public static MediaTime operator -(MediaTime value)
    {
        return new(-(BigInteger)value.Numerator, value.Denominator);
    }

    /// <summary>
    /// 精确乘以整数。
    /// </summary>
    public static MediaTime operator *(MediaTime value, long multiplier)
    {
        return new((BigInteger)value.Numerator * multiplier, value.Denominator);
    }

    /// <summary>
    /// 精确除以非零整数。
    /// </summary>
    public static MediaTime operator /(MediaTime value, long divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        return new(value.Numerator, (BigInteger)value.Denominator * divisor);
    }

    /// <summary>
    /// 判断左侧时间是否早于右侧。
    /// </summary>
    public static bool operator <(MediaTime left, MediaTime right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// 判断左侧时间是否晚于右侧。
    /// </summary>
    public static bool operator >(MediaTime left, MediaTime right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// 判断左侧时间是否不晚于右侧。
    /// </summary>
    public static bool operator <=(MediaTime left, MediaTime right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// 判断左侧时间是否不早于右侧。
    /// </summary>
    public static bool operator >=(MediaTime left, MediaTime right)
    {
        return left.CompareTo(right) >= 0;
    }

    private static long Round(BigInteger numerator, BigInteger denominator, MediaTimeRounding rounding)
    {
        if (!Enum.IsDefined(rounding))
        {
            throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown time rounding policy.");
        }

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);

        if (!remainder.IsZero)
        {
            var increment = rounding switch
            {
                MediaTimeRounding.TOWARD_ZERO => 0,
                MediaTimeRounding.FLOOR => remainder.Sign < 0 ? -1 : 0,
                MediaTimeRounding.CEILING => remainder.Sign > 0 ? 1 : 0,
                MediaTimeRounding.TO_EVEN => RoundToEven(quotient, remainder, denominator),
                _ => throw new ArgumentOutOfRangeException(nameof(rounding))
            };

            quotient += increment;
        }

        return checked((long)quotient);
    }

    private static int RoundToEven(BigInteger quotient, BigInteger remainder, BigInteger denominator)
    {
        var midpointComparison = (BigInteger.Abs(remainder) * 2).CompareTo(denominator);
        return midpointComparison > 0 || (midpointComparison == 0 && !quotient.IsEven) ? remainder.Sign : 0;
    }
}
