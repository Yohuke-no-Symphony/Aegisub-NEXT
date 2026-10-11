using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Images;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class AssetLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private readonly SkiaImageDecoder decoder = new();
    private readonly Dictionary<Guid, (string Path, string? Hash, long Length, DateTime Modified, string? State)> availability = [];

    internal async Task<string?> GetAvailabilityAsync(ProjectAsset asset, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = ProjectAssetLocation.Resolve(asset, session.ProjectDirectory);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return "Workbench.AssetMissing";
            }
            if (asset.Sha256 is null)
            {
                return null;
            }
            if (availability.TryGetValue(asset.Id, out var previous) && previous.Path == path && previous.Hash == asset.Sha256 &&
                previous.Length == info.Length && previous.Modified == info.LastWriteTimeUtc)
            {
                return previous.State;
            }
            var state = await ProjectImageResources.HasChangedAsync(asset, session.ProjectDirectory, cancellationToken)
                ? "Workbench.AssetChanged" : null;
            availability[asset.Id] = (path, asset.Sha256, info.Length, info.LastWriteTimeUtc, state);
            return state;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return "Workbench.AssetMissing";
        }
    }


    internal GraphicClipPlacement CapturePlacement(ScenePoint? position = null, MediaTime? time = null, Guid? trackId = null)
    {
        var cue = time is null ? session.SelectedCue : null;
        var start = time ?? cue?.Start ?? session.ProjectPosition;
        if (start < MediaTime.Zero)
        {
            start = MediaTime.Zero;
        }
        return new(start, cue?.End ?? start + new MediaTime(3), trackId ?? session.CurrentTrackId, position);
    }

    internal async Task PickImportAsync(bool place, Guid? folderId)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        var placement = CapturePlacement();
        var paths = await dialogs.OpenFilesAsync("ImportImages", "Images", ImageFileTypes.Patterns);
        if (paths.Count > 0 && IsCurrent(captured, revision, directory))
        {
            await ImportAsync(paths, place, folderId, capturedPlacement: placement);
        }
    }

    internal async Task ImportAsync(IReadOnlyList<string> paths, bool place, Guid? folderId = null,
        ScenePoint? position = null, bool forceManaged = false, MediaTime? time = null, Guid? trackId = null,
        GraphicClipPlacement? capturedPlacement = null)
    {
        if (paths.Count == 0 || session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        var placement = capturedPlacement ?? CapturePlacement(position, time, trackId);
        var requests = paths.Select(path => new ProjectImageImport(path, forceManaged || session.Preferences.Projects.CopyImportedAssetsToSources)).ToArray();
        Guid? selectedClip = null;
        await session.ApplicationContext.Tasks.Submit(new AssetLibraryTask(session, async context =>
        {
            await using var prepared = await ProjectImageResources.PrepareAsync(requests, directory, captured.Assets, context.CancellationToken);
            var metadata = await ProbeAsync(prepared.Assets, context.CancellationToken);
            var document = captured;
            for (var index = 0; index < prepared.Assets.Length; index++)
            {
                var candidate = prepared.Assets[index];
                var asset = document.Assets.FirstOrDefault(existing => existing.Kind == ProjectAssetKind.IMAGE &&
                    existing.Storage == candidate.Storage && existing.Sha256 == candidate.Sha256 &&
                    existing.RelativePath == candidate.RelativePath && existing.ExternalPath == candidate.ExternalPath) ?? candidate;
                if (!document.Assets.Any(existing => existing.Id == asset.Id))
                {
                    document = document with { Assets = document.Assets.Add(asset) };
                }
                var name = Path.GetFileNameWithoutExtension(paths[index]);
                var item = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, name, folderId);
                document = ProjectEditingOperations.AddAssetLibraryItem(document, item);
                if (place)
                {
                    var result = PlaceImage(document, asset, metadata[index], name, placement);
                    document = result.Document;
                    selectedClip = result.ClipId;
                }
            }
            using var lease = context.AcquireEditLease();
            context.EnterCommit(() => IsCurrent(captured, revision, directory));
            await prepared.CommitAsync(CancellationToken.None);
            session.Editor.Apply(place ? "Import and place images" : "Import images", _ => document);
            prepared.Accept();
        })).Completion;
        if (selectedClip is { } id && session.Editor.Snapshot.Layers.Any(layer => layer.Id == id))
        {
            session.SelectLayer(id, [id]);
        }
    }

    internal async Task PlaceAsync(Guid itemId, ScenePoint? position = null, MediaTime? time = null, Guid? trackId = null)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var item = captured.AssetLibrary.Items.Single(value => value.Id == itemId);
        var asset = captured.Assets.Single(value => value.Id == item.AssetId);
        var placement = CapturePlacement(position, time, trackId);
        var directory = session.ProjectDirectory;
        var revision = session.TaskInputRevision;
        Guid? createdClipId = null;
        await session.ApplicationContext.Tasks.Submit(new AssetLibraryTask(session, async context =>
        {
            var info = await Task.Run(() =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                using var stream = new DirectoryProjectAssetResolver(directory).Open(asset);
                return decoder.Probe(stream);
            }, context.CancellationToken);
            var result = PlaceImage(captured, asset, info, item.Name, placement);
            using var lease = context.AcquireEditLease();
            context.EnterCommit(() => IsCurrent(captured, revision, directory));
            session.Editor.Apply("Place image", _ => result.Document);
            createdClipId = result.ClipId;
        })).Completion;
        if (createdClipId is { } id && session.Editor.Snapshot.Layers.Any(layer => layer.Id == id))
        {
            session.SelectLayer(id, [id]);
        }
    }

    internal async Task ReplaceSelectedAsync()
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts() || session.SelectedLayer is not { Image: not null } layer)
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        var path = await dialogs.OpenFileAsync("ReplaceImage", "Images", ImageFileTypes.Patterns);
        if (path is null || !IsCurrent(captured, revision, directory) || session.SelectedLayer?.Id != layer.Id)
        {
            return;
        }
        var copyToSources = session.Preferences.Projects.CopyImportedAssetsToSources;
        await session.ApplicationContext.Tasks.Submit(new AssetLibraryTask(session, async context =>
        {
            await using var prepared = await ProjectImageResources.PrepareAsync([new(path, copyToSources)],
                directory, context.CancellationToken);
            var info = (await ProbeAsync(prepared.Assets, context.CancellationToken))[0];
            var asset = prepared.Assets[0];
            var document = captured with { Assets = captured.Assets.Add(asset) };
            document = ProjectEditingOperations.AddAssetLibraryItem(document,
                new(Guid.NewGuid(), asset.Id, Path.GetFileNameWithoutExtension(path)));
            document = ProjectEditingOperations.ReplaceImageAsset(document, layer.Id, asset.Id,
                info.IsAnimated ? layer.Image.Playback ?? new() : null);
            using var lease = context.AcquireEditLease();
            context.EnterCommit(() => IsCurrent(captured, revision, directory));
            await prepared.CommitAsync(CancellationToken.None);
            session.Editor.Apply("Replace clip image", _ => document);
            prepared.Accept();
        })).Completion;
    }

    internal async Task RefreshAssetAsync(Guid assetId, bool relocate = false)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        var asset = captured.Assets.Single(value => value.Id == assetId);
        var path = relocate ? await dialogs.OpenFileAsync("RelocateImage", "Images", ImageFileTypes.Patterns)
            : ProjectAssetLocation.Resolve(asset, directory);
        if (path is null || !IsCurrent(captured, revision, directory))
        {
            return;
        }
        await UpdateResourcesAsync(captured, [new(path, asset.Storage == ProjectAssetStorage.MANAGED, asset.Id)], "Refresh image resource");
    }

    internal async Task CollectAsync()
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var requests = captured.Assets.Where(asset => asset.Kind == ProjectAssetKind.IMAGE &&
                (asset.Storage == ProjectAssetStorage.LINKED || !asset.RelativePath.StartsWith("sources/", StringComparison.Ordinal)))
            .Select(asset => new ProjectImageImport(AcceptedPath(asset), true, asset.Id, asset.Sha256,
                Path.GetFileName(asset.ExternalPath ?? asset.RelativePath))).ToArray();
        if (requests.Length > 0)
        {
            await UpdateResourcesAsync(captured, requests, "Collect image resources");
        }
    }

    private string AcceptedPath(ProjectAsset asset)
    {
        if (asset.Sha256 is { } hash && File.Exists(ImageContentSnapshotLocation.Resolve(hash)))
        {
            return ImageContentSnapshotLocation.Resolve(hash);
        }
        return ProjectAssetLocation.Resolve(asset, session.ProjectDirectory);
    }

    private async Task UpdateResourcesAsync(ProjectDocument captured, ProjectImageImport[] requests, string name)
    {
        var revision = session.TaskInputRevision;
        var directory = session.ProjectDirectory;
        await session.ApplicationContext.Tasks.Submit(new AssetLibraryTask(session, async context =>
        {
            await using var prepared = await ProjectImageResources.PrepareAsync(requests, directory, context.CancellationToken);
            await ProbeAsync(prepared.Assets, context.CancellationToken);
            var replacements = prepared.Assets.ToDictionary(asset => asset.Id);
            var document = captured with { Assets = captured.Assets.Select(asset => replacements.GetValueOrDefault(asset.Id, asset)).ToImmutableArray() };
            ProjectValidator.Validate(document);
            using var lease = context.AcquireEditLease();
            context.EnterCommit(() => IsCurrent(captured, revision, directory));
            await prepared.CommitAsync(CancellationToken.None);
            session.Editor.Apply(name, _ => document);
            prepared.Accept();
        })).Completion;
    }

    private Task<ImageMetadata[]> ProbeAsync(ImmutableArray<ProjectAsset> assets, CancellationToken cancellationToken) =>
        Task.Run(() => assets.Select(asset =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(ImageContentSnapshotLocation.Resolve(asset.Sha256!));
            return decoder.Probe(stream);
        }).ToArray(), cancellationToken);

    private bool IsCurrent(ProjectDocument captured, long revision, string directory) => !session.IsClosing &&
        session.ProjectDirectory == directory && session.TaskInputRevision == revision && !session.HasProjectDrafts &&
        ReferenceEquals(captured, session.Editor.Snapshot);

    private static GraphicClipCreationResult PlaceImage(ProjectDocument document, ProjectAsset asset, ImageMetadata info,
        string name, GraphicClipPlacement placement)
    {
        var ratio = Math.Min(1, Math.Min((double)document.Width / info.Width, (double)document.Height / info.Height));
        var width = info.Width * ratio;
        var height = info.Height * ratio;
        var center = placement.Position ?? new ScenePoint(document.Width / 2.0, document.Height / 2.0);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Name = name, Start = placement.Start, End = placement.End,
            Image = new(asset.Id, width, height) { Playback = info.IsAnimated ? new() : null },
            Transform = new(X: center.X - width / 2, Y: center.Y - height / 2)
        };
        return ProjectEditingOperations.CreateGraphicClip(document, layer, placement.TrackId);
    }
}
