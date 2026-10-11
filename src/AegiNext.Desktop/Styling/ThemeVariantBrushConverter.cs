using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace AegiNext.Desktop.Styling;

/// <summary>根据控件的实际主题，从浅色和深色画刷中选择当前画刷。</summary>
public sealed class ThemeVariantBrushConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values is [IBrush light, IBrush dark, ThemeVariant theme]
            ? theme == ThemeVariant.Dark ? dark : light
            : AvaloniaProperty.UnsetValue;
    }
}
