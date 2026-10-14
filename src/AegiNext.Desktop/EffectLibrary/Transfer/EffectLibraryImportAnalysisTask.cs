using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed class EffectLibraryImportAnalysisTask(DesktopApplicationContext owner, IReadOnlyList<string> paths)
    : AegiTask<EffectLibraryImportPlan>
{
    private readonly string[] inputs = paths.Select(Path.GetFullPath).ToArray();

    public override string Name => "Tasks.ImportEffectLibrary";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. owner.EffectLibraryResources, .. inputs.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task<EffectLibraryImportPlan> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return Task.Run(() => owner.EffectLibraryTransfer.AnalyzeImportAsync(inputs, context.CancellationToken),
            context.CancellationToken);
    }
}
