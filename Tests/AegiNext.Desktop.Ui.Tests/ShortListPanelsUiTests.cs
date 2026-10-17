using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings.Assets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ShortListPanelsUiTests
{
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.SUBTITLES, "SubtitleList", 320, "zh-CN", true)]
    [InlineData(WorkbenchPanelIds.SUBTITLES, "SubtitleList", 950, "en-US", false)]
    [InlineData(WorkbenchPanelIds.ASSETS, "AssetList", 320, "zh-CN", true)]
    [InlineData(WorkbenchPanelIds.ASSETS, "AssetList", 950, "en-US", false)]
    [InlineData(WorkbenchPanelIds.LOG, "LogEntries", 320, "zh-CN", true)]
    [InlineData(WorkbenchPanelIds.LOG, "LogEntries", 950, "en-US", false)]
    [InlineData(WorkbenchPanelIds.EFFECT_PRESETS, "EffectPresetList", 320, "zh-CN", true)]
    [InlineData(WorkbenchPanelIds.EFFECT_PRESETS, "EffectPresetList", 950, "en-US", false)]
    public async Task ShortFloatingDocksKeepScrollableListsAndRecoverTheirNormalViewport(string panelId,
        string listName, double width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.Session.EffectScripts.Completion;
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var original = CreateDocument(80, true);
        context.Session.Editor.Reset(original);
        context.ViewModel.Assets.SetViewMode(AssetBrowserViewMode.LIST);
        context.Session.Journal.Clear();
        for (var index = 0; index < 80; index++)
        {
            context.Session.LogInfo("Layout", $"Entry {index + 1}");
        }
        var panel = context.Window.Panels[panelId];
        context.Window.Layouts.Float(panelId);
        Flush(context.Window);
        var host = Assert.IsType<WorkbenchFloatingHostWindow>(Assert.Single(context.Window.Layouts.FloatingWindows));
        try
        {
            host.Width = width;
            host.Height = 600;
            host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            host.DockHost.VerticalAlignment = VerticalAlignment.Top;
            var outer = Assert.Single(panel.GetVisualDescendants().OfType<OverflowScrollViewer>());
            var list = UiTestActions.Find<ListBox>(host, listName);
            var selected = list.SelectedItem;
            Assert.NotEmpty(list.Items);
            foreach (var height in new[] { 48d, 80d, 120d })
            {
                host.DockHost.Height = height;
                Flush(host);
                Assert.Same(panel, context.Window.Panels[panelId]);
                Assert.True(panel.Bounds.Height > 0);
                Assert.True(panel.Bounds.Height <= height);
                Assert.True(outer.Viewport.Height > 0);
                Assert.True(outer.Extent.Height > outer.Viewport.Height);
                AssertMinimumListViewport(list);
                if (panelId == WorkbenchPanelIds.ASSETS)
                {
                    var tree = UiTestActions.Find<TreeView>(host, "FolderTree");
                    var treeScroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Assert.True(treeScroll.Viewport.Height >= 63,
                        $"Folder tree viewport collapsed to {treeScroll.Viewport.Height}.");
                }
                outer.Offset = new(0, outer.Extent.Height);
                Flush(host);
                AssertLastItemReachable(host, list, outer);
                Assert.Same(selected, list.SelectedItem);
                Assert.Same(original, context.Session.DocumentSnapshot);
                Assert.False(context.Session.Editor.CanUndo);
            }
            host.DockHost.Height = 500;
            Flush(host);
            Assert.InRange(outer.Extent.Height - outer.Viewport.Height, -1, 1);
            Assert.InRange(outer.Offset.Y, 0, 1);
            AssertMinimumListViewport(list);
            AssertLastItemReachable(host, list, outer);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.HasUnsavedChanges);
        }
        finally
        {
            host.Close();
            Flush(context.Window);
        }
    }

    [AvaloniaTheory]
    [InlineData(320)]
    [InlineData(950)]
    public async Task RepeatedShortDockResizesKeepSubtitleVirtualizationAndInvalidTimeDrafts(double width)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var original = CreateDocument(1000, false);
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(original.Subtitles[0].Id);
        context.Window.Layouts.Float(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        var host = Assert.IsType<WorkbenchFloatingHostWindow>(Assert.Single(context.Window.Layouts.FloatingWindows));
        var row = context.ViewModel.Subtitles.VisibleRows[0];
        try
        {
            host.Width = width;
            host.Height = 600;
            host.DockHost.VerticalAlignment = VerticalAlignment.Top;
            var list = UiTestActions.Find<ListBox>(host, "SubtitleList");
            var outer = UiTestActions.Find<OverflowScrollViewer>(host, "SubtitlesPanelScrollViewer");
            row.StartText = "invalid";
            foreach (var height in new[] { 80d, 120d, 500d, 80d })
            {
                host.DockHost.Height = height;
                Flush(host);
                outer.Offset = new(0, outer.Extent.Height);
                Flush(host);
                AssertMinimumListViewport(list);
                Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
                AssertLastItemReachable(host, list, outer);
                var realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
                Assert.InRange(realized, 1, 64);
                Assert.Equal(1000, list.Items.Count);
                Assert.Equal("invalid", row.StartText);
                Assert.Same(original, context.Session.DocumentSnapshot);
                Assert.False(context.Session.Editor.CanUndo);
            }
        }
        finally
        {
            row.Accept(original.Subtitles[0]);
            host.Close();
            Flush(context.Window);
        }
    }

    [AvaloniaFact]
    public async Task PresetPreviewStopsBehindTheOuterClipAndResumesOnlyAfterTheCardReturns()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var original = host.Session.DocumentSnapshot;
        var position = host.Session.ViewModel.Preview.Position;
        host.Window.Width = 320;
        host.Window.Height = 300;
        host.Flush();
        var card = host.Card("fade-in-out");
        host.List.ScrollIntoView(card.ViewModel);
        host.Flush();
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        await host.WaitUntilAsync(() => card.IsPreviewAnimating);
        var request = card.Request;
        host.Window.Width = 321;
        host.Flush();
        Assert.Same(request, card.Request);
        Assert.True(card.IsPreviewAnimating);

        var outer = UiTestActions.Find<OverflowScrollViewer>(host.Window, "EffectPresetsPanelScrollViewer");
        host.Window.Height = 48;
        outer.Offset = default;
        host.Flush();
        Assert.True(outer.Extent.Height > outer.Viewport.Height);
        await host.WaitUntilAsync(() => card.Request is null);
        Assert.Null(host.View.ActivePreviewId);
        Assert.False(card.IsPreviewAnimating);
        Assert.Null(card.PresentedPosition);

        outer.Offset = new(0, outer.Extent.Height);
        host.Flush();
        await host.WaitUntilAsync(() => card.Request is not null && card.PresentedPosition is not null);
        host.Window.MouseMove(new(-10, -10));
        host.Window.MouseMove(new(host.Window.Width / 2, host.Window.Height - 12));
        host.Flush();
        await host.WaitUntilAsync(() => card.IsPreviewAnimating);
        Assert.Equal(card.ViewModel.Id, host.View.ActivePreviewId);
        Assert.Same(original, host.Session.DocumentSnapshot);
        Assert.Equal(position, host.Session.ViewModel.Preview.Position);
        Assert.False(host.Session.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument(int count, bool includeAssets)
    {
        var subtitles = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = $"字幕 Subtitle {index + 1}",
            Start = new(index * 3),
            End = new(index * 3 + 2)
        }).ToImmutableArray();
        var assets = includeAssets
            ? Enumerable.Range(0, 80).Select(index =>
                new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, $"images/short-list-{index}.png")).ToImmutableArray()
            : ImmutableArray<ProjectAsset>.Empty;
        return new()
        {
            Subtitles = subtitles,
            Layers = subtitles.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray(),
            Assets = assets,
            AssetLibrary = new()
            {
                Items = assets.Select((asset, index) =>
                    new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, $"Asset {index + 1}")).ToImmutableArray(),
                Folders = includeAssets
                    ? Enumerable.Range(0, 40).Select(index => new ProjectAssetFolder(Guid.NewGuid(), $"Folder {index + 1}")).ToImmutableArray()
                    : ImmutableArray<ProjectAssetFolder>.Empty
            }
        };
    }

    private static void AssertMinimumListViewport(ListBox list)
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(double.IsFinite(scroll.Viewport.Height));
        Assert.True(scroll.Viewport.Height >= 63, $"{list.Name} viewport collapsed to {scroll.Viewport.Height}.");
    }

    private static void AssertLastItemReachable(Window host, ListBox list, OverflowScrollViewer outer)
    {
        var lastIndex = list.Items.Count - 1;
        var last = list.Items[lastIndex];
        Assert.NotNull(last);
        list.ScrollIntoView(last);
        Flush(host);
        var container = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(lastIndex));
        var inner = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var innerViewport = inner.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        var outerViewport = outer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        AssertVisibleInBothViewports(container, innerViewport, outerViewport);
    }

    private static void AssertVisibleInBothViewports(Control content, Control inner, Control outer)
    {
        var contentOrigin = content.TranslatePoint(default, outer);
        var innerOrigin = inner.TranslatePoint(default, outer);
        Assert.NotNull(contentOrigin);
        Assert.NotNull(innerOrigin);
        var bounds = new Rect(contentOrigin.Value, content.Bounds.Size);
        var visible = bounds.Intersect(new Rect(innerOrigin.Value, inner.Bounds.Size)).Intersect(new Rect(outer.Bounds.Size));
        Assert.True(visible.Width > 0 && visible.Height > 0,
            $"{content.GetType().Name} at {bounds} is clipped by the list or panel viewport.");
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
