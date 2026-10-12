using System.Numerics;

namespace AegiNext.Rendering;

internal static class RenderValidation
{
    internal static void Finite(float value, string parameter)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameter, value, "The value must be finite.");
        }
    }

    internal static void Point(Vector2 value, string parameter)
    {
        Finite(value.X, parameter);
        Finite(value.Y, parameter);
    }

    internal static void UnitInterval(float value, string parameter)
    {
        Finite(value, parameter);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0, parameter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1, parameter);
    }
}
