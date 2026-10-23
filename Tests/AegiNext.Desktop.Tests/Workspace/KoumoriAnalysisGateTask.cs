using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class KoumoriAnalysisGateTask(string scopeId, AegiTaskResource? resource = null) : AegiTask
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string Name => "Controlled Koumori analysis";
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [resource ?? AegiTaskResource.Named("koumori:" + scopeId)];

    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        Started.TrySetResult();
        await Release.Task.WaitAsync(context.CancellationToken);
    }
}
