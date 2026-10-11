using AegiNext.Application;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal AssetFolderEditTarget? CaptureAssetFolderTarget(Guid folderId)
    {
        var document = editor.Snapshot;
        var folder = document.AssetLibrary.Folders.FirstOrDefault(folder => folder.Id == folderId);
        return folder is null ? null : new(document, projectGeneration, folder.Id, folder.Name);
    }

    internal Task RenameAssetFolderAsync(AssetFolderEditTarget target)
    {
        return RunCommandAsync(async () =>
        {
            if (!IsCurrentAssetFolderTarget(target) || IsProjectBusy)
            {
                return;
            }
            string? name;
            using (AcquireEditingLease())
            {
                name = await dialogs.RenameAssetFolderAsync(target.Name, ProjectOperationsToken);
            }
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == target.Name || !IsCurrentAssetFolderTarget(target))
            {
                return;
            }
            await EditAsync(() =>
            {
                if (IsCurrentAssetFolderTarget(target))
                {
                    editor.Apply("Rename asset folder", document =>
                        ProjectEditingOperations.RenameAssetFolder(document, target.FolderId, name.Trim()));
                }
            });
        });
    }

    internal Task RemoveAssetFolderAsync(AssetFolderEditTarget target)
    {
        return RunCommandAsync(async () =>
        {
            if (!IsCurrentAssetFolderTarget(target) || IsProjectBusy)
            {
                return;
            }
            bool accepted;
            using (AcquireEditingLease())
            {
                accepted = await dialogs.ConfirmAssetFolderDeletionAsync(target.Name, ProjectOperationsToken);
            }
            if (!accepted || !IsCurrentAssetFolderTarget(target))
            {
                return;
            }
            await EditAsync(() =>
            {
                if (IsCurrentAssetFolderTarget(target))
                {
                    editor.Apply("Remove asset folder", document =>
                        ProjectEditingOperations.RemoveAssetFolder(document, target.FolderId));
                }
            });
        });
    }

    private bool IsCurrentAssetFolderTarget(AssetFolderEditTarget target) => !closing &&
        target.Generation == projectGeneration && ReferenceEquals(target.Document, editor.Snapshot) &&
        editor.Snapshot.AssetLibrary.Folders.Any(folder => folder.Id == target.FolderId);
}
