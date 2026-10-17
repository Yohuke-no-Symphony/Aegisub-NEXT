using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Subtitles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleListLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisplayTogglesCollapseColumnsIndependentlyAndPreserveTheDocument(bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var document = CreateDocument(40);
        context.Session.Editor.Reset(document);
        using var view = new SubtitlesPanelView(context.ViewModel.Subtitles, context.Session);
        var window = new Window
        {
            Width = 640,
            Height = 340,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            Flush(window);
            var times = view.FindControl<ToolbarToggleButton>("ShowSubtitleTimesToggle");
            var duration = view.FindControl<ToolbarToggleButton>("ShowSubtitleDurationToggle");
            Assert.NotNull(times);
            Assert.NotNull(duration);
            Assert.True(times.IsChecked);
            Assert.True(duration.IsChecked);
            var list = view.FindControl<ListBox>("SubtitleList")!;
            var row = list.GetVisualDescendants().OfType<Grid>().Single(grid =>
                grid.DataContext is SubtitleRow value && value.Id == document.Subtitles[0].Id &&
                grid.Children.OfType<TextBox>().Count() == 3);
            var start = row.Children.OfType<TextBox>().Single(box => Grid.GetColumn(box) == 1);
            var end = row.Children.OfType<TextBox>().Single(box => Grid.GetColumn(box) == 2);
            var length = row.Children.OfType<TextBlock>().Single(block => Grid.GetColumn(block) == 3);
            var text = row.Children.OfType<TextBox>().Single(box => box.AcceptsReturn);
            var originalWidth = text.Bounds.Width;
            var header = view.FindControl<Grid>("SubtitleFieldsHeader")!;
            UiTestCapture.CaptureExportPanel(window, $"subtitle-columns-visible-{(dark ? "dark" : "light")}");
            UiTestActions.Click(window, "ShowSubtitleTimesToggle");
            Flush(window);
            Assert.False(times.IsChecked);
            Assert.True(duration.IsChecked);
            Assert.False(start.IsVisible);
            Assert.False(end.IsVisible);
            Assert.True(length.IsVisible);
            Assert.False(header.Children[1].IsVisible);
            Assert.False(header.Children[2].IsVisible);
            Assert.True(header.Children[3].IsVisible);
            Assert.Equal(originalWidth + 240, text.Bounds.Width, 1);
            UiTestActions.Click(window, "ShowSubtitleDurationToggle");
            Flush(window);
            Assert.False(duration.IsChecked);
            Assert.False(length.IsVisible);
            Assert.False(header.Children[3].IsVisible);
            Assert.Equal(originalWidth + 318, text.Bounds.Width, 1);
            AssertRowAlignment(window, header, list, document.Subtitles[0].Id);
            UiTestCapture.CaptureExportPanel(window, $"subtitle-columns-hidden-{(dark ? "dark" : "light")}");
            UiTestActions.Click(window, "ShowSubtitleTimesToggle");
            Flush(window);
            Assert.True(times.IsChecked);
            Assert.False(duration.IsChecked);
            Assert.True(start.IsVisible);
            Assert.True(end.IsVisible);
            Assert.False(length.IsVisible);
            Assert.Equal(originalWidth + 78, text.Bounds.Width, 1);
            list.ScrollIntoView(context.ViewModel.Subtitles.VisibleRows[^1]);
            Flush(window);
            var number = list.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                block.Name == "SubtitleRowNumber" && block.DataContext is SubtitleRow value &&
                value.Id == document.Subtitles[^1].Id);
            Assert.Equal("40", number.Text);
            UiTestActions.Click(window, "ShowSubtitleDurationToggle");
            Flush(window);
            Assert.True(duration.IsChecked);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(context.Session.Editor.HasUnsavedChanges);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InvalidTimeDraftCanBeRevealedAndFocusedAfterHidingColumns()
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        context.Session.Editor.Reset(document);
        using var view = new SubtitlesPanelView(context.ViewModel.Subtitles, context.Session);
        var window = new Window { Width = 640, Height = 340, Content = view };
        var row = context.ViewModel.Subtitles.VisibleRows[0];
        try
        {
            window.Show();
            Flush(window);
            row.StartText = "invalid";
            context.ViewModel.Subtitles.HideTimes = true;
            Flush(window);
            Assert.Equal("invalid", row.StartText);
            context.Session.CommitRow(row);
            view.FocusInvalidField("StartText");
            Flush(window);

            Assert.False(context.ViewModel.Subtitles.HideTimes);
            var focused = Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
            Assert.Same(row, focused.DataContext);
            Assert.Equal("invalid", focused.Text);
            Assert.True(focused.IsEffectivelyVisible);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            row.Accept(document.Subtitles[0]);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(424, "zh-CN", true)]
    [InlineData(640, "zh-CN", true)]
    [InlineData(640, "en-US", false)]
    [InlineData(1100, "en-US", false)]
    public async Task IconTagFilterKeepsTheNarrowToolbarOnOneLineAndPreservesNamedChoices(double width, string language,
        bool dark)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var tag = new SubtitleColorTag { Name = "还需校对", ColorHex = "#34C759" };
        var document = CreateDocument(2) with { ColorTags = [tag] };
        document = document with
        {
            Subtitles = document.Subtitles.SetItem(0, document.Subtitles[0] with { ColorTagId = tag.Id })
        };
        context.Session.Editor.Reset(document);
        using var view = new SubtitlesPanelView(context.ViewModel.Subtitles, context.Session);
        var window = new Window
        {
            Width = width,
            Height = 340,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            Flush(window);
            var track = view.FindControl<ComboBox>("SubtitleTrackCombo")!;
            var filter = view.FindControl<ComboBox>("SubtitleColorTagFilterCombo")!;
            var actions = view.FindControl<StackPanel>("SubtitleEditingActions")!;
            var trackOrigin = track.TranslatePoint(new(), window)!.Value;
            var filterOrigin = filter.TranslatePoint(new(), window)!.Value;
            var actionsOrigin = actions.TranslatePoint(new(), window)!.Value;
            Assert.Equal(32, filter.Bounds.Width);
            Assert.Equal(32, filter.Bounds.Height);
            Assert.Equal(trackOrigin.Y, filterOrigin.Y);
            Assert.Equal(8, filterOrigin.X - trackOrigin.X - track.Bounds.Width);
            Assert.InRange(filterOrigin.X + filter.Bounds.Width, 0, width);
            var buttons = actions.Children.OfType<Button>().ToArray();
            Assert.Equal(5, buttons.Length);
            Assert.All(buttons, button => Assert.Equal(buttons[0].Bounds.Y, button.Bounds.Y));
            Assert.InRange(actionsOrigin.X + actions.Bounds.Width, 0, width);
            Assert.Equal(filterOrigin.Y, actionsOrigin.Y);
            Assert.Equal(8, actionsOrigin.X - filterOrigin.X - filter.Bounds.Width);
            for (var index = 1; index < buttons.Length; index++)
            {
                var previous = buttons[index - 1];
                Assert.Equal(8, buttons[index].Bounds.X - previous.Bounds.Right);
            }
            Assert.DoesNotContain(filter.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text));

            Assert.DoesNotContain("filtered", filter.Classes);
            AssertFilterBorderVisible(filter);
            filter.SelectedItem =
                context.ViewModel.Subtitles.ColorTagFilters.Single(choice => choice.Value.TagId == tag.Id);
            Flush(window);
            Assert.Contains("filtered", filter.Classes);
            AssertFilterBorderVisible(filter);
            Assert.Single(context.ViewModel.Subtitles.VisibleRows);
            Assert.Contains(tag.Name, Assert.IsType<string>(ToolTip.GetTip(filter)), StringComparison.Ordinal);
            Assert.DoesNotContain(filter.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text));
            UiTestCapture.CaptureExportPanel(window, $"subtitle-filter-{width}-{language}-{(dark ? "dark" : "light")}");
            var filterCenter = filter.TranslatePoint(new(filter.Bounds.Width / 2, filter.Bounds.Height / 2), window)!
                .Value;
            window.MouseDown(filterCenter, MouseButton.Left);
            window.MouseUp(filterCenter, MouseButton.Left);
            Flush(window);
            Assert.True(filter.IsDropDownOpen);
            Assert.Equal(tag.Name, Assert.IsType<SubtitleColorTagFilterChoice>(filter.SelectedItem).Name);
            Assert.Contains(tag.Name,
                filter.ContainerFromIndex(filter.SelectedIndex)!.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text));
            UiTestCapture.CaptureExportPanel(window,
                $"subtitle-filter-menu-{width}-{language}-{(dark ? "dark" : "light")}");
            var all = filter.ContainerFromIndex(0)!;
            var popup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(all));
            var allCenter = all.TranslatePoint(new(all.Bounds.Width / 2, all.Bounds.Height / 2), popup)!.Value;
            popup.MouseDown(allCenter, MouseButton.Left);
            popup.MouseUp(allCenter, MouseButton.Left);
            Flush(window);
            Assert.False(filter.IsDropDownOpen);
            Assert.DoesNotContain("filtered", filter.Classes);
            Assert.Equal(2, context.ViewModel.Subtitles.VisibleRows.Length);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(640, "en-US", false)]
    [InlineData(640, "en-US", true)]
    [InlineData(640, "zh-CN", false)]
    [InlineData(640, "zh-CN", true)]
    [InlineData(1100, "en-US", false)]
    [InlineData(1100, "en-US", true)]
    [InlineData(1100, "zh-CN", false)]
    [InlineData(1100, "zh-CN", true)]
    public async Task ColumnHeadersAlignWithRowsBeforeAndAfterScrolling(double width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        using var view = new SubtitlesPanelView(context.ViewModel.Subtitles, context.Session);
        var window = new Window
        {
            Width = width,
            Height = 340,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            foreach (var count in new[] { 2, 40, 2 })
            {
                var document = CreateDocument(count);
                context.Session.Editor.Reset(document);
                Flush(window);
                var list = view.FindControl<ListBox>("SubtitleList")!;
                var header = view.GetVisualDescendants().OfType<Grid>().Single(grid =>
                    grid.Children.OfType<TextBlock>()
                        .Any(text => text.Text == Localization.Get("Workbench.SubtitleType")));
                AssertRowAlignment(window, header, list, document.Subtitles[0].Id);
                if (count > 2)
                {
                    list.ScrollIntoView(context.ViewModel.Subtitles.VisibleRows[^1]);
                    Flush(window);
                    AssertRowAlignment(window, header, list, document.Subtitles[^1].Id);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument CreateDocument(int count)
    {
        var lines = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = "字幕 ABC 123",
            Start = new(index * 3),
            End = new(index * 3 + 2)
        }).ToImmutableArray();
        return new()
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
    }

    private static void AssertFilterBorderVisible(ComboBox filter)
    {
        using var target =
            new RenderTargetBitmap(new((int)filter.Bounds.Width, (int)filter.Bounds.Height), new(96, 96));
        target.Render(filter);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var background = filter.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Background");
        var centerX = pixels.Width / 2;
        var centerY = pixels.Height / 2;
        foreach (var (side, edgeX, edgeY, innerX, innerY) in new[]
                 {
                     ("left", 0, centerY, 2, centerY),
                     ("right", pixels.Width - 1, centerY, pixels.Width - 3, centerY),
                     ("top", centerX, 0, centerX, 2),
                     ("bottom", centerX, pixels.Height - 1, centerX, pixels.Height - 3)
                 })
        {
            var edge = pixels.GetPixel(edgeX, edgeY);
            var inner = pixels.GetPixel(innerX, innerY);
            Assert.True(edge != inner,
                $"Missing {side} border: edge={edge}, inner={inner}, background bounds={background.Bounds}, thickness={background.BorderThickness}.");
        }
    }

    private static void AssertRowAlignment(Window window, Grid header, ListBox list, Guid id)
    {
        var row = list.GetVisualDescendants().OfType<Grid>().Single(grid =>
            grid.DataContext is SubtitleRow row && row.Id == id && grid.Children.OfType<TextBox>().Count() == 3);
        foreach (var label in header.Children.OfType<TextBlock>().Where(label => label.IsVisible))
        {
            var column = Grid.GetColumn(label);
            var field = row.Children.Single(control => Grid.GetColumn(control) == column);
            var labelPoint = label.TranslatePoint(new Point(column == 4 ? 0 : label.Bounds.Width / 2, 0), window)!
                .Value;
            var fieldPoint = field.TranslatePoint(new Point(column == 4 ? 0 : field.Bounds.Width / 2, 0), window)!
                .Value;
            Assert.True(Math.Abs(labelPoint.X - fieldPoint.X) <= 0.5,
                $"Column {column}: header X={labelPoint.X}, field X={fieldPoint.X}.");
        }

        var content = row.Children.OfType<TextBox>().Single(box => Grid.GetColumn(box) == 4);
        var right = content.TranslatePoint(new Point(content.Bounds.Width, 0), list)!.Value.X;
        Assert.InRange(right, 0, list.Bounds.Width);
        Assert.True(content.Bounds.Width > 0);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
