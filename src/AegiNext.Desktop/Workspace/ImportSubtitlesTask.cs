using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ImportSubtitlesTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string path, SubtitleExchangeFormat format, ProjectDocument captured, long inputRevision, Guid? trackId, Guid? presetId, Task fontLoading)
    : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ImportSubtitles";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [.. base.Resources, Session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE),
        AegiTaskResource.DeferredStoragePath(Path.Combine(Session.PreferencesStore.DirectoryPath, "preferences.json"))];
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        await coordinator.ImportSubtitlesCoreAsync(path, format, captured, inputRevision, trackId, presetId, fontLoading, context);
        return true;
    }
}
