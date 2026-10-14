using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class EffectScriptPreviewController : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Action<EffectScriptPreviewUpdate> updated;
    private readonly TimeProvider clock;
    private readonly TimeSpan inputDebounce;
    private readonly Func<IEffectScriptPreviewRenderer> createRenderer;
    private readonly Task worker;
    private TaskCompletionSource completion = CompletedSignal();
    private EffectScriptPreviewRequest? request;
    private EffectScriptPreviewUpdate? pendingPresentation;
    private CancellationTokenSource? activeCancellation;
    private ITimer? timer;
    private MediaTime position;
    private MediaTime presentedPosition;
    private MediaTime playbackOrigin;
    private MediaTime scrubOrigin;
    private long playbackStarted;
    private long inputStarted;
    private long renderStarted;
    private long generation;
    private long sequence;
    private long timerRevision;
    private TimeSpan inputDelay;
    private TimeSpan renderDelay;
    private Exception? error;
    private bool playing;
    private bool scrubbing;
    private bool resumeAfterScrub;
    private bool visible = true;
    private bool loop = true;
    private bool clockRunning;
    private bool pendingRender;
    private bool pendingPlayback;
    private bool disposed;

    internal EffectScriptPreviewController(Action<EffectScriptPreviewUpdate> updated,
        Func<IEffectScriptPreviewRenderer>? createRenderer = null, TimeProvider? timeProvider = null,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(updated);
        this.updated = updated;
        this.createRenderer = createRenderer ?? (() => new EffectScriptPreviewRenderer());
        clock = timeProvider ?? TimeProvider.System;
        inputDebounce = debounce ?? TimeSpan.FromMilliseconds(200);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputDebounce, TimeSpan.Zero);
        worker = Task.Factory.StartNew(Process, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal EffectScriptPreviewSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return SnapshotCore();
            }
        }
    }

    internal Task Completion
    {
        get
        {
            lock (gate)
            {
                return completion.Task;
            }
        }
    }

    internal bool Loop
    {
        get
        {
            lock (gate)
            {
                return loop;
            }
        }
        set
        {
            EffectScriptPreviewUpdate update;
            lock (gate)
            {
                if (disposed || loop == value)
                {
                    return;
                }
                BeginGeneration();
                loop = value;
                clockRunning = false;
                BeginCompletion();
                ScheduleCurrent();
                update = CreateUpdate();
            }
            updated(update);
        }
    }

    internal bool Submit(EffectScriptPreviewRequest value, bool autoPlay = true, bool debounce = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }
            var visualChange = request is null || request.DraftId != value.DraftId || !request.Input.HasSameVisuals(value.Input);
            var presentationChange = request is null || !request.RenderScaling.Equals(value.RenderScaling) ||
                !ReferenceEquals(request.FontCatalog, value.FontCatalog) || request.Input.OutputWidth != value.Input.OutputWidth ||
                request.Input.OutputHeight != value.Input.OutputHeight;
            if (!visualChange && !presentationChange)
            {
                return false;
            }
            BeginGeneration();
            request = value;
            error = null;
            scrubbing = false;
            clockRunning = false;
            if (visualChange)
            {
                position = MediaTime.Zero;
                presentedPosition = MediaTime.Zero;
                playing = autoPlay && visible;
                inputStarted = clock.GetTimestamp();
                inputDelay = debounce ? inputDebounce : TimeSpan.Zero;
            }
            else
            {
                position = QuantizePosition(presentedPosition);
            }
            BeginCompletion();
            ScheduleCurrent();
            update = CreateUpdate(clearFrame: visualChange);
        }
        updated(update);
        return true;
    }

    internal void Play()
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || request is null || !visible || playing && !scrubbing)
            {
                return;
            }
            BeginGeneration();
            playing = true;
            scrubbing = false;
            error = null;
            clockRunning = false;
            if (position >= LastPosition())
            {
                position = MediaTime.Zero;
            }
            BeginCompletion();
            ScheduleCurrent();
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void Pause()
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || request is null || !playing)
            {
                return;
            }
            BeginGeneration();
            playing = false;
            clockRunning = false;
            position = presentedPosition;
            BeginCompletion();
            ScheduleCurrent();
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void BeginScrub()
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || request is null || !visible || scrubbing)
            {
                return;
            }
            BeginGeneration();
            scrubOrigin = presentedPosition;
            resumeAfterScrub = playing;
            playing = false;
            scrubbing = true;
            clockRunning = false;
            position = presentedPosition;
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void Seek(MediaTime value)
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || request is null)
            {
                return;
            }
            BeginGeneration();
            position = QuantizePosition(value);
            error = null;
            clockRunning = false;
            BeginCompletion();
            ScheduleCurrent();
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void EndScrub()
    {
        FinishScrub(false);
    }

    internal void CancelScrub()
    {
        FinishScrub(true);
    }

    private void FinishScrub(bool canceled)
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || request is null || !scrubbing)
            {
                return;
            }
            BeginGeneration();
            if (canceled)
            {
                position = QuantizePosition(scrubOrigin);
            }
            scrubbing = false;
            playing = resumeAfterScrub && visible;
            clockRunning = false;
            BeginCompletion();
            ScheduleCurrent();
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void SetVisible(bool value)
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed || visible == value)
            {
                return;
            }
            BeginGeneration();
            visible = value;
            playing = false;
            scrubbing = false;
            clockRunning = false;
            position = presentedPosition;
            if (visible && request is not null)
            {
                BeginCompletion();
                ScheduleCurrent();
            }
            update = CreateUpdate();
        }
        updated(update);
    }

    internal void Invalidate()
    {
        EffectScriptPreviewUpdate update;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            BeginGeneration();
            request = null;
            position = MediaTime.Zero;
            presentedPosition = MediaTime.Zero;
            playing = false;
            scrubbing = false;
            clockRunning = false;
            inputDelay = TimeSpan.Zero;
            error = null;
            update = CreateUpdate(clearFrame: true);
        }
        updated(update);
    }

    internal bool IsCurrent(EffectScriptPreviewUpdate update)
    {
        lock (gate)
        {
            return IsCurrentCore(update);
        }
    }

    internal void AcknowledgePresentation(EffectScriptPreviewUpdate update)
    {
        lock (gate)
        {
            if (update.Frame is null || !IsCurrentCore(update) || !ReferenceEquals(pendingPresentation, update))
            {
                return;
            }
            pendingPresentation = null;
            presentedPosition = update.Snapshot.Position;
            if (!playing || !visible || scrubbing || request is null)
            {
                return;
            }
            if (update.CompletesCycle)
            {
                position = MediaTime.Zero;
                clockRunning = false;
                Schedule(FrameInterval(), false);
            }
            else
            {
                if (!clockRunning)
                {
                    playbackOrigin = presentedPosition;
                    playbackStarted = clock.GetTimestamp();
                    clockRunning = true;
                }
                var current = playbackOrigin + MediaTime.FromTimeSpan(clock.GetElapsedTime(playbackStarted));
                var next = presentedPosition + new MediaTime(1, request.Input.Options.FramesPerSecond);
                Schedule(next > current ? (next - current).ToTimeSpan(MediaTimeRounding.CEILING) : TimeSpan.Zero, true);
            }
        }
    }

    private bool IsCurrentCore(EffectScriptPreviewUpdate update)
    {
        return !disposed && update.Snapshot.Generation == generation && ReferenceEquals(update.Snapshot.Request, request) &&
            update.Sequence == sequence && (update.Frame is null || ReferenceEquals(pendingPresentation, update));
    }

    private EffectScriptPreviewSnapshot SnapshotCore()
    {
        return new(request, generation, position, playing, visible && (pendingRender || activeCancellation is not null),
            scrubbing, visible, loop, error);
    }

    private EffectScriptPreviewUpdate CreateUpdate(SdrVideoFrame? frame = null, bool clearFrame = false, bool closesCycle = false)
    {
        var update = new EffectScriptPreviewUpdate(SnapshotCore(), frame, clearFrame, ++sequence, closesCycle);
        if (frame is not null)
        {
            pendingPresentation = update;
        }
        return update;
    }

    private void BeginGeneration()
    {
        generation++;
        activeCancellation?.Cancel();
        pendingPresentation = null;
        pendingRender = false;
        StopTimer();
        completion.TrySetResult();
    }

    private void BeginCompletion()
    {
        completion.TrySetResult();
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ScheduleCurrent()
    {
        if (request is null || !visible)
        {
            completion.TrySetResult();
            return;
        }
        var remaining = inputDelay - clock.GetElapsedTime(inputStarted);
        Schedule(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, false);
    }

    private void Schedule(TimeSpan delay, bool playback)
    {
        pendingRender = true;
        pendingPlayback = playback;
        renderStarted = clock.GetTimestamp();
        renderDelay = delay;
        StopTimer();
        if (delay > TimeSpan.Zero)
        {
            ArmTimer(delay);
        }
        Monitor.PulseAll(gate);
    }

    private void ArmTimer(TimeSpan delay)
    {
        var revision = ++timerRevision;
        timer = clock.CreateTimer(_ =>
        {
            lock (gate)
            {
                if (!disposed && timerRevision == revision)
                {
                    StopTimer();
                    Monitor.PulseAll(gate);
                }
            }
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void StopTimer()
    {
        timerRevision++;
        timer?.Dispose();
        timer = null;
    }

    private MediaTime LastPosition()
    {
        var options = request!.Input.Options;
        var count = options.Duration.ToTimestamp(new(1, options.FramesPerSecond), MediaTimeRounding.CEILING).Value;
        return new(count - 1, options.FramesPerSecond);
    }

    private MediaTime QuantizePosition(MediaTime value)
    {
        var last = LastPosition();
        if (value <= MediaTime.Zero)
        {
            return MediaTime.Zero;
        }
        if (value >= last)
        {
            return last;
        }
        var frames = value.ToTimestamp(new(1, request!.Input.Options.FramesPerSecond), MediaTimeRounding.FLOOR).Value;
        return new(frames, request.Input.Options.FramesPerSecond);
    }

    private TimeSpan FrameInterval()
    {
        return new MediaTime(1, request!.Input.Options.FramesPerSecond).ToTimeSpan(MediaTimeRounding.CEILING);
    }

    private void Process()
    {
        try
        {
            using var renderer = createRenderer();
            while (TakeWork() is { } work)
            {
                ProcessWork(renderer, work.Request, work.Generation, work.Position, work.ClosesCycle, work.Cancellation);
            }
        }
        finally
        {
            lock (gate)
            {
                disposed = true;
                StopTimer();
                pendingPresentation = null;
                pendingRender = false;
                activeCancellation?.Dispose();
                activeCancellation = null;
                completion.TrySetResult();
            }
        }
    }

    private (EffectScriptPreviewRequest Request, long Generation, MediaTime Position, bool ClosesCycle,
        CancellationTokenSource Cancellation)? TakeWork()
    {
        lock (gate)
        {
            while (!disposed)
            {
                if (visible && request is { } current && pendingRender && pendingPresentation is null)
                {
                    var remaining = renderDelay - clock.GetElapsedTime(renderStarted);
                    if (remaining <= TimeSpan.Zero)
                    {
                        StopTimer();
                        pendingRender = false;
                        inputDelay = TimeSpan.Zero;
                        if (pendingPlayback && playing && clockRunning)
                        {
                            position = QuantizePosition(playbackOrigin + MediaTime.FromTimeSpan(clock.GetElapsedTime(playbackStarted)));
                        }
                        var cancellation = new CancellationTokenSource();
                        activeCancellation = cancellation;
                        return (current, generation, position, playing && !scrubbing && position == LastPosition(), cancellation);
                    }
                    if (timer is null)
                    {
                        ArmTimer(remaining);
                    }
                }
                Monitor.Wait(gate);
            }
            return null;
        }
    }

    private void ProcessWork(IEffectScriptPreviewRenderer renderer, EffectScriptPreviewRequest current, long revision,
        MediaTime target, bool closesCycle, CancellationTokenSource cancellation)
    {
        SdrVideoFrame? frame = null;
        Exception? failure = null;
        try
        {
            frame = renderer.Render(current, target, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        EffectScriptPreviewUpdate? update = null;
        TaskCompletionSource? delivered = null;
        lock (gate)
        {
            activeCancellation = null;
            if (!disposed && !cancellation.IsCancellationRequested && visible && revision == generation && ReferenceEquals(request, current))
            {
                error = failure;
                if (failure is not null || closesCycle && !loop)
                {
                    playing = false;
                    clockRunning = false;
                }
                position = target;
                update = CreateUpdate(frame, failure is not null, closesCycle);
                delivered = completion;
            }
            cancellation.Dispose();
        }
        if (update is not null && IsCurrent(update))
        {
            try
            {
                updated(update);
            }
            finally
            {
                delivered!.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    /// <summary>取消全部请求、计时器和迟到交付；不等待或跨线程释放原生对象。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            BeginGeneration();
            disposed = true;
            request = null;
            playing = false;
            scrubbing = false;
            visible = false;
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>等待创建线程上的渲染器和原生缓存完成释放。</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await worker.ConfigureAwait(false);
    }
}
