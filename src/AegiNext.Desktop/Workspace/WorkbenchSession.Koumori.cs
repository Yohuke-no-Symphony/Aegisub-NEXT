using System.Security.Cryptography;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Koumori;
using AegiNext.Koumori;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly SemaphoreSlim koumoriGate = new(1, 1);
    private readonly KoumoriSnapCache koumoriSnapCache = new();
    private CancellationTokenSource? koumoriSnapCancellation;
    private Task koumoriSnapCompletion = Task.CompletedTask;
    private KoumoriAnalyzer? koumoriAnalyzer;
    private string? koumoriModelIdentity;
    private KoumoriMediaSource? koumoriAudioSource;
    private string? koumoriAudioIdentity;
    private long koumoriAudioEpoch;
    private int koumoriForegroundRequests;
    private CancellationTokenSource koumoriContextCancellation = new();
    private string? koumoriContextIdentity;

    internal bool CanAnalyzeKoumori => !closing && preferences.Koumori.Enabled &&
        controller.MediaInfo?.AudioStreamIndex is not null && controller.Snapshot.FilePath is not null &&
        !controller.Snapshot.IsOpening && controller.Snapshot.Error is null &&
        controller.Snapshot.Duration is { } duration && duration > MediaTime.Zero;

    private string KoumoriModelDirectory => preferences.Koumori.ResolvedModelDirectory;

    internal async Task<KoumoriPreview> AnalyzeKoumoriAsync(CancellationToken cancellationToken, IProgress<double>? progress)
    {
        if (!CanAnalyzeKoumori || !TryCommitDrafts())
        {
            throw new InvalidOperationException(Localization.Get("Koumori.Unavailable"));
        }
        koumoriSnapCancellation?.Cancel();
        koumoriSnapCache.CancelPending();
        koumoriForegroundRequests++;
        try
        {
            return await applicationContext.Tasks.Submit(new KoumoriAnalysisTask(this, cancellationToken, progress)).Completion;
        }
        finally
        {
            koumoriForegroundRequests--;
            QueueKoumoriSnapAnalysis();
        }
    }

    internal async Task<KoumoriPreview> ExecuteKoumoriAnalysisAsync(AegiTaskExecutionContext context,
        CancellationToken cancellationToken, IProgress<double>? progress)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken,
            cancellationToken, ProjectOperationsToken);
        return await AnalyzeKoumoriCoreAsync(null, new Progress<KoumoriAnalysisProgress>(value =>
        {
            context.ReportProgress(new("Koumori.Analyzing", value.CompletedFrames, value.TotalFrames));
            progress?.Report(value.Fraction);
        }), lifetime.Token);
    }

    private async Task<KoumoriPreview> AnalyzeKoumoriCoreAsync(KoumoriAnalysisRange? range,
        IProgress<KoumoriAnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        RefreshKoumoriAnalysisContext();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, koumoriContextCancellation.Token);
        cancellationToken = lifetime.Token;
        if (!CanAnalyzeKoumori)
        {
            throw new InvalidOperationException(Localization.Get("Koumori.Unavailable"));
        }
        var snapshot = controller.Snapshot;
        var path = snapshot.FilePath!;
        var media = controller.MediaInfo!;
        var origin = media.Start ?? MediaTime.Zero;
        var duration = snapshot.Duration!.Value;
        var source = editor.Snapshot;
        var generation = projectGeneration;
        var directory = KoumoriModelDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(Localization.Get("Koumori.ManualDirectoryRequired"));
        }
        var stamp = new FileInfo(path);
        var length = stamp.Length;
        var modified = stamp.LastWriteTimeUtc;
        await koumoriGate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json"), cancellationToken);
            var identity = directory + ":" + Convert.ToHexString(SHA256.HashData(manifest));
            if (koumoriAnalyzer is null || identity != koumoriModelIdentity)
            {
                var loaded = await KoumoriAnalyzer.LoadAsync(directory, cancellationToken);
                if (koumoriAnalyzer is not null)
                {
                    await koumoriAnalyzer.DisposeAsync();
                }
                koumoriAnalyzer = loaded;
                koumoriModelIdentity = identity;
            }
            var analyzer = koumoriAnalyzer;
            var audioIdentity = FormattableString.Invariant($"{KoumoriMediaSource.PCM_CONTRACT}|{stamp.FullName}|{media.AudioStreamIndex}|{origin}|{duration}|{length}|{modified.Ticks}|{snapshot.Epoch}");
            if (koumoriAudioSource is null || koumoriAudioSource.IsCanceled || audioIdentity != koumoriAudioIdentity)
            {
                await ReleaseKoumoriAudioSourceAsync();
                var decoder = await Task.Run(() => FfmpegAudioDecoder.Open(path, media.AudioStreamIndex!.Value,
                    new(KoumoriAnalyzer.SAMPLE_RATE, 1), cancellationToken,
                    AudioTimestampPolicy.FFMPEG_ASYNC_ONE), cancellationToken);
                var cacheName = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(audioIdentity))) + ".f32";
                try
                {
                    koumoriAudioSource = new(decoder,
                        checked((long)Math.Round(ToSeconds(duration) * KoumoriAnalyzer.SAMPLE_RATE)), origin,
                        Path.Combine(ScratchDirectory, "koumori", cacheName));
                }
                catch
                {
                    decoder.Dispose();
                    throw;
                }
                koumoriAudioIdentity = audioIdentity;
                koumoriAudioEpoch = snapshot.Epoch;
            }
            var audio = koumoriAudioSource;
            if (range is { } requested)
            {
                range = new(Math.Clamp(requested.StartSample, 0, audio.SampleCount), Math.Clamp(requested.EndSample, 0, audio.SampleCount));
            }
            var result = await analyzer.AnalyzeAsync(audio, range, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            stamp.Refresh();
            if (!stamp.Exists || stamp.Length != length || stamp.LastWriteTimeUtc != modified || closing ||
                generation != projectGeneration || snapshot.Epoch != controller.Snapshot.Epoch ||
                path != controller.Snapshot.FilePath || directory != KoumoriModelDirectory || identity != ReadKoumoriModelIdentity(directory))
            {
                throw new OperationCanceledException(Localization.Get("Koumori.ContextChanged"), cancellationToken);
            }
            return new(source, generation, snapshot.Epoch, path, origin, duration, directory, identity, length, modified, result);
        }
        finally
        {
            koumoriGate.Release();
        }
    }

    internal bool IsKoumoriPreviewCurrent(KoumoriPreview preview) => CanAnalyzeKoumori && !IsProjectBusy &&
        preview.Generation == projectGeneration && preview.MediaEpoch == controller.Snapshot.Epoch &&
        preview.MediaPath == controller.Snapshot.FilePath && preview.ModelDirectory == KoumoriModelDirectory &&
        ReferenceEquals(preview.Document, editor.Snapshot) && !HasProjectDrafts && SceneEditing.GestureTarget is null &&
        IsKoumoriPreviewFilesCurrent(preview);

    private static string ReadKoumoriModelIdentity(string directory)
    {
        using var stream = File.OpenRead(Path.Combine(directory, "manifest.json"));
        return directory + ":" + Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsKoumoriPreviewFilesCurrent(KoumoriPreview preview)
    {
        try
        {
            var file = new FileInfo(preview.MediaPath);
            return file.Exists && file.Length == preview.FileLength && file.LastWriteTimeUtc == preview.LastWriteTimeUtc &&
                ReadKoumoriModelIdentity(preview.ModelDirectory) == preview.ModelIdentity;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string KoumoriModelStamp()
    {
        if (string.IsNullOrWhiteSpace(KoumoriModelDirectory))
        {
            return "unconfigured";
        }
        try
        {
            var file = new FileInfo(Path.Combine(KoumoriModelDirectory, "manifest.json"));
            return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "unavailable";
        }
    }

    internal int ApplyKoumoriPreview(KoumoriPreview preview, IReadOnlyList<MediaTimeRange> intervals)
    {
        if (!IsKoumoriPreviewCurrent(preview))
        {
            throw new InvalidOperationException(Localization.Get("Koumori.ContextChanged"));
        }
        if (intervals.Any(value => value.Start < MediaTime.Zero || value.End > preview.Duration))
        {
            throw new InvalidDataException(Localization.Get("Koumori.InvalidInterval"));
        }
        ViewModel.CancelGestures();
        InvalidateTimingSession();
        var result = KoumoriProjectBuilder.Add(preview.Document, intervals, Localization.Get("Koumori.Track"));
        editor.Apply("Automatic subtitle timing", _ => result);
        return intervals.Count;
    }

    internal async Task AuditionKoumoriAsync(KoumoriPreview preview, MediaTimeRange interval, CancellationToken cancellationToken)
    {
        if (!IsKoumoriPreviewCurrent(preview))
        {
            throw new InvalidOperationException(Localization.Get("Koumori.ContextChanged"));
        }
        await controller.PlayRangeAsync(preview.Origin + interval.Start, preview.Origin + interval.End, false, cancellationToken);
    }

    private void QueueKoumoriSnapAnalysis()
    {
        if (closing)
        {
            return;
        }
        RefreshKoumoriAnalysisContext();
        var timeline = ViewModel.Timeline;
        timeline.IsKoumoriAvailable = preferences.Koumori.Enabled;
        timeline.KoumoriMaximumSnapMilliseconds = preferences.Koumori.MaximumSnapMilliseconds;
        if (!CanAnalyzeKoumori || (!preferences.Koumori.SnapEnabled && !timeline.IsClassicTimingEnabled &&
            koumoriPreparationRequest is null))
        {
            koumoriSnapCancellation?.Cancel();
            koumoriSnapCache.CancelPending();
            timeline.KoumoriBoundaries = [];
            timeline.SetKoumoriSnapStatus(KoumoriSnapState.Idle);
            return;
        }
        var sampleCount = checked((long)Math.Round(ToSeconds(controller.Snapshot.Duration!.Value) * KoumoriAnalyzer.SAMPLE_RATE));
        var required = KoumoriSnapCache.RequiredRange(timeline.ViewStart, timeline.VisibleDuration,
            preferences.Koumori.MaximumSnapMilliseconds, sampleCount);
        if (koumoriForegroundRequests > 0)
        {
            PublishKoumoriSnapState(required, true);
            return;
        }
        if (!koumoriSnapCache.TryRequest(required, sampleCount, out var request))
        {
            PublishKoumoriSnapState(required);
            return;
        }
        koumoriSnapCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ProjectOperationsToken, koumoriContextCancellation.Token);
        koumoriSnapCancellation = cancellation;
        PublishKoumoriSnapState(required);
        var operation = UpdateKoumoriSnapAsync(request!, cancellation);
        koumoriSnapCompletion = koumoriSnapCompletion.IsCompleted ? operation : Task.WhenAll(koumoriSnapCompletion, operation);
    }

    private void PublishKoumoriSnapState(KoumoriAnalysisRange required, bool foreground = false)
    {
        var timeline = ViewModel.Timeline;
        timeline.KoumoriBoundaries = koumoriSnapCache.Boundaries;
        var state = koumoriSnapCache.GetState(required);
        if (foreground && state == KoumoriSnapState.Idle)
        {
            state = KoumoriSnapState.Analyzing;
        }
        timeline.SetKoumoriSnapStatus(state,
            koumoriSnapCache.HasCandidates(required, KoumoriBoundaryKind.Start),
            koumoriSnapCache.HasCandidates(required, KoumoriBoundaryKind.End),
            koumoriSnapCache.Error);
    }

    private void RefreshKoumoriAnalysisContext()
    {
        var snapshot = controller.Snapshot;
        if (koumoriAudioSource is not null && snapshot.Epoch != koumoriAudioEpoch)
        {
            koumoriAudioSource.Cancel();
        }
        var modelStamp = preferences.Koumori.Enabled ? KoumoriModelStamp() : "disabled";
        var identity = $"{projectGeneration}:{snapshot.Epoch}:{snapshot.FilePath}:{snapshot.Duration}:{KoumoriModelDirectory}:{modelStamp}:{preferences.Koumori.Enabled}";
        if (identity == koumoriContextIdentity)
        {
            return;
        }
        koumoriContextIdentity = identity;
        koumoriPreparationRequest = null;
        koumoriSnapCancellation?.Cancel();
        koumoriContextCancellation.Cancel();
        koumoriContextCancellation.Dispose();
        koumoriContextCancellation = new();
        koumoriSnapCache.SetContext(identity);
        ViewModel.Timeline.KoumoriBoundaries = [];
        ViewModel.Timeline.SetKoumoriSnapStatus(KoumoriSnapState.Idle);
        ViewModel.CancelGestures();
    }

    private async ValueTask ReleaseKoumoriAudioSourceAsync()
    {
        var source = koumoriAudioSource;
        koumoriAudioSource = null;
        koumoriAudioIdentity = null;
        if (source is not null)
        {
            await source.DisposeAsync();
        }
    }

    private async Task UpdateKoumoriSnapAsync(KoumoriSnapRequest request, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(350, cancellation.Token);
            var preview = await AnalyzeKoumoriCoreAsync(request.Range, null, cancellation.Token);
            if (closing || !koumoriSnapCache.Complete(request, preview.Result.Boundaries))
            {
                return;
            }
            QueueKoumoriSnapAnalysis();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(koumoriSnapCache.PendingRequest, request))
            {
                koumoriSnapCache.CancelPending();
            }
        }
        catch (Exception error)
        {
            if (!closing && koumoriSnapCache.Fail(request, error.Message))
            {
                QueueKoumoriSnapAnalysis();
            }
        }
        finally
        {
            if (ReferenceEquals(koumoriSnapCancellation, cancellation))
            {
                koumoriSnapCancellation = null;
            }
            cancellation.Dispose();
        }
    }
}
