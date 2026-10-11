using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Assets;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssetLibraryPanelUiTests
{
    [AvaloniaFact]
    public async Task PastedBitmapIsManagedEvenWhenAutomaticCopyIsDisabled()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.Editor.Reset(new() { AssetLibrary = new() { AutoCopyToSources = false } });
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
    public async Task FolderSelectionSearchAndImportPolicyUseTheProjectDocument()
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
            model.AutoCopyToSources = false;
            Assert.False(context.Session.Editor.Snapshot.AssetLibrary.AutoCopyToSources);
            Assert.True(context.Session.Editor.Undo());
            Assert.True(model.AutoCopyToSources);
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
}
