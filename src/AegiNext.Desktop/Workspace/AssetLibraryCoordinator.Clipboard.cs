using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class AssetLibraryCoordinator
{
    internal async Task PasteAsync(IClipboard clipboard, bool place, Guid? folderId = null,
        ScenePoint? position = null, MediaTime? time = null, Guid? trackId = null)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        var placement = CapturePlacement(position, time, trackId);
        using var data = await clipboard.TryGetDataAsync();
        if (data is null)
        {
            return;
        }
        var files = await data.TryGetFilesAsync();
        var paths = files?.OfType<IStorageFile>().Select(file => file.TryGetLocalPath())
            .Where(path => path is not null && ImageFileTypes.SupportsPath(path)).Cast<string>().ToArray() ?? [];
        if (paths.Length > 0)
        {
            if (IsCurrent(captured, revision, directory))
            {
                await ImportAsync(paths, place, folderId, capturedPlacement: placement);
            }
            return;
        }
        using var bitmap = await data.TryGetBitmapAsync();
        if (bitmap is null || !IsCurrent(captured, revision, directory))
        {
            return;
        }
        var temporary = Path.Combine(Path.GetTempPath(), $"aeginext-clipboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, Localization.Get("Workbench.ClipboardImage") + ".png");
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            await ImportAsync([path], place, folderId, forceManaged: true, capturedPlacement: placement);
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }
}
