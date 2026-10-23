using AegiNext.Application.Tasks;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private KoumoriSnapRequest? koumoriPreparationRequest;

    internal Task ObserveKoumoriProjectOpenAsync(AegiTaskHandle<ProjectOpenResult> handle)
    {
        var operation = ResumeKoumoriProjectAnalysisAfterOpenAsync(handle, projectGeneration);
        koumoriSnapCompletion = koumoriSnapCompletion.IsCompleted
            ? operation : Task.WhenAll(koumoriSnapCompletion, operation);
        return operation;
    }

    private async Task ResumeKoumoriProjectAnalysisAfterOpenAsync(AegiTaskHandle<ProjectOpenResult> handle, long generation)
    {
        await ((Task)handle.Completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (handle.Snapshot.State != AegiTaskState.Succeeded && generation == projectGeneration && !closing && !IsProjectBusy)
        {
            PrepareKoumoriProjectAnalysis();
        }
    }

    private void RetryKoumoriSnapAnalysis()
    {
        if (koumoriSnapCache.PendingRequest is null)
        {
            koumoriSnapCache.CancelPending();
        }

        QueueKoumoriSnapAnalysis();
    }

    internal bool CancelKoumoriProjectAnalysis()
    {
        if (koumoriPreparationRequest is not { } request || !koumoriSnapCache.Cancel(request))
        {
            return false;
        }

        koumoriSnapCancellation?.Cancel();
        return true;
    }

    internal void PrepareKoumoriProjectAnalysis(AegiTaskExecutionContext? context = null)
    {
        RefreshKoumoriAnalysisContext();
        if (!CanAnalyzeKoumori)
        {
            return;
        }

        koumoriSnapCancellation?.Cancel();
        koumoriSnapCache.CancelPending();
        var sampleCount = checked((long)Math.Round(ToSeconds(controller.Snapshot.Duration!.Value) * KoumoriAnalyzer.SAMPLE_RATE));
        if (!koumoriSnapCache.TryRequest(new(0, sampleCount), sampleCount, out var request))
        {
            return;
        }

        koumoriPreparationRequest = request;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ProjectOperationsToken, koumoriContextCancellation.Token);
        koumoriSnapCancellation = cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        koumoriSnapCompletion = koumoriSnapCompletion.IsCompleted
            ? completion.Task : Task.WhenAll(koumoriSnapCompletion, completion.Task);
        QueueKoumoriSnapAnalysis();
        try
        {
            var task = new KoumoriSnapPreparationTask(this, request!, cancellation.Token);
            if (context is not null)
            {
                context.ScheduleAfterCompletion(task, handle =>
                {
                    _ = ObserveKoumoriSnapPreparationAsync(handle, request!, cancellation, completion);
                });
            }
            else
            {
                _ = ObserveKoumoriSnapPreparationAsync(applicationContext.Tasks.Submit(task), request!, cancellation, completion);
            }
        }
        catch
        {
            _ = ObserveKoumoriSnapPreparationAsync(null, request!, cancellation, completion);
            throw;
        }
    }

    internal async Task ExecuteKoumoriSnapPreparationAsync(KoumoriSnapRequest request,
        AegiTaskExecutionContext context, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cancellationToken);
        lifetime.Token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(koumoriSnapCache.PendingRequest, request) || request.ContextIdentity != koumoriContextIdentity)
        {
            throw new OperationCanceledException(lifetime.Token);
        }

        var preview = await AnalyzeKoumoriCoreAsync(request.Range, new Progress<KoumoriAnalysisProgress>(value =>
        {
            context.ReportProgress(new("Koumori.Analyzing", value.CompletedFrames, value.TotalFrames));
        }), lifetime.Token);
        if (closing || !koumoriSnapCache.Complete(request, preview.Result.Boundaries))
        {
            throw new OperationCanceledException(lifetime.Token);
        }

        QueueKoumoriSnapAnalysis();
    }

    private async Task ObserveKoumoriSnapPreparationAsync(AegiTaskHandle? handle, KoumoriSnapRequest request,
        CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        try
        {
            if (handle is null)
            {
                koumoriSnapCache.Cancel(request);
                return;
            }

            using var registration = cancellation.Token.Register(() => handle.RequestCancel());
            try
            {
                await handle.Completion;
            }
            catch (OperationCanceledException)
            {
                koumoriSnapCache.Cancel(request);
            }
            catch (Exception error)
            {
                koumoriSnapCache.Fail(request, error.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(koumoriSnapCancellation, cancellation))
            {
                koumoriSnapCancellation = null;
            }

            cancellation.Dispose();
            completion.TrySetResult();
            if (!closing)
            {
                QueueKoumoriSnapAnalysis();
            }
        }
    }
}
