using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ProjectMediaPathWorkflowTests
{
    [Fact]
    public async Task FailedSameDirectorySaveDoesNotInstallNormalizedSnapshotOrChangeHistory()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(directory.Path, "video.mkv");
        await File.WriteAllTextAsync(source, "video");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: source);
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(new()
        {
            Width = 1, Height = 1, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        }, path);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Null(context.Session.LastError);
        context.ChangeDocument(() => context.Session.Editor.AddSubtitle(new(0), new(1), "unsaved"));
        var original = context.Session.Editor.Snapshot;
        var label = context.Session.Editor.UndoLabel;
        var bytes = await File.ReadAllBytesAsync(path);
        var backup = path + ".original";
        File.Move(path, backup);
        Directory.CreateDirectory(path);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.IsAssignableFrom<IOException>(context.Session.LastError);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Equal(label, context.Session.Editor.UndoLabel);
        Assert.True(context.Session.Editor.HasUnsavedChanges);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(backup));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
        context.ChangeDocument(() => Assert.True(context.Session.Editor.Undo()));
        Assert.Empty(context.Session.Editor.Snapshot.Subtitles);
    }

    [Fact]
    public async Task SavingLegacyExternalMediaInSameDirectoryNormalizesPathsAndPreservesUndoRedo()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(directory.Path, "字幕 01.mkv");
        await File.WriteAllTextAsync(source, "video");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: source);
        var document = new ProjectDocument
        {
            Width = 1, Height = 1, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        };
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(document, path);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Null(context.Session.LastError);
        context.ChangeDocument(() => context.Session.Editor.AddSubtitle(new(0), new(1), "Keep this edit"));
        var undoLabel = context.Session.Editor.UndoLabel;
        var probeCount = context.ProbePaths.Count;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        var saved = await ProjectStore.LoadAsync(path);
        Assert.Equal(asset with { RelativePath = "字幕 01.mkv", ExternalPath = null }, Assert.Single(saved.Assets));
        Assert.Equal(Assert.Single(saved.Assets), Assert.Single(context.Session.Editor.Snapshot.Assets));
        Assert.Equal(document.Media, saved.Media);
        Assert.Equal("Keep this edit", Assert.Single(saved.Subtitles).Text);
        Assert.False(context.Session.Editor.HasUnsavedChanges);
        Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(probeCount, context.ProbePaths.Count);
        context.ChangeDocument(() => Assert.True(context.Session.Editor.Undo()));
        Assert.Empty(context.Session.Editor.Snapshot.Subtitles);
        Assert.Equal(source, ProjectAssetLocation.Resolve(context.Session.Editor.Snapshot.Assets[0], directory.Path));
        Assert.True(context.Session.Editor.HasUnsavedChanges);
        context.ChangeDocument(() => Assert.True(context.Session.Editor.Redo()));
        Assert.False(context.Session.Editor.HasUnsavedChanges);

        var canonical = context.Session.Editor.Snapshot;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        Assert.Null(context.Session.LastError);
        Assert.Same(canonical, context.Session.Editor.Snapshot);
        Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
        context.ChangeDocument(() => Assert.True(context.Session.Editor.Undo()));
        Assert.Empty(context.Session.Editor.Snapshot.Subtitles);
    }

    [Theory]
    [InlineData("./media/../media/字幕 01.mkv", false)]
    [InlineData("./media/../media/字幕 01.mkv", true)]
    [InlineData(@".\media\..\media\字幕 01.mkv", false)]
    [InlineData(@".\media\..\media\字幕 01.mkv", true)]
    public async Task RelativeInputUsesProjectDirectoryForPreviewAndBinding(string input, bool updateProject)
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var directory = Path.Combine(context.Session.ProjectDirectory, "media");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "字幕 01.mkv");
        await File.WriteAllTextAsync(source, "video");

        await context.Session.OpenMediaAsync(input, updateProject);

        Assert.Equal(source, Assert.Single(context.ProbePaths));
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.Null(context.Session.Controller.Snapshot.Error);
        if (updateProject)
        {
            var asset = Assert.Single(context.Session.Editor.Snapshot.Assets);
            Assert.Equal("media/字幕 01.mkv", asset.RelativePath);
            Assert.Null(asset.ExternalPath);
        }
        else
        {
            Assert.Empty(context.Session.Editor.Snapshot.Assets);
        }
    }

    [Fact]
    public async Task FirstSaveThenMovingTheWholeProjectReopensTheMovedMedia()
    {
        using var original = new TemporaryWorkbenchDirectory();
        using var moved = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(original.Path, "字幕 01.mkv");
        await File.WriteAllTextAsync(source, "video");
        await context.Session.OpenMediaAsync(source, true);
        var assetId = Assert.Single(context.Session.Editor.Snapshot.Assets).Id;
        context.Dialogs.SavePath = Path.Combine(original.Path, "project.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        var saved = await ProjectStore.LoadAsync(context.Dialogs.SavePath);
        Assert.Equal("字幕 01.mkv", Assert.Single(saved.Assets).RelativePath);
        Assert.Null(saved.Assets[0].ExternalPath);
        var movedProject = Path.Combine(moved.Path, "project.aeginext");
        var movedSource = Path.Combine(moved.Path, "字幕 01.mkv");
        File.Copy(context.Dialogs.SavePath, movedProject);
        File.Move(source, movedSource);
        context.Dialogs.OpenPath = movedProject;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(movedSource, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(movedSource, context.ProbePaths.Last());
        Assert.Equal(assetId, Assert.Single(context.Session.Editor.Snapshot.Assets).Id);
        Assert.Equal(moved.Path, context.Session.ProjectDirectory);
        Assert.False(context.Session.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task SaveAsOutsideMediaDirectoryKeepsTheOriginalSource()
    {
        using var original = new TemporaryWorkbenchDirectory();
        using var destination = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(original.Path, "video.mkv");
        await File.WriteAllTextAsync(source, "video");
        await context.Session.OpenMediaAsync(source, true);
        context.Dialogs.SavePath = Path.Combine(original.Path, "project.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        var assetId = Assert.Single(context.Session.Editor.Snapshot.Assets).Id;
        context.Dialogs.SavePath = Path.Combine(destination.Path, "project.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.Null(context.Session.LastError);
        var saved = await ProjectStore.LoadAsync(context.Dialogs.SavePath);
        var asset = Assert.Single(saved.Assets);
        Assert.Equal(assetId, asset.Id);
        Assert.Equal(string.Empty, asset.RelativePath);
        Assert.Equal(source, asset.ExternalPath);
        Assert.Single(Directory.EnumerateFiles(destination.Path));
        context.Dialogs.OpenPath = context.Dialogs.SavePath;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Null(context.Session.LastError);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
    }

    [Fact]
    public async Task MissingRelativeMediaRestoresThePreviousProjectPreviewAndPosition()
    {
        using var original = new TemporaryWorkbenchDirectory();
        using var missing = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(original.Path, "video.mkv");
        await File.WriteAllTextAsync(source, "video");
        await context.Session.OpenMediaAsync(source, true);
        context.Dialogs.SavePath = Path.Combine(original.Path, "project.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        await context.Session.Controller.SeekAsync(new(1, 10));
        var previousDocument = context.Session.Editor.Snapshot;
        var previousPosition = context.Session.Controller.Snapshot.Position;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        context.Dialogs.OpenPath = Path.Combine(missing.Path, "missing.aeginext");
        await ProjectStore.SaveAsync(new()
        {
            Width = 1, Height = 1, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        }, context.Dialogs.OpenPath);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);
        Assert.Same(previousDocument, context.Session.Editor.Snapshot);
        Assert.Equal(original.Path, context.Session.ProjectDirectory);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(previousPosition, context.Session.Controller.Snapshot.Position);
        Assert.Null(context.Session.Controller.Snapshot.Error);
    }
}
