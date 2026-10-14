using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed class EffectLibraryImportCommitTask(DesktopApplicationContext owner, EffectLibraryImportPlan plan,
    IReadOnlyDictionary<int, EffectLibraryImportDecision> decisions) : AegiTask
{
    private readonly ImmutableDictionary<int, EffectLibraryImportDecision> frozenDecisions = decisions.ToImmutableDictionary();

    public override string Name => "Tasks.ImportEffectLibrary";
    public override IReadOnlyCollection<AegiTaskResource> Resources => owner.EffectLibraryResources;

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return owner.RunEffectLibraryOperationAsync(() => Task.Run(() => owner.EffectLibraryTransfer.CommitImportAsync(
            plan, frozenDecisions, () => context.EnterCommit(), context.CancellationToken), context.CancellationToken), reportError: false);
    }
}
