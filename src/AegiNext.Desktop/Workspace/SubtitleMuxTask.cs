using AegiNext.Application.Tasks;
using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal sealed class SubtitleMuxTask(WorkbenchSession session, ExportCoordinator coordinator,
    SubtitleMuxRequest request) : AegiTask<SubtitleMuxResult>
{
    public override string Name => "Tasks.SubtitleMuxExport";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.DeferredStoragePath(request.OutputPath)];

    protected override Task<SubtitleMuxResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return coordinator.RunSubtitleMuxAsync(request, context);
    }
}
