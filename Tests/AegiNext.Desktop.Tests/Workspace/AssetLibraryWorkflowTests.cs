using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssetLibraryWorkflowTests
{
    [Fact]
    public async Task ImportIntoFolderDeduplicatesFilesWhileKeepingAliasesAndSingleUndo()
    {
        var folder = new ProjectAssetFolder(Guid.NewGuid(), "Graphics");
        var original = new ProjectDocument { AssetLibrary = new() { Folders = [folder] } };
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        SetProjectDirectory(context);
        var image = WriteImage(context.DirectoryPath, "example.png", SKColors.Red);
        var alias = Path.Combine(context.DirectoryPath, "alias.bmp");
        File.Copy(image, alias);

        await context.Session.Assets.ImportAsync([image, alias], false, folder.Id);

        var document = context.Editor.Snapshot;
        var asset = Assert.Single(document.Assets);
        Assert.Equal(ProjectAssetStorage.MANAGED, asset.Storage);
        Assert.StartsWith("sources/", asset.RelativePath);
        Assert.Equal(2, document.AssetLibrary.Items.Length);
        Assert.All(document.AssetLibrary.Items, item =>
        {
            Assert.Equal(folder.Id, item.FolderId);
            Assert.Equal(asset.Id, item.AssetId);
        });
        Assert.Empty(document.Layers);
        Assert.Single(Directory.GetFiles(Path.Combine(context.DirectoryPath, "sources")));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(asset, context.DirectoryPath)));
    }

    [Fact]
    public async Task ImportPlacesAtSelectedSubtitleTimingAndCreatesAFreeTrack()
    {
        var cue = new SubtitleLine { Text = "Caption", Start = new(2), End = new(7) };
        var subtitleClip = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var original = new ProjectDocument { Subtitles = [cue], Layers = [subtitleClip] };
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        SetProjectDirectory(context);
        context.Session.SelectCue(cue.Id);
        var image = WriteImage(context.DirectoryPath, "position.png", SKColors.Blue);

        await context.Session.Assets.ImportAsync([image], true, position: new(300, 200));

        var placed = context.Session.SelectedLayer!;
        Assert.Equal(LayerKind.IMAGE, placed.Kind);
        Assert.Equal(cue.Start, placed.Start);
        Assert.Equal(cue.End, placed.End);
        Assert.NotEqual(subtitleClip.TrackId, placed.TrackId);
        Assert.Equal(299, placed.Transform.X);
        Assert.Equal(199, placed.Transform.Y);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task RefreshLinkedResourceUpdatesAllInstancesAndUndoRestoresAcceptedBytes()
    {
        await using var context = new WorkspaceSessionTestContext(new() { AssetLibrary = new() { AutoCopyToSources = false } });
        await context.InitializeAsync();
        SetProjectDirectory(context);
        var image = WriteImage(context.DirectoryPath, "linked.png", SKColors.Red);
        await context.Session.Assets.ImportAsync([image], true);
        var item = Assert.Single(context.Editor.Snapshot.AssetLibrary.Items);
        await context.Session.Assets.PlaceAsync(item.Id, time: new(8));
        var original = context.Editor.Snapshot;
        var asset = Assert.Single(original.Assets);
        Assert.Equal(ProjectAssetStorage.LINKED, asset.Storage);
        WriteImage(context.DirectoryPath, "linked.png", SKColors.Blue);
        Assert.Equal("Workbench.AssetChanged", await context.Session.Assets.GetAvailabilityAsync(asset));

        await context.Session.Assets.RefreshAssetAsync(asset.Id);

        var refreshed = Assert.Single(context.Editor.Snapshot.Assets);
        Assert.Equal(asset.Id, refreshed.Id);
        Assert.NotEqual(asset.Sha256, refreshed.Sha256);
        Assert.All(context.Editor.Snapshot.Layers, layer => Assert.Equal(asset.Id, layer.Image!.AssetId));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        using var source = new DirectoryProjectAssetResolver(context.DirectoryPath).Open(asset);
        using var decoded = SKBitmap.Decode(source);
        Assert.Equal(SKColors.Red, decoded.GetPixel(0, 0));
    }

    [Fact]
    public async Task FilePickerCannotImportIntoAChangedDocument()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        SetProjectDirectory(context);
        var image = WriteImage(context.DirectoryPath, "delayed.png", SKColors.Red);
        context.Dialogs.PendingOpenFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = context.Session.Assets.PickImportAsync(false, null);
        await context.Dialogs.OpenFilesShown.Task;
        context.Editor.AddSubtitle(new(0), new(2), "New editing context");
        var updated = context.Editor.Snapshot;
        context.Dialogs.PendingOpenFiles.SetResult([image]);

        await pending;

        Assert.Same(updated, context.Editor.Snapshot);
        Assert.Empty(context.Editor.Snapshot.Assets);
        Assert.False(Directory.Exists(Path.Combine(context.DirectoryPath, "sources")));
    }

    [Fact]
    public async Task UnsupportedImageRollsBackTheWholeImportAndPublishesNoSources()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        SetProjectDirectory(context);
        var valid = WriteImage(context.DirectoryPath, "valid.png", SKColors.Red);
        var invalid = Path.Combine(context.DirectoryPath, "invalid.png");
        await File.WriteAllTextAsync(invalid, "not an image");
        var original = context.Editor.Snapshot;

        await Assert.ThrowsAnyAsync<Exception>(() => context.Session.Assets.ImportAsync([valid, invalid], true));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(Directory.Exists(Path.Combine(context.DirectoryPath, "sources")));
    }

    private static void SetProjectDirectory(WorkspaceSessionTestContext context) =>
        context.Session.SetProjectLocation(Path.Combine(context.DirectoryPath, "project.aeginext"), context.DirectoryPath);

    private static string WriteImage(string directory, string name, SKColor color)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, encoded.ToArray());
        return path;
    }
}
