using AegiNext.Desktop.Controls.Common;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class OverflowViewportUiTests
{
    [AvaloniaTheory]
    [InlineData(18)]
    [InlineData(50)]
    [InlineData(130)]
    [InlineData(360)]
    public void OverflowKeepsBodyMeasurementsFiniteAndRestoresTheFullViewport(double height)
    {
        using var environment = new UiTestEnvironment();
        var body = new OverflowViewportMeasureProbe();
        var header = new TextBlock { Text = "Toolbar 中文 123", Height = 40 };
        var grid = new OverflowGrid { RowDefinitions = new("Auto,*"), Margin = new(6) };
        grid.RowDefinitions[1].MinHeight = 64;
        Grid.SetRow(body, 1);
        grid.Children.Add(header);
        grid.Children.Add(body);
        var scroll = new OverflowScrollViewer { Content = grid, Padding = new(3), BorderThickness = new(1) };
        var window = new Window { Width = 320, Height = height, Content = scroll };
        try
        {
            window.Show();
            Flush(window);
            Assert.NotEmpty(body.Constraints);
            Assert.All(body.Constraints, constraint => Assert.True(double.IsFinite(constraint.Height)));
            Assert.True(body.Bounds.Height >= 64);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height >= 116);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            if (height < 124)
            {
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new(0, scroll.Extent.Height - scroll.Viewport.Height);
                Flush(window);
                Assert.True(scroll.Offset.Y > 0);
            }

            window.Height = 500;
            Flush(window);
            Assert.InRange(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height), 0, 1);
            Assert.InRange(scroll.Offset.Y, 0, 1);
            Assert.True(body.Bounds.Height > 300);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AThousandRowsRemainVirtualizedInsideTheFiniteBody()
    {
        using var environment = new UiTestEnvironment();
        var list = new ListBox { ItemsSource = Enumerable.Range(0, 1000).ToArray() };
        var grid = new OverflowGrid { RowDefinitions = new("Auto,*") };
        grid.RowDefinitions[1].MinHeight = 64;
        grid.Children.Add(new TextBlock { Text = "List header", Height = 40 });
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var scroll = new OverflowScrollViewer { Content = grid };
        var window = new Window { Width = 320, Height = 50, Content = scroll };
        try
        {
            window.Show();
            Flush(window);
            Assert.InRange(list.Bounds.Height, 64, 65);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
            scroll.Offset = new(0, scroll.Extent.Height - scroll.Viewport.Height);
            list.SelectedIndex = 999;
            list.ScrollIntoView(999);
            Flush(window);
            var last = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(999));
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
            Assert.True(last.TranslatePoint(default, list)!.Value.Y < list.Bounds.Height);
            Assert.True(last.TranslatePoint(new(0, last.Bounds.Height), list)!.Value.Y > 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
