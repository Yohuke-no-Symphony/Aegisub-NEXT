using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Workspace;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class KoumoriProjectOpenIntegrationTests
{
    [KoumoriIntegrationFact]
    public async Task OpenProjectPreparesWholeAudioThroughNativeDecodingAndOnnxWithoutEditingTheDocument()
    {
        await using var context = new KoumoriIntegrationContext(new(2));
        await context.InitializeAsync();
        var session = context.Session;
        session.ApplicationContext.Tasks.MaximumConcurrentTasks = 1;
        var path = Path.Combine(context.Workspace.DirectoryPath, "native-analysis.aeginext");
        await ProjectStore.SaveAsync(context.Editor.Snapshot, path);

        var result = await session.OpenProjectAsync(path).WaitAsync(TimeSpan.FromSeconds(20));
        var document = context.Editor.Snapshot;
        await session.ApplicationContext.Tasks.DrainScopeAsync(session.TaskScope).WaitAsync(TimeSpan.FromSeconds(20));
        var timeline = session.ViewModel.Timeline;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (timeline.KoumoriSnapState is not (KoumoriSnapState.Ready or KoumoriSnapState.ReadyEmpty))
        {
            Assert.NotEqual(KoumoriSnapState.Failed, timeline.KoumoriSnapState);
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Empty(result.Diagnostics);
        var preparation = Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.ScopeId == session.TaskScope && value.Name == "Koumori.Analyzing");
        Assert.Equal(AegiTaskState.Succeeded, preparation.State);
        Assert.NotNull(preparation.Progress.Fraction);
        Assert.Equal(1, preparation.Progress.Fraction);
        var preparedBoundaries = timeline.KoumoriBoundaries.ToArray();
        var preview = await session.AnalyzeKoumoriAsync(timeout.Token, null);
        var endSample = 2 * KoumoriAnalyzer.SAMPLE_RATE;
        Assert.Equal(preview.Result.Boundaries.Where(value => value.Sample >= 0 && value.Sample <= endSample).ToArray(), preparedBoundaries);
        Assert.Equal(new KoumoriAnalysisRange(0, endSample), preview.Result.Range);

        timeline.VisibleDuration = 0.5;
        timeline.ViewStart = 1;
        session.Tick();

        Assert.Equal(preparedBoundaries, timeline.KoumoriBoundaries);
        Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(), value => value.Name == "Koumori.Analyzing");
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(session.IsProjectBusy);
        Assert.False(timeline.IsKoumoriSnapEnabled);
    }
}
