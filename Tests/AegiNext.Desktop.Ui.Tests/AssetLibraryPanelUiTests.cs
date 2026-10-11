using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Assets;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Assets;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using System.Collections.Immutable;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssetLibraryPanelUiTests
{
    [AvaloniaFact]
    public async Task DisplayModesWheelZoomAndFolderSplitterPreserveProjectAndSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var colors = new[] { SKColors.Coral, SKColors.CornflowerBlue, SKColors.MediumSeaGreen, SKColors.Goldenrod, SKColors.Orchid, SKColors.SlateBlue };
        var assets = colors.Select((color, index) => WriteImageFixture(context.Session.ProjectDirectory, $"graphic-{index}.png", color)).ToImmutableArray();
        var items = assets.Select((asset, index) => new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, $"图形素材 Graphic {index + 1}")).ToImmutableArray();
        var original = new ProjectDocument { Assets = assets, AssetLibrary = new() { Items = items, Folders = [new(Guid.NewGuid(), "字幕装饰"), new(Guid.NewGuid(), "参考图片")] } };
        context.Session.Editor.Reset(original);
        var model = context.ViewModel.Assets;
        using var panel = new AssetsPanelView(model, context.Session);
        var window = new Window { Width = 620, Height = 480, Content = panel };
        try
        {
            window.Show();
            Flush(window);
            model.SelectedItem = model.Items[0];
            var selected = model.SelectedItem;
            var list = panel.FindControl<ListBox>("AssetList")!;
            var import = panel.FindControl<Button>("ImportAssetsButton")!;
            var search = panel.FindControl<TextBox>("AssetSearchInput")!;
            Assert.Equal(32, import.Bounds.Width);
            Assert.Equal(import.Bounds.Width, import.Bounds.Height);
            Assert.True(import.TranslatePoint(default, panel)!.Value.X >= search.TranslatePoint(default, panel)!.Value.X + search.Bounds.Width);
            Click(window, panel.FindControl<ToolbarToggleButton>("AssetIconViewButton")!);
            Flush(window);
            Assert.True(model.IsIconView);
            Assert.IsType<WrapPanel>(list.ItemsPanelRoot);
            Assert.Same(selected, list.SelectedItem);
            Click(window, panel.FindControl<ToolbarToggleButton>("AssetIconViewButton")!);
            Assert.True(panel.FindControl<ToolbarToggleButton>("AssetIconViewButton")!.IsChecked);
            var position = list.TranslatePoint(new Point(16, 16), window)!.Value;
            var initialSize = model.ThumbnailSize;
            window.MouseWheel(position, new(0, 2), RawInputModifiers.Control);
            Assert.Equal(initialSize + 16, model.ThumbnailSize);
            window.MouseWheel(position, new(0, -1), RawInputModifiers.Meta);
            Assert.Equal(initialSize + 8, model.ThumbnailSize);
            window.MouseWheel(position, new(0, -1));
            Assert.Equal(initialSize + 8, model.ThumbnailSize);
            window.MouseWheel(position, new(0, 100), RawInputModifiers.Control);
            Assert.Equal(192, model.ThumbnailSize);
            Flush(window);
            Assert.Same(selected, list.SelectedItem);
            Click(window, panel.FindControl<ToolbarToggleButton>("AssetListViewButton")!);
            Flush(window);
            Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
            Assert.Same(selected, list.SelectedItem);
            AssertCompactListRows(list);
            Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            var splitter = panel.FindControl<GridSplitter>("AssetFolderSplitter")!;
            var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
            var initialWidth = model.FolderPaneWidth;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 0));
            window.MouseUp(start + new Vector(40, 0), MouseButton.Left);
            Flush(window);
            Assert.True(model.FolderPaneWidth > initialWidth);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(model.FolderPaneWidth, context.Session.Preferences.AssetBrowser.FolderPaneWidth);
            model.SetFolderPaneWidth(500);
            window.Width = 300;
            Flush(window);
            var columns = panel.FindControl<Grid>("AssetBrowserColumns")!;
            Assert.True(columns.ColumnDefinitions[0].Width.Value + 6 + 88 <= columns.Bounds.Width);
            Assert.Equal(500, model.FolderPaneWidth);
            window.Width = 1000;
            Flush(window);
            Assert.Equal(500, columns.ColumnDefinitions[0].Width.Value);
            model.SetFolderPaneWidth(initialWidth + 40);
            window.Width = 620;
            model.ResizeThumbnails(-100);
            await WaitUntilAsync(() => panel.GetVisualDescendants().OfType<AssetThumbnail>().Any(thumbnail => thumbnail.Source is not null));
            var selectedId = model.SelectedItem!.Id;
            Localization.SetLanguage("zh-CN");
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Flush(window);
            Assert.Equal(selectedId, model.SelectedItem!.Id);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            await WaitUntilAsync(() => panel.GetVisualDescendants().OfType<AssetThumbnail>().Any(thumbnail => thumbnail.Source is not null));
            Capture(window, "asset-browser-list-dark.png");
            Localization.SetLanguage("en-US");
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Flush(window);
            await WaitUntilAsync(() => panel.GetVisualDescendants().OfType<AssetThumbnail>().Any(thumbnail => thumbnail.Source is not null));
            AssertCompactListRows(list);
            Capture(window, "asset-browser-list-light.png");
            Click(window, panel.FindControl<ToolbarToggleButton>("AssetIconViewButton")!);
            model.ResizeThumbnails(6);
            await WaitUntilAsync(() => panel.GetVisualDescendants().OfType<AssetThumbnail>().Any(thumbnail => thumbnail.Source is not null));
            Flush(window);
            Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>(), container =>
                Assert.True(container.Bounds.Height <= model.ThumbnailSize + 32));
            Capture(window, "asset-browser-icons-light.png");
            Localization.SetLanguage("zh-CN");
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Flush(window);
            await WaitUntilAsync(() => panel.GetVisualDescendants().OfType<AssetThumbnail>().Any(thumbnail => thumbnail.Source is not null));
            Capture(window, "asset-browser-icons-dark.png");
        }
        finally
        {
            window.Close();
            Localization.SetLanguage("en-US");
        }
    }

    [AvaloniaFact]
    public async Task RightClickFolderTargetsItsOwnContextMenuAndDeletionConfirmation()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var first = new ProjectAssetFolder(Guid.NewGuid(), "First");
        var second = new ProjectAssetFolder(Guid.NewGuid(), "Second");
        var original = new ProjectDocument { AssetLibrary = new() { Folders = [first, second] } };
        context.Session.Editor.Reset(original);
        var model = context.ViewModel.Assets;
        using var panel = new AssetsPanelView(model, context.Session);
        var window = new Window { Width = 520, Height = 420, Content = panel };
        try
        {
            window.Show();
            Flush(window);
            var tree = panel.FindControl<TreeView>("FolderTree")!;
            model.SelectedFolder = model.Folders[0].Children.Single(folder => folder.Id == second.Id);
            var label = tree.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "First");
            var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Flush(window);
            Assert.Equal(first.Id, model.SelectedFolder!.Id);
            Assert.True(tree.ContextMenu!.IsOpen);
            Assert.All(tree.ContextMenu.Items.OfType<MenuItem>(), item => Assert.NotNull(item.Icon));
            model.SelectedFolder = model.Folders[0].Children.Single(folder => folder.Id == second.Id);
            var remove = tree.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "RemoveFolderMenuItem");
            UiTestActions.ClickFontMenuItem(remove);
            var dialog = await FindDialogAsync<AssetFolderDeletionDialog>(context.Window);
            Assert.Contains("First", UiTestActions.Find<TextBlock>(dialog, "AssetFolderDeletionMessage").Text);
            Click(dialog, UiTestActions.Find<Button>(dialog, "CancelButton"));
            await WaitUntilAsync(() => !context.Session.IsProjectBusy);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            foreach (var dialog in context.Window.OwnedWindows.OfType<AssetFolderDeletionDialog>().ToArray())
            {
                dialog.Close(false);
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThumbnailsLoadOnlyInsideViewportAndReloadAfterScrollingBack()
    {
        using var environment = new UiTestEnvironment();
        var asset = WriteImageFixture(environment.DirectoryPath, "viewport.png", SKColors.Coral);
        var first = new AssetThumbnail { Asset = asset, Directory = environment.DirectoryPath, Width = 96, Height = 96 };
        var last = new AssetThumbnail { Asset = asset, Directory = environment.DirectoryPath, Width = 96, Height = 96 };
        var scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { first, new Border { Height = 1000 }, last } }
        };
        var window = new Window { Width = 240, Height = 240, Content = scroller };
        try
        {
            window.Show();
            Flush(window);
            await WaitUntilAsync(() => first.Source is not null);
            Assert.Null(last.Source);
            scroller.Offset = new(0, scroller.Extent.Height);
            Flush(window);
            await WaitUntilAsync(() => last.Source is not null);
            Assert.Null(first.Source);
            scroller.Offset = default;
            Flush(window);
            await WaitUntilAsync(() => first.Source is not null);
            Assert.Null(last.Source);
        }
        finally
        {
            window.Close();
        }
        Assert.Null(first.Source);
        Assert.Null(last.Source);
    }

    [AvaloniaFact]
    public async Task FolderRenameAndDeletionRequireDialogAndKeepFrozenTarget()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var first = new ProjectAssetFolder(Guid.NewGuid(), "First");
        var second = new ProjectAssetFolder(Guid.NewGuid(), "Second");
        var child = new ProjectAssetFolder(Guid.NewGuid(), "Child", first.Id);
        var original = new ProjectDocument { AssetLibrary = new() { Folders = [first, second, child] } };
        context.Session.Editor.Reset(original);
        var model = context.ViewModel.Assets;
        var target = model.CaptureFolderTarget(first.Id)!;
        var pending = model.RenameFolderAsync(target);
        var rename = await FindDialogAsync<AssetFolderRenameDialog>(context.Window);
        var input = UiTestActions.Find<TextBox>(rename, "AssetFolderNameInput");
        input.Text = "   ";
        Assert.False(UiTestActions.Find<Button>(rename, "ConfirmButton").IsEnabled);
        input.Text = "Renamed";
        model.SelectedFolder = model.Folders[0].Children.Single(folder => folder.Id == second.Id);
        Click(rename, UiTestActions.Find<Button>(rename, "ConfirmButton"));
        await pending;
        Assert.Equal("Renamed", context.Session.Editor.Snapshot.AssetLibrary.Folders.Single(folder => folder.Id == first.Id).Name);
        Assert.Equal("Second", context.Session.Editor.Snapshot.AssetLibrary.Folders.Single(folder => folder.Id == second.Id).Name);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);

        pending = model.RemoveFolderAsync(model.CaptureFolderTarget(first.Id)!);
        var deletion = await FindDialogAsync<AssetFolderDeletionDialog>(context.Window);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Contains("First", UiTestActions.Find<TextBlock>(deletion, "AssetFolderDeletionMessage").Text);
        Click(deletion, UiTestActions.Find<Button>(deletion, "CancelButton"));
        await pending;
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);

        pending = model.RemoveFolderAsync(model.CaptureFolderTarget(first.Id)!);
        deletion = await FindDialogAsync<AssetFolderDeletionDialog>(context.Window);
        Click(deletion, UiTestActions.Find<Button>(deletion, "DeleteButton"));
        await pending;
        Assert.DoesNotContain(context.Session.Editor.Snapshot.AssetLibrary.Folders, folder => folder.Id == first.Id);
        Assert.Null(context.Session.Editor.Snapshot.AssetLibrary.Folders.Single(folder => folder.Id == child.Id).ParentId);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task StaleFolderDialogCannotRenameAReplacementProject()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var folder = new ProjectAssetFolder(Guid.NewGuid(), "Original");
        var original = new ProjectDocument { AssetLibrary = new() { Folders = [folder] } };
        context.Session.Editor.Reset(original);
        var model = context.ViewModel.Assets;
        var pending = model.RenameFolderAsync(model.CaptureFolderTarget(folder.Id)!);
        var dialog = await FindDialogAsync<AssetFolderRenameDialog>(context.Window);
        var replacement = original with { Name = "Replacement", AssetLibrary = new() { Folders = [folder with { Name = "Replacement folder" }] } };
        context.Session.Editor.Reset(replacement);
        UiTestActions.Find<TextBox>(dialog, "AssetFolderNameInput").Text = "Should not apply";
        Click(dialog, UiTestActions.Find<Button>(dialog, "ConfirmButton"));
        await pending;
        Assert.Same(replacement, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        var stale = model.CaptureFolderTarget(folder.Id)!;
        context.Session.Editor.Reset(original);
        await model.RemoveFolderAsync(stale);
        Assert.DoesNotContain(context.Window.OwnedWindows, window => window is AssetFolderDeletionDialog);
        Assert.Same(original, context.Session.Editor.Snapshot);
    }

    [AvaloniaFact]
    public async Task PastedBitmapIsManagedEvenWhenAutomaticCopyIsDisabled()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with
        {
            Projects = preferences.Projects with { CopyImportedAssetsToSources = false }
        });
        var clipboard = context.Window.Clipboard!;
        using var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        await clipboard.SetBitmapAsync(bitmap);

        await context.Session.Assets.PasteAsync(clipboard, true);

        var snapshot = context.Session.Editor.Snapshot;
        var asset = Assert.Single(snapshot.Assets);
        Assert.Equal(ProjectAssetStorage.MANAGED, asset.Storage);
        Assert.StartsWith("sources/", asset.RelativePath);
        Assert.Single(snapshot.AssetLibrary.Items);
        Assert.Equal(LayerKind.IMAGE, context.Session.SelectedLayer!.Kind);
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(asset, context.Session.ProjectDirectory)));
        Assert.True(context.Session.Editor.Undo());
        Assert.Empty(context.Session.Editor.Snapshot.Assets);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task FolderSelectionAndSearchUseTheProjectDocument()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/missing.png");
        var folder = new ProjectAssetFolder(Guid.NewGuid(), "Annotations");
        var child = new ProjectAssetFolder(Guid.NewGuid(), "Arrows", folder.Id);
        var rootItem = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Caption background");
        var nestedItem = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Arrow overlay", child.Id);
        context.Session.Editor.Reset(new()
        {
            Assets = [asset], AssetLibrary = new() { Folders = [folder, child], Items = [rootItem, nestedItem] }
        });
        var model = context.ViewModel.Assets;
        using var panel = new AssetsPanelView(model, context.Session);
        var window = new Window { Width = 420, Height = 720, Content = panel, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(rootItem.Id, Assert.Single(model.Items).Id);
            var tree = panel.FindControl<TreeView>("FolderTree")!;
            tree.SelectedItem = model.Folders[0].Children[0].Children[0];
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(nestedItem.Id, Assert.Single(model.Items).Id);
            model.Search = "background";
            Assert.Equal(rootItem.Id, Assert.Single(model.Items).Id);
            Assert.False(panel.CanExecuteFocusCommand(WorkbenchCommand.PASTE_CLIPS, new TextBox()));
            model.Search = string.Empty;
            await model.CheckAvailabilityAsync();
            Assert.NotEmpty(Assert.Single(model.Items).Status);
            model.SelectedItem = model.Items[0];
            if (Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, "project-assets.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssetContextMenuTargetsClickedItemAndReplacesInlineActions(bool icons)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = WriteImageFixture(context.Session.ProjectDirectory, "menu.png", SKColors.Coral);
        var first = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "First");
        var second = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Second");
        var original = new ProjectDocument { Assets = [asset], AssetLibrary = new() { Items = [first, second] } };
        context.Session.Editor.Reset(original);
        var model = context.ViewModel.Assets;
        if (icons)
        {
            model.IconViewCommand.Execute(null);
        }
        using var panel = new AssetsPanelView(model, context.Session);
        var window = new Window { Width = 620, Height = 400, Content = panel, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        try
        {
            window.Show();
            Flush(window);
            var list = panel.FindControl<ListBox>("AssetList")!;
            Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            model.SelectedItem = model.Items.Single(item => item.Id == second.Id);
            OpenAssetMenu(window, list, first.Name);
            Assert.Equal(first.Id, model.SelectedItem!.Id);
            Assert.All(list.ContextMenu!.Items.OfType<MenuItem>(), item => Assert.NotNull(item.Icon));
            Assert.Equal(5, list.ContextMenu.Items.OfType<MenuItem>().Count());
            var rename = list.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "RenameAssetMenuItem");
            Localization.SetLanguage("zh-CN");
            Assert.Equal(Localization.Get("Workbench.RenameAsset"), rename.Header);
            if (!list.ContextMenu.IsOpen)
            {
                OpenAssetMenu(window, list, first.Name);
            }
            Capture(window, icons ? "asset-menu-icons-dark.png" : "asset-menu-list-dark.png");
            model.SelectedItem = model.Items.Single(item => item.Id == second.Id);
            UiTestActions.ClickFontMenuItem(rename);
            var dialog = await FindDialogAsync<AssetItemRenameDialog>(context.Window);
            var input = UiTestActions.Find<TextBox>(dialog, "AssetItemNameInput");
            Assert.Equal(first.Name, input.Text);
            input.Text = "   ";
            Assert.False(UiTestActions.Find<Button>(dialog, "ConfirmButton").IsEnabled);
            input.Text = "Renamed";
            Click(dialog, UiTestActions.Find<Button>(dialog, "ConfirmButton"));
            await WaitUntilAsync(() => context.Session.Editor.Snapshot.AssetLibrary.Items.Any(item => item.Name == "Renamed"));
            Assert.Equal(second, context.Session.Editor.Snapshot.AssetLibrary.Items.Single(item => item.Id == second.Id));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);

            OpenAssetMenu(window, list, first.Name);
            model.SelectedItem = model.Items.Single(item => item.Id == second.Id);
            UiTestActions.ClickFontMenuItem(list.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "PlaceAssetMenuItem"));
            await WaitUntilAsync(() => context.Session.Editor.Snapshot.Layers.Length == 1);
            Assert.Equal(first.Name, Assert.Single(context.Session.Editor.Snapshot.Layers).Name);
            OpenAssetMenu(window, list, first.Name);
            Assert.False(list.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "RemoveAssetMenuItem").IsEnabled);
            list.ContextMenu.Close();
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);

            OpenAssetMenu(window, list, first.Name);
            model.SelectedItem = model.Items.Single(item => item.Id == second.Id);
            UiTestActions.ClickFontMenuItem(list.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "RemoveAssetMenuItem"));
            await WaitUntilAsync(() => context.Session.Editor.Snapshot.AssetLibrary.Items.Length == 1);
            Assert.Equal(second, Assert.Single(context.Session.Editor.Snapshot.AssetLibrary.Items));
            Assert.True(File.Exists(Path.Combine(context.Session.ProjectDirectory, "menu.png")));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            var blank = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height - 8), window)!.Value;
            window.MouseDown(blank, MouseButton.Right);
            window.MouseUp(blank, MouseButton.Right);
            Assert.False(list.ContextMenu.IsOpen);
            Assert.Null(model.SelectedItem);
        }
        finally
        {
            foreach (var dialog in context.Window.OwnedWindows.OfType<AssetItemRenameDialog>().ToArray())
            {
                dialog.Close(null);
            }
            window.Close();
            Localization.SetLanguage("en-US");
        }
    }

    private static void OpenAssetMenu(Window window, ListBox list, string name)
    {
        Flush(window);
        var label = list.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == name);
        var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Flush(window);
        Assert.True(list.ContextMenu!.IsOpen);
    }

    private static void AssertCompactListRows(ListBox list)
    {
        var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.NotEmpty(containers);
        foreach (var container in containers)
        {
            Assert.Equal(32, container.Bounds.Height);
            var row = Assert.IsType<AssetLibraryRow>(container.DataContext);
            var thumbnail = Assert.Single(container.GetVisualDescendants().OfType<AssetThumbnail>());
            var title = container.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == row.Name);
            Assert.InRange(thumbnail.Bounds.Width, 16, 24);
            Assert.InRange(thumbnail.Bounds.Height, 1, 24);
            var imageCenter = thumbnail.TranslatePoint(new Rect(thumbnail.Bounds.Size).Center, container)!.Value;
            var textCenter = title.TranslatePoint(new Rect(title.Bounds.Size).Center, container)!.Value;
            Assert.InRange(Math.Abs(imageCenter.Y - textCenter.Y), 0, 0.5);
            Assert.True(title.TranslatePoint(default, container)!.Value.X >= thumbnail.TranslatePoint(default, container)!.Value.X + thumbnail.Bounds.Width);
        }
    }

    private static void Click(Window window, Control control)
    {
        Flush(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task<T> FindDialogAsync<T>(Window owner) where T : Window
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (owner.OwnedWindows.OfType<T>().FirstOrDefault() is { } dialog)
            {
                Flush(dialog);
                return dialog;
            }
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        throw new InvalidOperationException($"Expected dialog {typeof(T).Name} was not shown.");
    }

    private static void Capture(Window window, string filename)
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
        }
    }

    private static ProjectAsset WriteImageFixture(string directory, string filename, SKColor color)
    {
        Directory.CreateDirectory(directory);
        using var bitmap = new SKBitmap(160, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(80, 50, 30, paint);
        paint.Color = color.WithAlpha(210);
        canvas.DrawRect(72, 10, 16, 80, paint);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, filename), encoded.ToArray());
        return new(Guid.NewGuid(), ProjectAssetKind.IMAGE, filename);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return;
            }
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.True(predicate());
    }
}
