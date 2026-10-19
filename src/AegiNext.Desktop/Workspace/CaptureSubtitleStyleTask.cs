using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class CaptureSubtitleStyleTask(WorkbenchSession session, StyleLibraryCoordinator coordinator,
    Guid presetId, SubtitleLine cue, ProjectDocument captured, string directory) : AegiTask
{
    public override string Name => "Tasks.CaptureStyle";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE), AegiTaskResource.DeferredStoragePath(directory)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) => coordinator.CaptureCoreAsync(presetId, cue, captured, directory, context);
}
