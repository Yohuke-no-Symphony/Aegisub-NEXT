using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssetItemActionTests
{
    [Fact]
    public async Task RenameChangesOnlyFrozenItemAndUndoesAsOneEdit()
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var item = original.AssetLibrary.Items[0];
        var target = context.Session.CaptureAssetItemTarget(item.Id)!;
        context.Dialogs.AssetRenameChoice = "  New asset name  ";

        await context.Session.RenameAssetItemAsync(target);

        Assert.Equal(item.Name, context.Dialogs.RenamedAssetName);
        Assert.Equal("New asset name", context.Editor.Snapshot.AssetLibrary.Items[0].Name);
        Assert.Equal(original.AssetLibrary.Items[1], context.Editor.Snapshot.AssetLibrary.Items[1]);
        Assert.Equal(original.Assets.ToArray(), context.Editor.Snapshot.Assets.ToArray());
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" First ")]
    [InlineData("Bad\nname")]
    public async Task CancelOrInvalidRenameDoesNotCreateAnEdit(string? name)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        context.Dialogs.AssetRenameChoice = name;

        await context.Session.RenameAssetItemAsync(context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[0].Id)!);

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Null(context.Session.LastError);
    }

    [Fact]
    public async Task RenameDialogCannotChangeAReplacedDocument()
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        context.Dialogs.PendingAssetRename = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[0].Id)!;
        var pending = context.Session.RenameAssetItemAsync(target);
        Assert.Equal(1, context.Dialogs.AssetRenameRequests);
        context.Editor.AddSubtitle(new(0), new(2), "Changed document");
        var changed = context.Editor.Snapshot;

        context.Dialogs.PendingAssetRename.SetResult("Unaccepted name");
        await pending;

        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Equal("First", context.Editor.Snapshot.AssetLibrary.Items[0].Name);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task StaleContextActionsDoNotOpenDialogsOrAccessResources()
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var target = context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[0].Id)!;
        context.Editor.AddSubtitle(new(0), new(2), "Changed document");
        var changed = context.Editor.Snapshot;

        await context.Session.RenameAssetItemAsync(target);
        await context.Session.RemoveAssetItemAsync(target);
        await context.Session.PlaceAssetItemAsync(target);
        await context.Session.RefreshAssetItemAsync(target);
        await context.Session.RefreshAssetItemAsync(target, true);

        Assert.False(context.Session.CanRemoveAssetItem(target));
        Assert.Equal(0, context.Dialogs.AssetRenameRequests);
        Assert.Equal(0, context.Dialogs.OpenFileRequests);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Null(context.Session.LastError);
    }

    [Fact]
    public async Task RemovePreservesSharedAssetAndUndoesAsOneEdit()
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var target = context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[0].Id)!;
        Assert.True(context.Session.CanRemoveAssetItem(target));

        await context.Session.RemoveAssetItemAsync(target);

        Assert.Equal(original.AssetLibrary.Items[1], Assert.Single(context.Editor.Snapshot.AssetLibrary.Items));
        Assert.Equal(original.Assets.ToArray(), context.Editor.Snapshot.Assets.ToArray());
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task UsedAssetCannotBeRemovedEvenThroughAnotherAlias()
    {
        var document = CreateDocument();
        var original = document with
        {
            Layers = [new() { Kind = LayerKind.IMAGE, Image = new(document.Assets[0].Id, 10, 10), Start = new(0), End = new(2) }]
        };
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var target = context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[1].Id)!;
        Assert.False(context.Session.CanRemoveAssetItem(target));

        await context.Session.RemoveAssetItemAsync(target);

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Null(context.Session.LastError);
    }

    [Fact]
    public async Task RelocationPickerCannotRefreshAChangedDocument()
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        context.Dialogs.PendingOpenFile = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = context.Session.CaptureAssetItemTarget(original.AssetLibrary.Items[0].Id)!;
        var pending = context.Session.RefreshAssetItemAsync(target, true);
        Assert.Equal(1, context.Dialogs.OpenFileRequests);
        context.Editor.AddSubtitle(new(0), new(2), "Changed document");
        var changed = context.Editor.Snapshot;

        context.Dialogs.PendingOpenFile.SetResult(Path.Combine(context.DirectoryPath, "not-needed.png"));
        await pending;

        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Null(context.Session.LastError);
    }

    private static ProjectDocument CreateDocument()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/shared.png");
        return new()
        {
            Assets = [asset],
            AssetLibrary = new()
            {
                Items = [new(Guid.NewGuid(), asset.Id, "First"), new(Guid.NewGuid(), asset.Id, "Second")]
            }
        };
    }
}
