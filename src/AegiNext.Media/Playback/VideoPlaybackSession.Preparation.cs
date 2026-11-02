using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Media.Playback;

public sealed partial class VideoPlaybackSession
{
    private readonly Queue<PreparedVideoTiming> preparedTimings = new();
    private readonly Queue<VideoPreparationCostObservation> decodeCosts = new();
    private VideoPreparationOptions? preparationOptions;
    private MediaTime downstreamPreparationLead;
    private MediaTime minimumPreparationLead;
    private MediaTime preparationLead;
    private int preparationPendingCount;
    private long preparationPendingBytes;

    /// <summary>取得会话用于调度和处理耗时测量的同一个单调时间提供者。</summary>
    public TimeProvider TimeProvider => timeProvider;

    /// <summary>取得调度正在使用的提前量；该值已限制在配置的最大提前范围内。</summary>
    public MediaTime PreparationLead
    {
        get
        {
            lock (gate)
            {
                RefreshDecodePreparationLeadUnderLock();
                return preparationLead;
            }
        }
    }

    /// <summary>取得允许的最大准备提前范围，供成本估计及交付调度保持同一边界。</summary>
    public MediaTime MaximumPreparationAhead
    {
        get
        {
            lock (gate)
            {
                return (preparationOptions ?? throw new InvalidOperationException("会话没有启用提前准备。")).MaximumAhead;
            }
        }
    }

    /// <summary>在打开前选择提前准备模式；原始帧和解码游标仍由本会话独占。</summary>
    public void ConfigurePreparation(VideoPreparationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closeRequested, this);
            if (snapshot.State != VideoPlaybackState.CREATED)
            {
                throw new InvalidOperationException("只能在打开视频前配置提前准备。");
            }
            preparationOptions = options;
        }
    }

    /// <summary>结合已测得的解码、转换和交付成本调整准备范围；最小提前量允许保留交付管线仍可使用的帧。</summary>
    public void SetPreparationLead(MediaTime lead, MediaTime? minimumLead = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lead, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumLead ?? lead, MediaTime.Zero);
        lock (gate)
        {
            var options = preparationOptions ?? throw new InvalidOperationException("会话没有启用提前准备。");
            downstreamPreparationLead = lead < options.MaximumAhead ? lead : options.MaximumAhead;
            var minimum = minimumLead ?? lead;
            minimumPreparationLead = minimum < downstreamPreparationLead ? minimum : downstreamPreparationLead;
            RefreshDecodePreparationLeadUnderLock();
            PulseCommandUnderLock();
        }
    }

    /// <summary>接管一帧用于提前转换；帧可能尚未到显示时间，调用方必须根据真实区间调度呈现。</summary>
    public ValueTask<VideoPresentation?> ReadPreparationAsync(CancellationToken cancellationToken = default)
    {
        return ReadPresentationCoreAsync(true, cancellationToken);
    }

    /// <summary>等待原始媒体时钟到达目标；暂停、结束或请求变更会返回以便调用方重新验证身份。</summary>
    public Task WaitForPositionAsync(MediaTime target, CancellationToken cancellationToken = default)
    {
        return WaitForPositionAsync(target, Snapshot.Generation, cancellationToken);
    }

    /// <summary>只在指定代次中等待目标；新定位或暂停会立即终止旧帧的等待。</summary>
    public async Task WaitForPositionAsync(MediaTime target, long generation, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            MediaTime remaining;
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                remaining = target - GetPositionUnderLock();
                if (closeRequested || snapshot.Generation != generation || snapshot.State != VideoPlaybackState.PLAYING || remaining <= MediaTime.Zero)
                {
                    return;
                }
                changed = commandChanged.Task;
            }
            using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            var delay = Task.Delay(remaining.ToTimeSpan(MediaTimeRounding.CEILING), timeProvider, delayCancellation.Token);
            await Task.WhenAny(changed, delay).WaitAsync(cancellationToken).ConfigureAwait(false);
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private MediaTime? GetPreparationDueUnderLock(out bool advance)
    {
        advance = false;
        RefreshDecodePreparationLeadUnderLock();
        UpdatePreparedTimingUnderLock();
        if (snapshot.State != VideoPlaybackState.PLAYING)
        {
            return null;
        }
        var position = GetPositionUnderLock();
        MediaTime? due = preparedTimings.TryPeek(out var timing) ? timing.Time - position : null;
        var hasRoom = preparationPendingCount < preparationOptions!.PendingCapacity;
        if (hasRoom && (needsResynchronization || nextFrameTime is not null))
        {
            var preparationDue = needsResynchronization ? MediaTime.Zero : nextFrameTime!.Value - position - preparationLead;
            if (preparationDue <= MediaTime.Zero)
            {
                advance = true;
                return MediaTime.Zero;
            }
            due = due is { } existing && existing < preparationDue ? existing : preparationDue;
        }
        if (playbackRange is { } range)
        {
            var boundary = range.End - position;
            due = due is { } existing && existing < boundary ? existing : boundary;
        }
        return due;
    }

    private void UpdatePreparedTimingUnderLock()
    {
        if (preparationOptions is null || snapshot.State != VideoPlaybackState.PLAYING)
        {
            return;
        }
        var position = GetPositionUnderLock();
        if (playbackRange is { } range && position >= range.End)
        {
            CompleteRangeUnderLock(range);
            return;
        }
        while (preparedTimings.TryPeek(out var timing) && timing.Time <= position)
        {
            preparedTimings.Dequeue();
            snapshot = snapshot with { DisplayTime = timing.Time };
            if (timing.ReachedEnd && playbackRange is null)
            {
                reachedEnd = true;
                snapshot = snapshot with { State = VideoPlaybackState.ENDED, Position = timing.Time };
                PulsePresentationUnderLock();
                return;
            }
        }
    }

    private void AdvancePreparation(long generation)
    {
        PositionedVideoFrame? frame = null;
        try
        {
            MediaTime target;
            lock (gate)
            {
                RefreshDecodePreparationLeadUnderLock();
                target = GetPositionUnderLock() + (needsResynchronization ? preparationLead : minimumPreparationLead);
            }
            var resynchronized = needsResynchronization;
            frame = ReadPreparationFrame(generation, target, resynchronized, out var readCost);
            while (true)
            {
                ValidateFrame(frame);
                var resynchronize = false;
                lock (gate)
                {
                    if (closeRequested || generation != snapshot.Generation)
                    {
                        needsResynchronization = true;
                        return;
                    }
                    var position = GetPositionUnderLock();
                    if (playbackRange is { } range && (position >= range.End || frame is not null && frame.Time >= range.End))
                    {
                        nextFrameTime = null;
                        needsResynchronization = false;
                        if (position >= range.End)
                        {
                            CompleteRangeUnderLock(range);
                        }
                        return;
                    }
                    if (frame is null)
                    {
                        nextFrameTime = null;
                        needsResynchronization = false;
                        return;
                    }
                    if (playbackRange is { } bounded && target >= bounded.End)
                    {
                        target = position;
                    }
                    var behindClock = frame.NextFrameTime is { } end && end <= position;
                    if (!resynchronized && behindClock && frame.NextFrameTime is { } next && readCost >= next - frame.Time)
                    {
                        target = position + downstreamPreparationLead;
                        if (playbackRange is { } seekBounded && target >= seekBounded.End)
                        {
                            target = position;
                        }
                        resynchronize = true;
                    }
                    else if (resynchronized || frame.NextFrameTime is null || frame.NextFrameTime > target)
                    {
                        nextFrameTime = frame.NextFrameTime;
                        needsResynchronization = behindClock;
                        preparedTimings.Enqueue(new(frame.Time, frame.NextFrameTime, frame.ReachedEnd));
                        PublishUnderLock(frame, generation);
                        frame = null;
                        UpdatePreparedTimingUnderLock();
                        return;
                    }
                }
                frame.Dispose();
                resynchronized |= resynchronize;
                frame = ReadPreparationFrame(generation, target, resynchronize, out readCost);
            }
        }
        catch (OperationCanceledException) when (IsSuperseded(generation))
        {
            needsResynchronization = true;
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private PositionedVideoFrame? ReadPreparationFrame(long generation, MediaTime target, bool seek, out MediaTime elapsed)
    {
        var started = timeProvider.GetTimestamp();
        try
        {
            return seek
                ? source!.SeekFrame(target, () => IsSuperseded(generation), lifetime.Token)
                : source!.ReadFrame(lifetime.Token);
        }
        finally
        {
            elapsed = MediaTime.FromTimeSpan(timeProvider.GetElapsedTime(started));
            lock (gate)
            {
                decodeCosts.Enqueue(new(elapsed, timeProvider.GetTimestamp()));
                while (decodeCosts.Count > 16)
                {
                    decodeCosts.Dequeue();
                }
                RefreshDecodePreparationLeadUnderLock();
            }
        }
    }

    private void RefreshDecodePreparationLeadUnderLock()
    {
        if (preparationOptions is not { } options)
        {
            return;
        }
        var timestamp = timeProvider.GetTimestamp();
        var maximumAge = options.MaximumAhead.ToTimeSpan(MediaTimeRounding.CEILING);
        while (decodeCosts.TryPeek(out var cost) && timeProvider.GetElapsedTime(cost.Timestamp, timestamp) >= maximumAge)
        {
            decodeCosts.Dequeue();
        }
        var decodeLead = decodeCosts.Count == 0 ? MediaTime.Zero : decodeCosts.Max(cost => cost.Cost);
        var lead = downstreamPreparationLead + decodeLead;
        preparationLead = lead < options.MaximumAhead ? lead : options.MaximumAhead;
    }

    private long GetPreparationFrameBytes(IVideoFrame frame)
    {
        if (frame is AudioOnlyVideoFrame)
        {
            return 0;
        }

        var bytes = 0L;
        for (var index = 0; index < frame.Info.PlaneCount; index++)
        {
            var plane = frame.GetPlaneInfo(index);
            bytes = checked(bytes + Math.Max(plane.RowBytes, Math.Abs((long)plane.SourceStride)) * plane.Height);
        }
        var options = preparationOptions!;
        if (bytes > options.MaximumPendingBytes / options.PendingCapacity)
        {
            throw new NotSupportedException("视频原始帧超过提前准备的单帧内存预算。");
        }
        return bytes;
    }

    private void ReleasePreparation(long bytes)
    {
        lock (gate)
        {
            preparationPendingCount--;
            preparationPendingBytes -= bytes;
            PulseCommandUnderLock();
        }
    }
}
