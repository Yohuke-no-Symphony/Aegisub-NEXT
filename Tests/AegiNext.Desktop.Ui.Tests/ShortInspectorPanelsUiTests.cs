using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Panels.DrawingTools;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Panels.SubtitleDetails;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ShortInspectorPanelsUiTests
{
    [AvaloniaTheory]
    [InlineData(18)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(130)]
    public async Task SubtitleDetailsScrollsToBothEndsWithinTheAvailableHeight(double height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Subtitle ABC 中文");
        context.Session.SelectCue(cueId);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.DocumentSnapshot;
        var panel = new SubtitleDetailsPanelView(context.Session);
        var host = CreateHost(panel, height);
        try
        {
            host.Show();
            var scroll = UiTestActions.Find<ScrollViewer>(host, "SubtitleDetailsScroll");
            AssertScrollEndpoints(host, panel, scroll, height);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
            panel.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(18)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(130)]
    public async Task SelectionHintAndEveryStyleEditorRemainReachableAtShortHeights(double height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        foreach (var kind in new[] { LayerKind.SUBTITLE, LayerKind.SHAPE, LayerKind.IMAGE })
        {
            var cue = new SubtitleLine { Text = "Subtitle ABC 中文", End = new(4) };
            var otherCue = cue with { Id = Guid.NewGuid(), Start = new(4), End = new(8) };
            var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/short-inspector.png");
            var layer = kind switch
            {
                LayerKind.SUBTITLE => new ProjectLayer { Kind = kind, SubtitleId = cue.Id, End = cue.End },
                LayerKind.SHAPE => new ProjectLayer { Kind = kind, Shape = new(ShapeKind.RECTANGLE, 100, 80), End = new(4) },
                _ => new ProjectLayer { Kind = kind, Image = new(asset.Id, 200, 100), End = new(4) }
            };
            var other = layer with
            {
                Id = Guid.NewGuid(), Start = new(4), End = new(8),
                SubtitleId = kind == LayerKind.SUBTITLE ? otherCue.Id : null
            };
            var original = new ProjectDocument
            {
                Subtitles = kind == LayerKind.SUBTITLE ? [cue, otherCue] : [], Assets = [asset], Layers = [layer, other]
            };
            context.Session.Editor.Reset(original);
            context.Session.SelectLayer(layer.Id, [layer.Id, other.Id]);
            var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
            var host = CreateHost(panel, height);
            try
            {
                host.Show();
                var hint = UiTestActions.Find<TextBlock>(host, "StyleSelectionLabel");
                Assert.True(hint.IsVisible);
                Assert.False(string.IsNullOrWhiteSpace(hint.Text));
                var editor = Assert.IsAssignableFrom<Control>(UiTestActions.Find<ContentControl>(host, "StyleEditorHost").Content);
                Assert.True(editor.Bounds.Height > 0);
                var scroll = UiTestActions.Find<ScrollViewer>(host, "StylesPanelScrollViewer");
                AssertScrollEndpoints(host, panel, scroll, height);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.Same(original, context.Session.DocumentSnapshot);
            }
            finally
            {
                host.Close();
                panel.Dispose();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(18)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(130)]
    public async Task ImageToolbarAndParametersShareAUsableShortViewport(double height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/short-inspector.png");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100) };
        var original = new ProjectDocument { Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(original);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var panel = new ImageStyleView(context.ViewModel.Styles.Image, context.Session);
        var host = CreateHost(panel, height);
        try
        {
            host.Show();
            var scroll = UiTestActions.Find<ScrollViewer>(host, "ImageStyleScrollViewer");
            AssertScrollEndpoints(host, panel, scroll, height);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var replace = UiTestActions.Find<Button>(host, "ReplaceImageButton");
            Assert.Equal(32, replace.Bounds.Width);
            Assert.Equal(32, replace.Bounds.Height);
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
            panel.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(18)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(130)]
    public async Task DrawingToolsRetainTheirSizeAndScrollInBothConstrainedDirections(double height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var original = context.Session.DocumentSnapshot;
        var panel = new DrawingToolsPanelView(context.ViewModel.DrawingTools);
        var host = CreateHost(panel, height, 260);
        try
        {
            host.Show();
            var scroll = UiTestActions.Find<ScrollViewer>(host, "DrawingToolsScrollViewer");
            AssertScrollEndpoints(host, panel, scroll, height);
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            foreach (var button in panel.GetVisualDescendants().OfType<ToolbarToggleButton>())
            {
                Assert.Equal(32, button.Bounds.Width);
                Assert.Equal(32, button.Bounds.Height);
            }
            scroll.Offset = new(scroll.Extent.Width, scroll.Extent.Height);
            Flush(host);
            var last = UiTestActions.Find<Button>(host, "SubtitleBackgroundButton");
            var presenter = Presenter(scroll);
            var right = last.TranslatePoint(new(last.Bounds.Width, 0), presenter)!.Value.X;
            Assert.InRange(right, 1, presenter.Bounds.Width + 1);
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
            panel.Dispose();
        }
    }

    private static Window CreateHost(Control panel, double height, double width = 420)
    {
        return new()
        {
            Width = width,
            Height = 360,
            Content = new Border
            {
                Height = height,
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true,
                Child = panel
            }
        };
    }

    private static void AssertScrollEndpoints(Window host, Control panel, ScrollViewer scroll, double height)
    {
        Flush(host);
        Assert.InRange(Math.Abs(panel.Bounds.Height - height), 0, 1);
        Assert.InRange(scroll.Bounds.Height, 1, height + 1);
        Assert.InRange(scroll.Viewport.Height, 1, height + 1);
        var content = Assert.IsAssignableFrom<Control>(scroll.Content);
        var presenter = Presenter(scroll);
        scroll.Offset = default;
        Flush(host);
        var top = content.TranslatePoint(default, presenter)!.Value.Y - content.Margin.Top;
        Assert.InRange(top, -1, 1);
        scroll.Offset = new(0, scroll.Extent.Height);
        Flush(host);
        var expectedOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        Assert.InRange(Math.Abs(scroll.Offset.Y - expectedOffset), 0, 1);
        var bottom = content.TranslatePoint(new(0, content.Bounds.Height), presenter)!.Value.Y + content.Margin.Bottom;
        Assert.InRange(bottom, 0, presenter.Bounds.Height + 1);
    }

    private static ScrollContentPresenter Presenter(ScrollViewer scroll)
    {
        return scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First(presenter =>
            ReferenceEquals(presenter.GetVisualAncestors().OfType<ScrollViewer>().First(), scroll));
    }

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
