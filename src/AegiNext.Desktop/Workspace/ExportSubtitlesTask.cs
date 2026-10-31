using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportSubtitlesTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string path, ProjectDocument document, SubtitleExchangeFormat format) : AegiTask
{
    public override string Name => "Tasks.ExportSubtitles";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.DeferredStoragePath(path)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) =>
        coordinator.ExportSubtitlesCoreAsync(path, document, format, context);
}
