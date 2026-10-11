using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Assets;
using AegiNext.Desktop.Settings.Assets;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssetDragUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PointerDragProvidesOneNativeWritableItemAndRetainsTypedIdentity(bool icons, bool folder)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = WriteImage(context.Session.ProjectDirectory);
        var item = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Drag image");
        var directory = new ProjectAssetFolder(Guid.NewGuid(), "Drag folder");
        var original = new ProjectDocument
        {
            Assets = [asset],
            AssetLibrary = new() { Items = [item], Folders = [directory] }
        };
        context.Session.Editor.Reset(original);
        context.ViewModel.Assets.SetViewMode(icons ? AssetBrowserViewMode.ICONS : AssetBrowserViewMode.LIST);
        using var panel = new AssetsPanelView(context.ViewModel.Assets, context.Session);
        var window = new Window { Width = 620, Height = 480, Content = panel };
        IDataTransfer? dragData = null;
        var allowedEffects = DragDropEffects.None;
        panel.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            dragData = e.DataTransfer;
            allowedEffects = e.DragEffects;
        }, handledEventsToo: true);
        try
        {
            window.Show();
            Flush(window);
            var label = panel.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == (folder ? directory.Name : item.Name));
            var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(2, 0), RawInputModifiers.LeftMouseButton);
            Assert.Null(dragData);
            window.MouseMove(point + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(point + new Vector(18, 0), RawInputModifiers.LeftMouseButton);
            Flush(window);
            var data = Assert.IsAssignableFrom<IDataTransfer>(dragData);
            var transferred = Assert.Single(data.Items);
            var payload = Assert.IsType<AssetDragPayload>(transferred.TryGetValue(AssetDragPayload.Format));
            Assert.Equal(new AssetDragPayload(original.Id, folder ? directory.Id : item.Id, folder), payload);
            var nativeFormats = transferred.Formats.Where(format => format.Kind != DataFormatKind.InProcess).ToArray();
            Assert.NotEmpty(nativeFormats);
            Assert.All(nativeFormats, format => Assert.False(string.IsNullOrEmpty(Assert.IsType<string>(transferred.TryGetRaw(format)))));
            Assert.DoesNotContain(DataFormat.File, transferred.Formats);
            Assert.DoesNotContain(DataFormat.Text, transferred.Formats);
            Assert.Equal(folder ? DragDropEffects.Move : DragDropEffects.Copy | DragDropEffects.Move, allowedEffects);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.MouseUp(point + new Vector(18, 0), MouseButton.Left);
            Flush(window);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Null(context.Session.LastError);
        }
        finally
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImagePointerDragPlacesOriginalAssetOnPreviewOrTimelineWithOneUndo(bool timeline)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = WriteImage(context.Session.ProjectDirectory);
        var item = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Drag image");
        var original = new ProjectDocument { Assets = [asset], AssetLibrary = new() { Items = [item] } };
        context.Session.Editor.Reset(original);
        using var panel = new AssetsPanelView(context.ViewModel.Assets, context.Session);
        var window = new Window { Width = 620, Height = 480, Content = panel };
        try
        {
            window.Show();
            Flush(window);
            var label = panel.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == item.Name);
            var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(point + new Vector(18, 0), RawInputModifiers.LeftMouseButton);
            var destination = DropPoint(context, timeline);
            context.Window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            context.Window.MouseUp(destination, MouseButton.Left);
            await context.Session.WaitForProjectIdleAsync();
            Assert.Null(context.Session.LastError);
            var layer = Assert.Single(context.Session.Editor.Snapshot.Layers);
            Assert.Equal(asset.Id, layer.Image!.AssetId);
            Assert.Single(context.Session.Editor.Snapshot.Assets);
            Assert.Equal(original.AssetLibrary, context.Session.Editor.Snapshot.AssetLibrary);
            Assert.Equal("Place image", context.Session.Editor.UndoLabel);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.UNDO);
            Assert.Equal(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeMarkerAloneOrForeignProjectPayloadCannotPlaceAssets(bool timeline, bool foreignProject)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = WriteImage(context.Session.ProjectDirectory);
        var item = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Drag image");
        var original = new ProjectDocument { Assets = [asset], AssetLibrary = new() { Items = [item] } };
        context.Session.Editor.Reset(original);
        var payload = new AssetDragPayload(foreignProject ? Guid.NewGuid() : original.Id, item.Id, false);
        var transferred = payload.CreateTransferItem();
        if (!foreignProject)
        {
            transferred.Set(AssetDragPayload.Format, (AssetDragPayload?)null);
        }
        using var data = new DataTransfer();
        data.Add(transferred);
        var point = DropPoint(context, timeline);
        context.Window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        context.Window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        context.Window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Null(context.Session.LastError);
    }

    private static Point DropPoint(MainWindowTestContext context, bool timeline)
    {
        Flush(context.Window);
        if (timeline)
        {
            var control = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            var header = control.GetTrackHeaderRectangle(ProjectTrack.DEFAULT_TRACK_ID)!.Value;
            var local = new Point(control.HeaderWidth + control.PixelsPerSecond, header.Center.Y);
            Assert.NotNull(control.GetClipPlacementTarget(local));
            return control.TranslatePoint(local, context.Window)!.Value;
        }
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        var center = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        Assert.NotNull(canvas.ProjectPoint(center));
        return canvas.TranslatePoint(center, context.Window)!.Value;
    }

    private static ProjectAsset WriteImage(string directory)
    {
        Directory.CreateDirectory(directory);
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(SKColors.Coral);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, "drag.png"), encoded.ToArray());
        return new(Guid.NewGuid(), ProjectAssetKind.IMAGE, "drag.png");
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
