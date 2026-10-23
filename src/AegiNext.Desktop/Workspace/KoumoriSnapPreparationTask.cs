using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class KoumoriSnapPreparationTask(WorkbenchSession session, KoumoriSnapRequest request,
    CancellationToken cancellationToken) : AegiTask
{
    public override string Name => "Koumori.Analyzing";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Named("koumori:" + session.TaskScope)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return session.ExecuteKoumoriSnapPreparationAsync(request, context, cancellationToken);
    }
}
