using Avalonia;

namespace AegiNext.Desktop.Controls;

internal readonly record struct MaskSnapResult(Vector Offset, double? XGuide = null, double? YGuide = null)
{
    internal bool HasSnap => XGuide.HasValue || YGuide.HasValue;
}
