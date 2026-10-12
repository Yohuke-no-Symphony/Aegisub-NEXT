using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ProjectStoreTests
{
    [Fact]
    public async Task RejectedCommitKeepsOriginalFileAndRemovesPreparedTemporaryFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Original" }, path);
        var before = await File.ReadAllBytesAsync(path);
        var prepared = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectStore.SaveAsync(new() { Name = "Prepared" },
            path, () =>
            {
                prepared = Directory.EnumerateFiles(directory.Path, "*.tmp").Any();
                throw new OperationCanceledException("Commit rejected");
            }));
        Assert.True(prepared);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CreateWritesANewFileButCannotOverwriteAnExistingProject()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.CreateAsync(new() { Name = "Original" }, path);
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() => ProjectStore.CreateAsync(new() { Name = "Replacement" }, path));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task RebaseRejectsModifiedManagedAssetWithoutChangingSavedProjectOrExistingDestination()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var source = Path.Combine(original.Path, "source.png");
        await File.WriteAllTextAsync(source, "original pixels");
        var asset = await ProjectResources.ImportAsync(source, ProjectAssetKind.IMAGE, original.Path);
        var document = new ProjectDocument { Assets = [asset] };
        var projectPath = Path.Combine(original.Path, "original.aeginext");
        await ProjectStore.SaveAsync(document, projectPath);
        var before = await File.ReadAllBytesAsync(projectPath);
        var destinationPath = Path.Combine(destination.Path, "existing.aeginext");
        await File.WriteAllTextAsync(destinationPath, "keep existing project");
        await File.WriteAllTextAsync(ProjectAssetLocation.Resolve(asset, original.Path), "modified pixels");

        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectResources.RebaseAsync(document, original.Path, destination.Path));

        Assert.Equal(before, await File.ReadAllBytesAsync(projectPath));
        Assert.Equal("keep existing project", await File.ReadAllTextAsync(destinationPath));
        Assert.Equal(asset, Assert.Single(document.Assets));
    }

    [Fact]
    public async Task ProjectRoundTripPreservesRationalTimesLayersStylesAndStableIds()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(1001, 30000), new(2002, 30000), "字幕 😀\nمرحبا");
        editor.UpdateSubtitle(id, line => line with { Style = line.Style with { Fill = new(4, -0.1, 2, 0.5), FontFamily = "Noto Sans" } });
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.25));
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/media/source.mkv");
        editor.Apply("Media", document => document with { Assets = [media], Media = new(media.Id, 1, 0, new(-1001, 30000)) });
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(editor.Snapshot, path);
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Equal(ProjectStore.Serialize(editor.Snapshot), ProjectStore.Serialize(loaded));
        Assert.Equal(new MediaTime(1001, 30000), Assert.Single(loaded.Subtitles).Start);
        Assert.Equal(id, Assert.Single(loaded.Layers).Id);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void LegacyCollapsedAnimationRowsRemainReadableButAreNotWrittenOrExpanded()
    {
        var editor = new ProjectEditor();
        var subtitleId = editor.AddSubtitle(new(0), new(4), "Collapsed animation");
        editor.SetKeyframe(subtitleId, AnimationProperty.OPACITY, new(new(0), 0.25));
        var row = new TimelineAnimationRowId(TimelineRowScope.TRACK, editor.Snapshot.Tracks[0].Id, AnimationProperty.OPACITY);
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["timelineViewState"]!.AsObject().Remove("expandedAnimationClips");
        LegacyProjectJsonFixture.AddAnimationRows(root, [row]);

        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.Equal(row, Assert.Single(loaded.TimelineViewState.CollapsedAnimationRows));
        Assert.Empty(loaded.TimelineViewState.ExpandedAnimationClips);
        var saved = JsonNode.Parse(ProjectStore.Serialize(loaded))!.AsObject();
        Assert.False(saved["timelineViewState"]!.AsObject().ContainsKey("collapsedAnimationRows"));
        var reopened = ProjectStore.Deserialize(ProjectStore.Serialize(loaded));
        Assert.Empty(reopened.TimelineViewState.CollapsedAnimationRows);
        Assert.Empty(reopened.TimelineViewState.ExpandedAnimationClips);
    }

    [Fact]
    public async Task InvalidOrCancelledSaveLeavesExistingBytesAndNoTemporaryFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var original = new ProjectDocument { Name = "original" };
        await ProjectStore.SaveAsync(original, path);
        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.SaveAsync(original with { Width = 0 }, path));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectStore.SaveAsync(original with { Name = "changed" }, path, cancellation.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("\"version\": {version}", "\"version\": 99")]
    [InlineData("\"version\": {version},", "")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"version\": {version},")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"unknown\": 0,")]
    [InlineData("\"width\": 1920", "\"width\": 0")]
    public void UnsupportedMissingDuplicateAndUnknownDataIsRejected(string find, string replacement)
    {
        var json = Encoding.UTF8.GetString(ProjectStore.Serialize(new()));
        var currentVersion = ProjectDocument.CURRENT_VERSION.ToString(CultureInfo.InvariantCulture);
        find = find.Replace("{version}", currentVersion, StringComparison.Ordinal);
        replacement = replacement.Replace("{version}", currentVersion, StringComparison.Ordinal);
        var modified = json.Replace(find, replacement, StringComparison.Ordinal);
        Assert.NotEqual(json, modified);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(modified)));
    }

    [Fact]
    public void ZeroTimeDenominatorCannotBecomeDefaultZeroSilently()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(1, 3), new(2, 3), "time");
        var json = Encoding.UTF8.GetString(ProjectStore.Serialize(editor.Snapshot)).Replace("\"denominator\": 3", "\"denominator\": 0", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task MediaImportReferencesOriginalWithoutCopyAndRebaseCopiesOnlySmallAssets()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var mediaPath = Path.Combine(original.Path, "source.mkv");
        var imagePath = Path.Combine(original.Path, "image.png");
        await File.WriteAllTextAsync(mediaPath, "media fixture");
        await File.WriteAllTextAsync(imagePath, "image fixture");
        var media = await ProjectResources.ImportAsync(mediaPath, ProjectAssetKind.MEDIA, original.Path);
        Assert.Equal("source.mkv", media.RelativePath);
        Assert.Null(media.ExternalPath);
        Assert.False(Directory.Exists(Path.Combine(original.Path, "assets")));
        var image = await ProjectResources.ImportAsync(imagePath, ProjectAssetKind.IMAGE, original.Path);
        var document = new ProjectDocument { Assets = [media, image], Media = new(media.Id, 0, null, new(0)) };
        var moved = await ProjectResources.RebaseAsync(document, original.Path, destination.Path);
        Assert.Equal(media with { RelativePath = string.Empty, ExternalPath = mediaPath }, moved.Assets[0]);
        Assert.Equal(image.Id, moved.Assets[1].Id);
        Assert.Equal(image.Sha256, moved.Assets[1].Sha256);
        Assert.Equal("image fixture", await File.ReadAllTextAsync(ProjectAssetLocation.Resolve(moved.Assets[1], destination.Path)));
        Assert.Single(Directory.GetFiles(destination.Path, "*", SearchOption.AllDirectories));
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(image, original.Path)));
    }

    [Fact]
    public async Task RebaseConvertsRelativeMediaToExternalReferenceWithoutCopyingIt()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var path = Path.Combine(original.Path, "video.mkv");
        await File.WriteAllTextAsync(path, "video");
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "video.mkv");
        var moved = await ProjectResources.RebaseAsync(new() { Assets = [media] }, original.Path, destination.Path);
        Assert.Equal(path, Assert.Single(moved.Assets).ExternalPath);
        Assert.Equal(media.Id, Assert.Single(moved.Assets).Id);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }
}
