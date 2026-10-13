using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.EffectPresets;

internal sealed class EffectPresetGridPanel : Panel, INavigableContainer
{
    private const double MINIMUM_CARD_WIDTH = 176;
    private const double SPACING = 8;
    private int columns = 1;
    private double cardWidth;
    private double[] rowHeights = [];

    IInputElement? INavigableContainer.GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (Children.Count == 0)
        {
            return null;
        }
        var origin = (from as Control)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(Children.Contains);
        var index = origin is not null ? Children.IndexOf(origin) : -1;
        var target = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => Children.Count - 1,
            NavigationDirection.Next or NavigationDirection.Right => index + 1,
            NavigationDirection.Previous or NavigationDirection.Left => index < 0 ? Children.Count - 1 : index - 1,
            NavigationDirection.Down => index < 0 ? 0 : index + columns,
            NavigationDirection.Up => index < 0 ? Children.Count - 1 : index - columns,
            _ => -1
        };
        if (wrap && direction is NavigationDirection.Next or NavigationDirection.Previous or
            NavigationDirection.Left or NavigationDirection.Right or NavigationDirection.Up or NavigationDirection.Down)
        {
            target = (target % Children.Count + Children.Count) % Children.Count;
        }
        return target >= 0 && target < Children.Count ? Children[target] : null;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : MINIMUM_CARD_WIDTH;
        columns = Math.Max(1, (int)Math.Floor((width + SPACING) / (MINIMUM_CARD_WIDTH + SPACING)));
        cardWidth = Math.Max(0, (width - SPACING * (columns - 1)) / columns);
        rowHeights = new double[(Children.Count + columns - 1) / columns];
        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            child.Measure(new(cardWidth, double.PositiveInfinity));
            rowHeights[index / columns] = Math.Max(rowHeights[index / columns], child.DesiredSize.Height);
        }
        return new(width, rowHeights.Sum() + SPACING * Math.Max(0, rowHeights.Length - 1));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var offset = 0d;
        for (var row = 0; row < rowHeights.Length; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                if (index >= Children.Count)
                {
                    break;
                }
                Children[index].Arrange(new(column * (cardWidth + SPACING), offset, cardWidth, rowHeights[row]));
            }
            offset += rowHeights[row] + SPACING;
        }
        return finalSize;
    }
}
