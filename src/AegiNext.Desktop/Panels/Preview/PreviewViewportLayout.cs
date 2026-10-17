using AegiNext.Desktop.Controls.Common;
using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Panels.Preview;

internal sealed class PreviewViewportLayout : OverflowGrid
{
    private Grid? previewOptions;

    /// <summary>为顶部选项保留控件与顶部间距，允许底部留白随画面收缩。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        previewOptions ??= this.FindControl<Grid>("PreviewOptions")!;
        previewOptions.Measure(new(availableSize.Width, double.PositiveInfinity));
        RowDefinitions[0].MinHeight = Math.Max(0, previewOptions.DesiredSize.Height - previewOptions.Margin.Bottom);
        return base.MeasureOverride(availableSize);
    }
}
