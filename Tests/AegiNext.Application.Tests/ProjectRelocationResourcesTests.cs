using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectRelocationResourcesTests
{
    [Fact]
    public async Task MissingLaterResourceNeverPublishesEarlierResource()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var source = Path.Combine(original.Path, "image.png");
        await File.WriteAllTextAsync(source, "image data");
        var first = await ProjectResources.ImportAsync(source, ProjectAssetKind.IMAGE, original.Path);
        var missing = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/missing.png");

        await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectResources.RebaseAsync(
            new() { Assets = [first, missing] }, original.Path, destination.Path));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(first, original.Path)));
    }

    [Fact]
    public async Task ProjectSaveFailureRollsBackOnlyNewlyPublishedResources()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var firstPath = Path.Combine(original.Path, "first.png");
        var secondPath = Path.Combine(original.Path, "second.png");
        await File.WriteAllTextAsync(firstPath, "first image");
        await File.WriteAllTextAsync(secondPath, "second image");
        var first = await ProjectResources.ImportAsync(firstPath, ProjectAssetKind.IMAGE, original.Path);
        var second = await ProjectResources.ImportAsync(secondPath, ProjectAssetKind.IMAGE, original.Path);
        await ProjectResources.ImportAsync(firstPath, ProjectAssetKind.IMAGE, destination.Path);
        var projectPath = Path.Combine(destination.Path, "project.aeginext");
        await File.WriteAllTextAsync(projectPath, "previous project");

        await using (var prepared = await ProjectResources.PrepareRebaseAsync(
            new() { Assets = [first, second] }, original.Path, destination.Path))
        {
            Assert.False(File.Exists(ProjectAssetLocation.Resolve(second, destination.Path)));
            await prepared.CommitAsync();
            await Assert.ThrowsAsync<IOException>(() => ProjectStore.SaveAsync(prepared.Snapshot, projectPath,
                () => throw new IOException("Simulated save failure")));
        }

        Assert.True(File.Exists(ProjectAssetLocation.Resolve(first, destination.Path)));
        Assert.False(File.Exists(ProjectAssetLocation.Resolve(second, destination.Path)));
        Assert.Equal("previous project", await File.ReadAllTextAsync(projectPath));
        Assert.Equal(2, Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task AcceptedRelocationPreservesLegacyAndSourcesPathsAndLinkedReferences()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var source = Path.Combine(original.Path, "image.png");
        await File.WriteAllTextAsync(source, "image data");
        var legacy = await ProjectResources.ImportAsync(source, ProjectAssetKind.IMAGE, original.Path);
        var managed = legacy with { Id = Guid.NewGuid(), RelativePath = "sources/copied.png" };
        Directory.CreateDirectory(Path.Combine(original.Path, "sources"));
        File.Copy(source, ProjectAssetLocation.Resolve(managed, original.Path));
        var linked = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing.png", Storage: ProjectAssetStorage.LINKED);
        var document = new ProjectDocument { Assets = [legacy, managed, linked] };

        await using (var prepared = await ProjectResources.PrepareRebaseAsync(document, original.Path, destination.Path))
        {
            Assert.Equal(legacy.RelativePath, prepared.Snapshot.Assets[0].RelativePath);
            Assert.Equal(managed.RelativePath, prepared.Snapshot.Assets[1].RelativePath);
            Assert.Equal(Path.Combine(original.Path, "missing.png"), prepared.Snapshot.Assets[2].ExternalPath);
            await prepared.CommitAsync();
            await ProjectStore.SaveAsync(prepared.Snapshot, Path.Combine(destination.Path, "project.aeginext"));
            prepared.Accept();
        }

        Assert.True(File.Exists(ProjectAssetLocation.Resolve(legacy, destination.Path)));
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(managed, destination.Path)));
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(legacy, original.Path)));
    }

    [Fact]
    public async Task TwoSnapshotLeasesRollbackSharedFilesAfterSaveFailure()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var source = Path.Combine(original.Path, "image.png");
        await File.WriteAllTextAsync(source, "image data");
        var asset = await ProjectResources.ImportAsync(source, ProjectAssetKind.IMAGE, original.Path);
        var document = new ProjectDocument { Assets = [asset] };

        await using (var saved = await ProjectResources.PrepareRebaseAsync(document, original.Path, destination.Path))
        {
            await using var current = await ProjectResources.PrepareRebaseAsync(document with { Name = "Edited" }, original.Path, destination.Path);
            await saved.CommitAsync();
            await current.CommitAsync();
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }
}
