using AegiNext.Application.Tasks;
using AegiNext.Export;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed class VideoExportTask(WorkbenchSession session, ExportCoordinator coordinator,
    VideoExportRequest request, MediaTime? duration) : AegiTask<VideoExportResult>
{
    public override string Name => "Tasks.VideoExport";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.DeferredStoragePath(request.OutputPath)];

    protected override Task<VideoExportResult> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        coordinator.RunAsync(request, context, duration);
}
