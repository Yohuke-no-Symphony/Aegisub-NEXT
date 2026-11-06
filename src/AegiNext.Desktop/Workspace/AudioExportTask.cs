using AegiNext.Application.Tasks;
using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal sealed class AudioExportTask(WorkbenchSession session, ExportCoordinator coordinator,
    AudioExportRequest request) : AegiTask<AudioExportResult>
{
    public override string Name => "Tasks.AudioExport";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.DeferredStoragePath(request.OutputPath)];

    /// <inheritdoc />
    protected override Task<AudioExportResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return coordinator.RunAudioExportAsync(request, context);
    }
}
