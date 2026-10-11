using System.Diagnostics;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class ScenePreviewScheduler : IDisposable
{
    private readonly object gate = new();
    private readonly Func<ScenePreviewRequest, CancellationToken, SdrVideoFrame> compose;
    private readonly Action<ScenePreviewResult> present;
    private readonly Action? release;
    private readonly Func<Exception?>? getImageWarning;
    private ScenePreviewRequest? pending;
    private CancellationTokenSource? activeCancellation;
    private Task? worker;
    private long latestSequence;
    private bool disposed;

    internal ScenePreviewScheduler(Action<ScenePreviewResult> present)
    {
        var compositor = new ScenePreviewCompositor();
        compose = compositor.Compose;
        release = compositor.Dispose;
        getImageWarning = () => compositor.ImageWarning;
        this.present = present;
    }

    internal ScenePreviewScheduler(Func<ScenePreviewRequest, CancellationToken, SdrVideoFrame> compose, Action<ScenePreviewResult> present)
    {
        this.compose = compose;
        this.present = present;
    }

    internal Task Completion
    {
        get
        {
            lock (gate)
            {
                return worker ?? Task.CompletedTask;
            }
        }
    }

    internal void Submit(ScenePreviewRequest request)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            latestSequence = request.Sequence;
            pending = request;
            activeCancellation?.Cancel();
            worker ??= Task.Run(Process);
        }
    }

    private void Process()
    {
        while (true)
        {
            ScenePreviewRequest request;
            CancellationTokenSource cancellation;
            lock (gate)
            {
                if (disposed || pending is null)
                {
                    worker = null;
                    if (disposed)
                    {
                        release?.Invoke();
                    }
                    return;
                }
                request = pending;
                pending = null;
                activeCancellation = cancellation = new();
            }
            var started = Stopwatch.GetTimestamp();
            SdrVideoFrame? frame = null;
            Exception? error = null;
            try
            {
                frame = compose(request, cancellation.Token);
                error = getImageWarning?.Invoke();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception failure)
            {
                error = failure;
                frame = ScenePreviewCompositor.Fallback(request);
            }
            lock (gate)
            {
                if (!disposed && !cancellation.IsCancellationRequested && request.Sequence == latestSequence)
                {
                    present(new(request, frame, error, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                }
                activeCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            pending = null;
            activeCancellation?.Cancel();
            if (worker is null)
            {
                release?.Invoke();
            }
        }
    }
}
