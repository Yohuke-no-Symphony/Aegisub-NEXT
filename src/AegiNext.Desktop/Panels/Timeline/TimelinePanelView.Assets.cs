using AegiNext.Desktop.Panels.Assets;
using AegiNext.Desktop.Workspace;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView
{
    private void InitializeAssetDrop()
    {
        DragDrop.SetAllowDrop(timeline, true);
        timeline.AddHandler(DragDrop.DragOverEvent, OnAssetDragOver);
        timeline.AddHandler(DragDrop.DropEvent, OnAssetDrop);
    }

    private void OnAssetDragOver(object? sender, DragEventArgs e)
    {
        var payload = e.DataTransfer.TryGetValue(AssetDragPayload.Format);
        var supported = payload is { IsFolder: false } && payload.ProjectId == session.Editor.Snapshot.Id ||
            AssetDropFiles(e).Length > 0;
        e.DragEffects = supported && !session.IsProjectBusy && timeline.GetClipPlacementTarget(e.GetPosition(timeline), e.KeyModifiers) is not null
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAssetDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (timeline.GetClipPlacementTarget(e.GetPosition(timeline), e.KeyModifiers) is not { } target)
        {
            return;
        }
        var payload = e.DataTransfer.TryGetValue(AssetDragPayload.Format);
        if (payload is { IsFolder: false } && payload.ProjectId == session.Editor.Snapshot.Id)
        {
            await session.RunCommandAsync(() => session.Assets.PlaceAsync(payload.Id, time: target.Time, trackId: target.TrackId));
        }
        else
        {
            var paths = AssetDropFiles(e);
            if (paths.Length > 0)
            {
                await session.RunCommandAsync(() => session.Assets.ImportAsync(paths, true, time: target.Time, trackId: target.TrackId));
            }
        }
    }

    private static string[] AssetDropFiles(DragEventArgs e) => e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
        .Select(file => file.TryGetLocalPath()).Where(path => path is not null && ImageFileTypes.SupportsPath(path)).Cast<string>().ToArray() ?? [];
}
