using AegiNext.Application.Tasks;
using AegiNext.Desktop.Koumori;

namespace AegiNext.Desktop.Workspace;

internal sealed class KoumoriAnalysisTask(WorkbenchSession session, CancellationToken cancellationToken,
    IProgress<double>? progress) : AegiTask<KoumoriPreview>
{
    public override string Name => "Koumori.Title";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.None;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Named("koumori:" + session.TaskScope)];
    protected override Task<KoumoriPreview> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        session.ExecuteKoumoriAnalysisAsync(context, cancellationToken, progress);
}
