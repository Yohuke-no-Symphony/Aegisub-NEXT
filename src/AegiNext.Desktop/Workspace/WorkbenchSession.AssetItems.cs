using AegiNext.Application;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal AssetItemEditTarget? CaptureAssetItemTarget(Guid itemId)
    {
        var document = editor.Snapshot;
        var item = document.AssetLibrary.Items.FirstOrDefault(item => item.Id == itemId);
        return item is null ? null : new(document, projectGeneration, item.Id, item.AssetId, item.Name);
    }

    internal bool CanRemoveAssetItem(AssetItemEditTarget target) => IsCurrentAssetItemTarget(target) &&
        !editor.Snapshot.Layers.Any(layer => layer.Image?.AssetId == target.AssetId);

    internal Task RenameAssetItemAsync(AssetItemEditTarget target)
    {
        return RunCommandAsync(async () =>
        {
            if (!IsCurrentAssetItemTarget(target) || IsProjectBusy)
            {
                return;
            }
            string? name;
            using (AcquireEditingLease())
            {
                name = await dialogs.RenameAssetItemAsync(target.Name, ProjectOperationsToken);
            }
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name.Trim() == target.Name ||
                !IsCurrentAssetItemTarget(target))
            {
                return;
            }
            await EditAsync(() =>
            {
                if (IsCurrentAssetItemTarget(target))
                {
                    editor.Apply("Rename asset", document =>
                        ProjectEditingOperations.RenameAssetLibraryItem(document, target.ItemId, name.Trim()));
                }
            });
        });
    }

    internal Task RemoveAssetItemAsync(AssetItemEditTarget target)
    {
        return RunCommandAsync(() => CanRemoveAssetItem(target) ? EditAsync(() =>
        {
            if (CanRemoveAssetItem(target))
            {
                editor.Apply("Remove unused asset", document =>
                    ProjectEditingOperations.RemoveUnusedAssetLibraryItems(document, [target.ItemId]));
            }
        }) : Task.CompletedTask);
    }

    internal Task PlaceAssetItemAsync(AssetItemEditTarget target)
    {
        return RunCommandAsync(() => PrepareAssetItemAction(target)
            ? Assets.PlaceAsync(target.ItemId) : Task.CompletedTask);
    }

    internal Task RefreshAssetItemAsync(AssetItemEditTarget target, bool relocate = false)
    {
        return RunCommandAsync(() => PrepareAssetItemAction(target)
            ? Assets.RefreshAssetAsync(target.AssetId, relocate) : Task.CompletedTask);
    }

    internal bool IsCurrentAssetItemTarget(AssetItemEditTarget target) => !closing &&
        target.Generation == projectGeneration && ReferenceEquals(target.Document, editor.Snapshot) &&
        editor.Snapshot.AssetLibrary.Items.Any(item => item.Id == target.ItemId && item.AssetId == target.AssetId);

    private bool PrepareAssetItemAction(AssetItemEditTarget target) => IsCurrentAssetItemTarget(target) &&
        !IsProjectBusy && TryCommitDrafts() && IsCurrentAssetItemTarget(target);
}
