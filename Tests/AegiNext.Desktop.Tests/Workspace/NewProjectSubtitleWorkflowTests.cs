using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class NewProjectSubtitleWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewProjectStartsWithOneSavedBlankSubtitleAndReopeningDoesNotDuplicateIt(bool namedDirectory)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var previousId = context.Editor.AddSubtitle(MediaTime.Zero, new(1), "Previous project");
        context.Session.SelectCue(previousId);
        await context.Session.WaitForProjectIdleAsync();

        var result = namedDirectory
            ? await context.Session.CreateProjectAsync(new ProjectCreationRequest("New project", context.DirectoryPath))
            : await context.Session.CreateProjectAsync(Path.Combine(context.DirectoryPath, "New project.aeginext"));
        await context.Session.WaitForProjectIdleAsync();

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Empty(result.Diagnostics);
        var document = context.Editor.Snapshot;
        var subtitle = Assert.Single(document.Subtitles);
        Assert.NotEqual(previousId, subtitle.Id);
        Assert.Equal(string.Empty, subtitle.Text);
        Assert.Equal(MediaTime.Zero, subtitle.Start);
        Assert.Equal(new MediaTime(2), subtitle.End);
        var layer = Assert.Single(document.Layers);
        Assert.Equal(LayerKind.SUBTITLE, layer.Kind);
        Assert.Equal(subtitle.Id, layer.SubtitleId);
        Assert.Equal(Assert.Single(document.Tracks).Id, layer.TrackId);
        Assert.Equal(subtitle.Id, Assert.Single(context.Session.ViewModel.Subtitles.VisibleRows).Id);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
        Assert.False(context.Session.HasUnsavedChanges);
        var saved = await ProjectStore.LoadAsync(context.Session.ProjectPath!);
        Assert.Equal(subtitle.Id, Assert.Single(saved.Subtitles).Id);
        Assert.Equal(layer.Id, Assert.Single(saved.Layers).Id);

        var reopened = await context.Session.OpenProjectAsync(context.Session.ProjectPath!);
        await context.Session.WaitForProjectIdleAsync();

        Assert.Equal(ProjectOpenStatus.OPENED, reopened.Status);
        Assert.Equal(subtitle.Id, Assert.Single(context.Editor.Snapshot.Subtitles).Id);
        Assert.Equal(layer.Id, Assert.Single(context.Editor.Snapshot.Layers).Id);
        Assert.Equal(subtitle.Id, Assert.Single(context.Session.ViewModel.Subtitles.VisibleRows).Id);
        Assert.False(context.Session.HasUnsavedChanges);
    }
}
