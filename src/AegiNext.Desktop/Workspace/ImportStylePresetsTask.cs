using AegiNext.Application.Tasks;
using AegiNext.Application.Presets;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ImportStylePresetsTask(WorkbenchSession session, string path) : AegiTask
{
    public override string Name => "Tasks.ImportStyles";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE), AegiTaskResource.DeferredStoragePath(path)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) => session.ApplicationContext.RunStyleOperationAsync(async () =>
    {
        var transfer = session.ApplicationContext.EffectLibraryTransfer;
        var plan = await transfer.AnalyzeImportAsync([path], context.CancellationToken);
        if (plan.Rows.Any(row => row.Status == EffectLibraryImportStatus.CONFLICT))
        {
            throw new InvalidDataException(Localization.Get("EffectLibrary.StyleConflictUseLibrary"));
        }
        await transfer.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>(),
            () => context.EnterCommit(), context.CancellationToken);
    });
}
