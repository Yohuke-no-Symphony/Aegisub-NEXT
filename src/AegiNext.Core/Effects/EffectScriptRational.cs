using System.Numerics;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal readonly record struct EffectScriptRational
{
    internal EffectScriptRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException();
        }
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
        if (Numerator.GetBitLength() > 256 || Denominator.GetBitLength() > 256)
        {
            throw new OverflowException("表达式有理数超过表示预算。");
        }
    }

    internal BigInteger Numerator { get; }
    internal BigInteger Denominator { get; }
    internal double Double
    {
        get
        {
            if (Numerator.IsZero)
            {
                return 0;
            }
            var numerator = BigInteger.Abs(Numerator);
            var exponent = checked((int)(numerator.GetBitLength() - Denominator.GetBitLength()));
            if (exponent >= 0 ? numerator < Denominator << exponent : numerator << -exponent < Denominator)
            {
                exponent--;
            }
            var shift = 52 - exponent;
            var top = shift >= 0 ? numerator << shift : numerator;
            var bottom = shift >= 0 ? Denominator : Denominator << -shift;
            var significand = BigInteger.DivRem(top, bottom, out var remainder);
            var comparison = (remainder * 2).CompareTo(bottom);
            if (comparison > 0 || comparison == 0 && !significand.IsEven)
            {
                significand++;
            }
            if (significand == BigInteger.One << 53)
            {
                significand >>= 1;
                exponent++;
            }
            var bits = ((ulong)(exponent + 1023) << 52) | (ulong)(significand - (BigInteger.One << 52));
            if (Numerator.Sign < 0)
            {
                bits |= 1UL << 63;
            }
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        }
    }
    internal bool IsZero => Numerator.IsZero;
    internal static EffectScriptRational Parse(string text)
    {
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var exponent = exponentIndex < 0 ? 0 : int.Parse(text[(exponentIndex + 1)..], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var dot = mantissa.IndexOf('.');
        var scale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        if (digits.Length > 256 || scale is < -100 or > 100)
        {
            throw new OverflowException("数字字面量超过精确表示预算。");
        }
        var coefficient = BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return scale >= 0 ? new(coefficient, BigInteger.Pow(10, scale)) : new(coefficient * BigInteger.Pow(10, -scale), 1);
    }

    internal static EffectScriptRational From(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        return new(bits[3] < 0 ? -coefficient : coefficient, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }
    internal static EffectScriptRational From(MediaTime value) => new(value.Numerator, value.Denominator);
    internal MediaTime Time => new(checked((long)Numerator), checked((long)Denominator));
    internal decimal Decimal => (decimal)Numerator / (decimal)Denominator;
    internal long Integer => Denominator.IsOne ? checked((long)Numerator) : throw new InvalidOperationException("表达式需要整数。");
    internal int Compare(EffectScriptRational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    internal static EffectScriptRational Add(EffectScriptRational a, EffectScriptRational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    internal static EffectScriptRational Subtract(EffectScriptRational a, EffectScriptRational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    internal static EffectScriptRational Multiply(EffectScriptRational a, EffectScriptRational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    internal static EffectScriptRational Divide(EffectScriptRational a, EffectScriptRational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
}
