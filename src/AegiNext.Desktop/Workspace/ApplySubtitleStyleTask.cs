using System.Collections.Immutable;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ApplySubtitleStyleTask(WorkbenchSession session, StyleLibraryCoordinator coordinator,
    SubtitleStylePreset preset, ImmutableArray<Guid> cueIds, ProjectDocument captured, long inputRevision, string directory)
    : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ApplyStyle";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, Session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE)];
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        await coordinator.ApplyCoreAsync(preset, cueIds, captured, inputRevision, directory, context);
        return true;
    }
}
