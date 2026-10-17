using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>以宿主有限视口和各行实际最小需求的较大值布局，避免列表被无限测量或压至零高度。</summary>
public class OverflowGrid : Grid
{
    private double viewportHeight = double.PositiveInfinity;

    internal void SetViewportHeight(double value)
    {
        if (!viewportHeight.Equals(value))
        {
            viewportHeight = value;
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsFinite(viewportHeight))
        {
            return base.MeasureOverride(availableSize);
        }

        var height = Math.Max(0, viewportHeight - Margin.Top - Margin.Bottom);
        var desired = base.MeasureOverride(new(availableSize.Width, height));
        var extentHeight = Math.Max(height, desired.Height);
        if (extentHeight > height)
        {
            desired = base.MeasureOverride(new(availableSize.Width, extentHeight));
        }

        return new(desired.Width, Math.Max(extentHeight, desired.Height));
    }
}
