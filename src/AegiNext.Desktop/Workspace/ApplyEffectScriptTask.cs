using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using System.Collections.Immutable;

namespace AegiNext.Desktop.Workspace;

internal sealed class ApplyEffectScriptTask(WorkbenchSession session, string source, IReadOnlyCollection<Guid> layerIds,
    ProjectDocument captured, long inputRevision, AnimationTrackTarget? targetContext = null, string? name = null,
    ImmutableDictionary<string, EffectScriptParameterValue>? parameters = null) : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ApplyEffectScript";
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        var prepared = await Task.Run(() => ProjectEditingOperations.AddEffectInstances(captured,
            layerIds, name ?? EffectScriptParser.Parse(source).Id, source, parameters, targetContext, context.CancellationToken), context.CancellationToken);
        using var editLease = context.AcquireEditLease();
        context.EnterCommit(() => !Session.IsClosing && Session.TaskInputRevision == inputRevision &&
            !Session.HasProjectDrafts && ReferenceEquals(captured, Session.Editor.Snapshot));
        Session.Editor.Apply("Add effect instances", _ => prepared);
        Session.LogInfo("Effects", Localization.Get("Workbench.AddEffectInstance"));
        return true;
    }
}
