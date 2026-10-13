using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class EffectPresetPreviewScheduler : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly EffectPresetPreviewOptions options;
    private readonly Action<EffectPresetPreviewResult> present;
    private readonly Func<IEffectPresetPreviewRenderer> createRenderer;
    private readonly EffectPresetPreviewFrameCache cache;
    private readonly Dictionary<string, EffectPresetPreviewVisibleRequest> visible = new(StringComparer.Ordinal);
    private readonly Queue<string> staticQueue = new();
    private readonly Task worker;
    private readonly long durationFrames;
    private TaskCompletionSource quiescence = CompletedSignal();
    private EffectPresetPreviewWork? activeWork;
    private string? activeId;
    private long nextGeneration;
    private long animationGeneration;
    private long animationStart;
    private long lastAnimationFrame = -1;
    private bool animationFailed;
    private bool preferStatic;
    private bool disposed;

    internal EffectPresetPreviewScheduler(Action<EffectPresetPreviewResult> present,
        EffectPresetPreviewOptions? options = null, Func<IEffectPresetPreviewRenderer>? createRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(present);
        this.options = options ?? new();
        this.options.Validate();
        this.present = present;
        this.createRenderer = createRenderer ?? (() => new EffectPresetPreviewRenderer(this.options));
        cache = new(this.options.StaticCacheBytes);
        durationFrames = this.options.Duration.ToTimestamp(new(1, this.options.FramesPerSecond), MediaTimeRounding.CEILING).Value;
        worker = Task.Factory.StartNew(Process, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal Task Completion
    {
        get
        {
            lock (gate)
            {
                return quiescence.Task;
            }
        }
    }

    internal EffectPresetPreviewCacheStatistics CacheStatistics => cache.Statistics;

    internal void UpdateVisible(IReadOnlyList<EffectPresetPreviewRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var replacement = new Dictionary<string, EffectPresetPreviewRequest>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            _ = EffectPresetPreviewCacheKey.GetOutputSize(options, request.RenderScaling);
            if (!replacement.TryAdd(request.PresetId, request))
            {
                throw new ArgumentException("可见特效预设身份不能重复。", nameof(requests));
            }
        }
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            var previousActive = activeId is not null ? visible.GetValueOrDefault(activeId) : null;
            foreach (var id in visible.Keys.Where(id => !replacement.ContainsKey(id)).ToArray())
            {
                visible.Remove(id);
            }
            staticQueue.Clear();
            foreach (var request in requests)
            {
                if (!visible.TryGetValue(request.PresetId, out var entry) || entry.Request != request)
                {
                    entry = new(request, ++nextGeneration);
                    visible[request.PresetId] = entry;
                }
                entry.StaticQueued = false;
                if (!entry.StaticCompleted && !entry.StaticInFlight)
                {
                    QueueStatic(entry);
                }
            }
            var currentActive = activeId is not null ? visible.GetValueOrDefault(activeId) : null;
            if (!ReferenceEquals(previousActive, currentActive))
            {
                activeId = currentActive?.Request.PresetId;
                RestartAnimation();
            }
            if (activeWork is { } work && (!visible.TryGetValue(work.Request.PresetId, out var current) || current.Request != work.Request))
            {
                work.Cancellation.Cancel();
            }
            if (staticQueue.Count > 0 || activeId is not null && !animationFailed)
            {
                MarkBusy();
            }
            Monitor.PulseAll(gate);
        }
    }

    internal void SetActive(string? presetId)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            presetId = presetId is not null && visible.ContainsKey(presetId) ? presetId : null;
            if (activeId == presetId)
            {
                return;
            }
            var previous = activeId is not null ? visible.GetValueOrDefault(activeId) : null;
            activeId = presetId;
            RestartAnimation();
            if (previous is not null)
            {
                previous.StaticCompleted = false;
                QueueStatic(previous);
            }
            if (staticQueue.Count > 0 || activeId is not null)
            {
                MarkBusy();
            }
            Monitor.PulseAll(gate);
        }
    }

    internal void Invalidate()
    {
        lock (gate)
        {
            visible.Clear();
            staticQueue.Clear();
            activeId = null;
            RestartAnimation();
            Monitor.PulseAll(gate);
        }
    }

    internal bool IsCurrent(EffectPresetPreviewResult result)
    {
        lock (gate)
        {
            return IsCurrentCore(result);
        }
    }

    private bool IsCurrentCore(EffectPresetPreviewResult result)
    {
        if (disposed || !visible.TryGetValue(result.Request.PresetId, out var entry) || entry.Request != result.Request)
        {
            return false;
        }
        return result.IsAnimated
            ? activeId == result.Request.PresetId && animationGeneration == result.Generation
            : activeId != result.Request.PresetId && entry.Generation == result.Generation;
    }

    private void RestartAnimation()
    {
        animationGeneration = ++nextGeneration;
        animationStart = 0;
        lastAnimationFrame = -1;
        animationFailed = false;
        preferStatic = false;
        activeWork?.Cancellation.Cancel();
    }

    private void QueueStatic(EffectPresetPreviewVisibleRequest entry)
    {
        if (entry.StaticQueued || entry.StaticInFlight)
        {
            return;
        }
        entry.StaticQueued = true;
        staticQueue.Enqueue(entry.Request.PresetId);
    }

    private void MarkBusy()
    {
        if (quiescence.Task.IsCompleted)
        {
            quiescence = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void Process()
    {
        try
        {
            using var renderer = createRenderer();
            while (TakeWork() is { } work)
            {
                ProcessWork(renderer, work);
            }
        }
        catch (Exception failure)
        {
            lock (gate)
            {
                quiescence.TrySetException(failure);
            }
            throw;
        }
        finally
        {
            cache.Clear();
            lock (gate)
            {
                disposed = true;
                visible.Clear();
                staticQueue.Clear();
                quiescence.TrySetResult();
            }
        }
    }

    private EffectPresetPreviewWork? TakeWork()
    {
        lock (gate)
        {
            while (!disposed)
            {
                var elapsed = activeId is not null && lastAnimationFrame >= 0
                    ? Stopwatch.GetElapsedTime(animationStart).TotalSeconds
                    : 0;
                var frame = (long)(elapsed * options.FramesPerSecond);
                var animationReady = activeId is not null && !animationFailed && frame > lastAnimationFrame;
                if (animationReady && (!preferStatic || staticQueue.Count == 0) && visible.TryGetValue(activeId!, out var animated))
                {
                    if (lastAnimationFrame < 0)
                    {
                        animationStart = Stopwatch.GetTimestamp();
                    }
                    lastAnimationFrame = frame;
                    preferStatic = true;
                    return BeginWork(animated.Request, animationGeneration,
                        new(frame % durationFrames, options.FramesPerSecond), true);
                }
                while (staticQueue.TryDequeue(out var id))
                {
                    if (!visible.TryGetValue(id, out var entry) || !entry.StaticQueued)
                    {
                        continue;
                    }
                    entry.StaticQueued = false;
                    entry.StaticInFlight = true;
                    preferStatic = false;
                    return BeginWork(entry.Request, entry.Generation, options.PosterTime, false);
                }
                if (activeId is not null && !animationFailed)
                {
                    var delay = Math.Max(1, ((lastAnimationFrame + 1d) / options.FramesPerSecond - elapsed) * 1000);
                    Monitor.Wait(gate, TimeSpan.FromMilliseconds(delay));
                }
                else
                {
                    quiescence.TrySetResult();
                    Monitor.Wait(gate);
                }
            }
            return null;
        }
    }

    private EffectPresetPreviewWork BeginWork(EffectPresetPreviewRequest request, long generation, MediaTime position, bool animated)
    {
        MarkBusy();
        activeWork = new(request, generation, position, animated, new());
        return activeWork;
    }

    private void ProcessWork(IEffectPresetPreviewRenderer renderer, EffectPresetPreviewWork work)
    {
        SdrVideoFrame? frame = null;
        Exception? error = null;
        var token = work.Cancellation.Token;
        var key = EffectPresetPreviewCacheKey.Create(options, work.Request);
        try
        {
            if (work.IsAnimated || !cache.TryGet(key, out frame))
            {
                frame = renderer.Render(work.Request, work.Position, token);
            }
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            error = failure;
        }
        EffectPresetPreviewResult? result = null;
        lock (gate)
        {
            activeWork = null;
            var entry = visible.GetValueOrDefault(work.Request.PresetId);
            var current = !disposed && entry?.Request == work.Request;
            if (!work.IsAnimated && current)
            {
                entry!.StaticInFlight = false;
                if (token.IsCancellationRequested)
                {
                    entry.StaticCompleted = false;
                    QueueStatic(entry);
                }
                else
                {
                    entry.StaticCompleted = true;
                    if (frame is not null)
                    {
                        cache.Add(key, frame);
                    }
                }
            }
            if (!token.IsCancellationRequested && current)
            {
                var delivered = new EffectPresetPreviewResult(work.Request, work.Generation, work.Position, work.IsAnimated, frame, error);
                if (IsCurrentCore(delivered))
                {
                    result = delivered;
                    if (work.IsAnimated && error is not null)
                    {
                        animationFailed = true;
                    }
                }
            }
            work.Cancellation.Dispose();
        }
        if (result is not null && IsCurrent(result))
        {
            present(result);
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    /// <summary>立即拒绝后续请求与迟到帧；异步关闭仍须等待 DisposeAsync。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            activeId = null;
            visible.Clear();
            staticQueue.Clear();
            activeWork?.Cancellation.Cancel();
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>停止预览并等待工作者在创建线程上释放渲染器和原生缓存。</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await worker.ConfigureAwait(false);
    }
}
