using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class OverflowViewportMeasureProbe : Control
{
    internal List<Size> Constraints { get; } = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        Constraints.Add(availableSize);
        return new(Math.Min(100, availableSize.Width), Math.Min(64, availableSize.Height));
    }
}
