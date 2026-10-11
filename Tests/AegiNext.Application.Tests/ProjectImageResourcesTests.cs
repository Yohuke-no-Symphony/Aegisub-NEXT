using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectImageResourcesTests
{
    [Fact]
    public async Task ManagedImportPublishesIntoSourcesOnlyWhenCommittedAndRollsBackUnlessAccepted()
    {
        using var source = new TemporaryProjectDirectory();
        using var target = new TemporaryProjectDirectory();
        var path = Path.Combine(source.Path, "picture.png");
        await File.WriteAllTextAsync(path, "image fixture");
        await using (var prepared = await ProjectImageResources.PrepareAsync([new(path, true)], target.Path))
        {
            var asset = Assert.Single(prepared.Assets);
            Assert.StartsWith("sources/", asset.RelativePath, StringComparison.Ordinal);
            Assert.Equal(ProjectAssetStorage.MANAGED, asset.Storage);
            Assert.Empty(Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories));
            await prepared.CommitAsync();
            Assert.True(File.Exists(ProjectAssetLocation.Resolve(asset, target.Path)));
        }
        Assert.Empty(Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LinkedImportKeepsOriginalPathAndAcceptedSnapshotSurvivesExternalChange()
    {
        using var source = new TemporaryProjectDirectory();
        using var target = new TemporaryProjectDirectory();
        var path = Path.Combine(source.Path, "linked.png");
        await File.WriteAllTextAsync(path, "first version");
        await using var prepared = await ProjectImageResources.PrepareAsync([new(path, false)], target.Path);
        var asset = Assert.Single(prepared.Assets);
        Assert.Equal(ProjectAssetStorage.LINKED, asset.Storage);
        Assert.Equal(path, asset.ExternalPath);
        await prepared.CommitAsync();
        prepared.Accept();
        await File.WriteAllTextAsync(path, "second version");
        Assert.Equal("first version", await File.ReadAllTextAsync(ImageContentSnapshotLocation.Resolve(asset.Sha256!)));
        Assert.Empty(Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CollectPreservesIdentityAndDoesNotDeleteOriginal()
    {
        using var source = new TemporaryProjectDirectory();
        using var target = new TemporaryProjectDirectory();
        var path = Path.Combine(source.Path, "linked.png");
        await File.WriteAllTextAsync(path, "collect fixture");
        var id = Guid.NewGuid();
        await using var prepared = await ProjectImageResources.PrepareAsync([new(path, true, id)], target.Path);
        await prepared.CommitAsync();
        prepared.Accept();
        Assert.Equal(id, Assert.Single(prepared.Assets).Id);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(prepared.Assets[0], target.Path)));
    }

    [Fact]
    public async Task RebasePreservesLinkedImageWithoutCopyingAndManagedLegacyPath()
    {
        using var source = new TemporaryProjectDirectory();
        using var target = new TemporaryProjectDirectory();
        var path = Path.Combine(source.Path, "linked.png");
        await File.WriteAllTextAsync(path, "link");
        var legacy = await ProjectResources.ImportAsync(path, ProjectAssetKind.IMAGE, source.Path);
        var linked = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "linked.png", Storage: ProjectAssetStorage.LINKED);
        var result = await ProjectResources.RebaseAsync(new() { Assets = [legacy, linked] }, source.Path, target.Path);
        Assert.Equal(legacy.RelativePath, result.Assets[0].RelativePath);
        Assert.Equal(path, result.Assets[1].ExternalPath);
        Assert.Equal(ProjectAssetStorage.LINKED, result.Assets[1].Storage);
        Assert.Single(Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancelledBatchDoesNotPublishOrTouchExistingFiles()
    {
        using var source = new TemporaryProjectDirectory();
        using var target = new TemporaryProjectDirectory();
        var path = Path.Combine(source.Path, "image.png");
        await File.WriteAllTextAsync(path, "cancel fixture");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProjectImageResources.PrepareAsync([new(path, true)], target.Path, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target.Path));
    }
}
