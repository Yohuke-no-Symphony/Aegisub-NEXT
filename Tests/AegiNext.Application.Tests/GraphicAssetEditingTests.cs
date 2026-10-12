using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class GraphicAssetEditingTests
{
    [Fact]
    public void MovingFolderIntoItsDescendantRejectsTheEntireHistoryTransaction()
    {
        var parent = new ProjectAssetFolder(Guid.NewGuid(), "Parent");
        var child = new ProjectAssetFolder(Guid.NewGuid(), "Child", parent.Id);
        var original = new ProjectDocument { AssetLibrary = new() { Folders = [parent, child] } };
        var editor = new ProjectEditor(original);
        Assert.Throws<InvalidDataException>(() => editor.Apply("Move folder", document =>
            ProjectEditingOperations.MoveAssetFolder(document, parent.Id, child.Id)));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RemovingFolderPromotesChildrenAndUndoRestoresHierarchy()
    {
        var parent = new ProjectAssetFolder(Guid.NewGuid(), "Parent");
        var child = new ProjectAssetFolder(Guid.NewGuid(), "Child", parent.Id);
        var image = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.png");
        var item = new ProjectAssetLibraryItem(Guid.NewGuid(), image.Id, "Image", parent.Id);
        var original = new ProjectDocument { Assets = [image], AssetLibrary = new() { Folders = [parent, child], Items = [item] } };
        var editor = new ProjectEditor(original);
        editor.Apply("Remove folder", document => ProjectEditingOperations.RemoveAssetFolder(document, parent.Id));
        Assert.Null(Assert.Single(editor.Snapshot.AssetLibrary.Folders).ParentId);
        Assert.Null(Assert.Single(editor.Snapshot.AssetLibrary.Items).FolderId);
        editor.Undo();
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void GraphicCreationUsesAnAdjacentTrackOnlyWhenThePreferredTrackIsOccupied()
    {
        var occupied = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), Start = new(1), End = new(4) };
        var document = new ProjectDocument { Layers = [occupied] };
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 30, 30), Start = new(2), End = new(3) };
        var result = ProjectEditingOperations.CreateGraphicClip(document, shape, occupied.TrackId);
        Assert.Equal(result.TrackId, result.Document.Tracks[0].Id);
        Assert.Equal(occupied.TrackId, result.Document.Tracks[1].Id);
        Assert.Equal(result.TrackId, result.CreatedTrackId);
        var clear = ProjectEditingOperations.CreateGraphicClip(document, shape with { Start = new(4), End = new(5) }, occupied.TrackId);
        Assert.Null(clear.CreatedTrackId);
        Assert.Single(clear.Document.Tracks);
    }

    [Fact]
    public void ReplacementAffectsOnlyOneClipAndPreservesItsCropTransformAndTiming()
    {
        var original = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/old.png");
        var replacement = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/new.png");
        var first = new ProjectLayer { Kind = LayerKind.IMAGE, Start = new(1), End = new(2), Image = new(original.Id, 100, 50) { Crop = new(0.1, 0.2, 0.5, 0.5) }, Transform = new(X: 123) };
        var second = first with { Id = Guid.NewGuid(), Start = new(3), End = new(4) };
        var document = new ProjectDocument { Assets = [original, replacement], Layers = [first, second] };
        var result = ProjectEditingOperations.ReplaceImageAsset(document, first.Id, replacement.Id);
        Assert.Equal(first with { Image = first.Image! with { AssetId = replacement.Id } }, result.Layers[0]);
        Assert.Same(second, result.Layers[1]);
    }

    [Fact]
    public void RemovingUnusedAliasRetainsSharedAssetAndRemovingFinalAliasReleasesItsRecord()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/shared.png");
        var first = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "First");
        var second = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Second");
        var document = new ProjectDocument { Assets = [asset], AssetLibrary = new() { Items = [first, second] } };
        var reduced = ProjectEditingOperations.RemoveUnusedAssetLibraryItems(document, [first.Id]);
        Assert.Equal(asset, Assert.Single(reduced.Assets));
        var removed = ProjectEditingOperations.RemoveUnusedAssetLibraryItems(reduced, [second.Id]);
        Assert.Empty(removed.Assets);
        var used = document with { Layers = [new() { Kind = LayerKind.IMAGE, Image = new(asset.Id, 10, 10) }] };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.RemoveUnusedAssetLibraryItems(used, [first.Id]));
    }

    [Fact]
    public void ShapeConversionPreservesWorldTransformAndRejectsInvalidGeometryAtomically()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ROUNDED_RECTANGLE, 120, 40) { CornerRadius = 12 },
            Transform = new(20, 30, 2, 3, 45), Fill = new(1, 0, 0)
        };
        var original = new ProjectDocument { Layers = [layer] };
        var converted = ProjectEditingOperations.ConvertShapeToPath(original, layer.Id);
        Assert.Equal(layer.Transform, converted.Layers[0].Transform);
        Assert.Equal(layer.Fill, converted.Layers[0].Fill);
        var expected = ShapeGeometry.CreatePath(layer.Shape);
        var actual = converted.Layers[0].Shape!.Path!;
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.Closed, actual.Closed);
        Assert.Equal(expected.Segments.ToArray(), actual.Segments.ToArray());
        var editor = new ProjectEditor(converted);
        Assert.Throws<InvalidDataException>(() => editor.Apply("Bad geometry", document =>
            ProjectEditingOperations.UpdateShapeGeometry(document, layer.Id, new(ShapeKind.PATH, 100, 100, new(new(0, 0), [])))));
        Assert.Same(converted, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void MatchingTimingKeepsImageSpeedAndSourceOffsetAndIsUndoable()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/animated.gif");
        var imageTrack = new ProjectTrack { Name = "Image" };
        var image = new ProjectLayer
        {
            TrackId = imageTrack.Id, Kind = LayerKind.IMAGE, Start = new(1), End = new(2),
            Image = new(asset.Id, 10, 10) { Playback = new() { Speed = 2, SourceOffset = new(3) } }
        };
        var reference = new ProjectLayer { Kind = LayerKind.SHAPE, Start = new(4), End = new(8), Shape = new(ShapeKind.RECTANGLE, 20, 20) };
        var original = new ProjectDocument { Assets = [asset], Tracks = [ProjectTrack.Default, imageTrack], Layers = [reference, image] };
        var editor = new ProjectEditor(original);
        editor.Apply("Match timing", document => ProjectEditingOperations.MatchGraphicClipTiming(document, image.Id, reference.Id));
        var matched = editor.Snapshot.Layers[1];
        Assert.Equal((reference.Start, reference.End), (matched.Start, matched.End));
        Assert.Equal(image.Image, matched.Image);
        editor.Undo();
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void VersionThirteenImagesRemainManagedAndBecomeRootLibraryItems()
    {
        var image = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/legacy.png");
        var json = JsonNode.Parse(ProjectStore.Serialize(new() { Assets = [image] }))!.AsObject();
        json["version"] = 13;
        LegacySubtitleScalingJsonFixture.DowngradeProject(json);
        json.Remove("assetLibrary");
        json["assets"]![0]!.AsObject().Remove("storage");
        var document = ProjectStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
        Assert.True(document.AssetLibrary.AutoCopyToSources);
        Assert.Equal(image, Assert.Single(document.Assets));
        var item = Assert.Single(document.AssetLibrary.Items);
        Assert.Equal(image.Id, item.AssetId);
        Assert.Null(item.FolderId);
    }

    [Fact]
    public void MergeIncludesUnusedLibraryImagesAndRemapsTheirFoldersAndItems()
    {
        var folder = new ProjectAssetFolder(Guid.NewGuid(), "Unused");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/unused.png");
        var item = new ProjectAssetLibraryItem(Guid.NewGuid(), asset.Id, "Unused picture", folder.Id);
        var source = new ProjectDocument { Assets = [asset], AssetLibrary = new() { Folders = [folder], Items = [item] } };
        Assert.Contains(asset.Id, ProjectEditingOperations.GetMergeAssetIds(source));
        var merged = ProjectEditingOperations.MergeProjects(new(), [new(source, "Source", "/source")]).Document;
        var imported = Assert.Single(merged.AssetLibrary.Items);
        Assert.NotEqual(item.Id, imported.Id);
        Assert.NotEqual(asset.Id, imported.AssetId);
        Assert.Contains(merged.AssetLibrary.Folders, value => value.Id == imported.FolderId && value.Name == "Unused");
    }

    [Fact]
    public void ProjectRoundTripPreservesLibraryLinksCropAndAnimationParameters()
    {
        var folder = new ProjectAssetFolder(Guid.NewGuid(), "References");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, string.Empty,
            ExternalPath: "/external/animated.webp", Storage: ProjectAssetStorage.LINKED);
        var document = new ProjectDocument
        {
            Assets = [asset], AssetLibrary = new() { AutoCopyToSources = false, Folders = [folder], Items = [new(Guid.NewGuid(), asset.Id, "Reference", folder.Id)] },
            Layers = [new() { Kind = LayerKind.IMAGE, Image = new(asset.Id, 100, 200)
            {
                Crop = new(0.1, 0.2, 0.5, 0.5), Playback = new() { SourceOffset = new(-2, 3), Speed = 1.25, Mode = ImagePlaybackMode.FILE }
            } }]
        };
        var serialized = ProjectStore.Serialize(document);
        Assert.Equal(serialized, ProjectStore.Serialize(ProjectStore.Deserialize(serialized)));
    }
}
