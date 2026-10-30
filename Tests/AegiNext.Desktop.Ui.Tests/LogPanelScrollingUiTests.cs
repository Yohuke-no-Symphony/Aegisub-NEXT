using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LogPanelScrollingUiTests
{
    [AvaloniaTheory]
    [InlineData(0, 540)]
    [InlineData(50, 540)]
    [InlineData(149, 540)]
    [InlineData(149, 160)]
    public void ExpandingLongDetailsPreservesTheClickedHeaderPosition(int entryIndex, int height)
    {
        using var environment = new UiTestEnvironment();
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        using var view = new LogPanelView(model, journal);
        var window = new Window { Width = 900, Height = height, Content = view };
        try
        {
            AppendEntries(journal);
            window.Show();
            Flush(window);
            var list = view.FindControl<ListBox>("LogEntries")!;
            var entry = model.Entries[entryIndex];
            list.ScrollIntoView(entry);
            Flush(window);
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(entry));
            var expander = Assert.Single(row.GetVisualDescendants().OfType<Expander>());
            var header = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
            var before = header.TranslatePoint(default, window)!.Value;
            var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
            Assert.True(ReferenceEquals(hit, header) || hit.GetVisualAncestors().Contains(header));

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Flush(window);

            Assert.True(ReferenceEquals(entry, row.DataContext),
                $"Expanded entry {entry.Sequence} was replaced by entry {(row.DataContext as WorkbenchLogEntry)?.Sequence}.");
            Assert.True(expander.IsExpanded);
            var after = header.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(after.Y - before.Y), 0, 1);
            Assert.True(row.Bounds.Height > list.Bounds.Height);
            Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 64);

            Assert.True(header.Focus());
            Press(window, Key.Space);
            Assert.False(expander.IsExpanded);
            Assert.InRange(Math.Abs(header.TranslatePoint(default, window)!.Value.Y - before.Y), 0, 1);
            Press(window, Key.Space);
            Assert.True(expander.IsExpanded);
            Assert.InRange(Math.Abs(header.TranslatePoint(default, window)!.Value.Y - before.Y), 0, 1);

            var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            var offset = scroll.Offset.Y;
            var center = scroll.TranslatePoint(new(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(center, new(0, -3));
            Flush(window);
            Assert.True(scroll.Offset.Y > offset);
            Assert.True(ReferenceEquals(entry, row.DataContext));
            Assert.True(expander.IsExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardNavigationAndExplicitRevealKeepOffscreenEntriesReachable()
    {
        using var environment = new UiTestEnvironment();
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        using var view = new LogPanelView(model, journal);
        var window = new Window { Width = 900, Height = 540, Content = view };
        try
        {
            AppendEntries(journal);
            window.Show();
            Flush(window);
            var list = view.FindControl<ListBox>("LogEntries")!;
            list.SelectedIndex = 0;
            Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0)).Focus());
            for (var index = 0; index < 30; index++)
            {
                Press(window, Key.Down);
            }

            Assert.Equal(30, list.SelectedIndex);
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(30));
            var top = row.TranslatePoint(default, list)!.Value.Y;
            Assert.InRange(top, 0, list.Bounds.Height - row.Bounds.Height);
            var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroll.Offset.Y > 0);

            for (var index = 0; index < 30; index++)
            {
                Press(window, Key.Up);
            }
            Assert.Equal(0, list.SelectedIndex);
            Assert.InRange(scroll.Offset.Y, 0, 1);

            var target = journal.Entries[^1];
            model.FilterIndex = 1;
            model.FilterText = "No matching log";
            Flush(window);
            Assert.Empty(model.Entries);

            view.RevealEntry(target);
            Flush(window);

            Assert.Equal(0, model.FilterIndex);
            Assert.Equal(string.Empty, model.FilterText);
            Assert.True(ReferenceEquals(target, model.SelectedEntry));
            row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(target));
            var expander = Assert.Single(row.GetVisualDescendants().OfType<Expander>());
            Assert.True(expander.IsExpanded);
            var header = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
            top = header.TranslatePoint(default, list)!.Value.Y;
            Assert.InRange(top, 0, list.Bounds.Height - header.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AppendEntries(WorkbenchLogJournal journal)
    {
        var details = string.Join(Environment.NewLine,
            Enumerable.Range(0, 80).Select(index => $"at Example.Operation{index}() in /project/Example.cs:line {index}"));
        for (var index = 0; index < 150; index++)
        {
            journal.Append(WorkbenchLogLevel.ERROR, "Fixture", $"Failure {index}", details);
        }
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
