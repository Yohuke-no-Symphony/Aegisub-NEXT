using AegiNext.Koumori;

namespace AegiNext.Desktop.Workspace;

internal sealed class KoumoriSnapCache
{
    private const long BLOCK_SAMPLES = 1024 * KoumoriAnalyzer.HOP_SAMPLES;
    private string? contextIdentity;
    private KoumoriAnalysisRange? completedRange;
    private KoumoriAnalysisRange? failedRange;
    private KoumoriAnalysisRange? cancelledRange;

    internal KoumoriSnapRequest? PendingRequest { get; private set; }
    internal IReadOnlyList<KoumoriBoundary> Boundaries { get; private set; } = [];
    internal string? Error { get; private set; }

    internal bool SetContext(string identity)
    {
        if (contextIdentity == identity)
        {
            return false;
        }

        contextIdentity = identity;
        completedRange = null;
        Boundaries = [];
        CancelPending();
        return true;
    }

    internal bool TryRequest(KoumoriAnalysisRange required, long sampleCount, out KoumoriSnapRequest? request)
    {
        request = null;
        if (Contains(completedRange, required) || Contains(PendingRequest?.Range, required) || Contains(failedRange, required) ||
            Contains(cancelledRange, required))
        {
            return false;
        }

        var first = Math.Max(0, required.StartSample - KoumoriAnalyzer.SAMPLE_RATE) / BLOCK_SAMPLES * BLOCK_SAMPLES;
        var paddedEnd = Math.Min(sampleCount, required.EndSample + KoumoriAnalyzer.SAMPLE_RATE);
        var last = Math.Min(sampleCount, (paddedEnd + BLOCK_SAMPLES - 1) / BLOCK_SAMPLES * BLOCK_SAMPLES);
        request = new(contextIdentity ?? throw new InvalidOperationException("A model snap context is required."), new(first, last));
        PendingRequest = request;
        failedRange = null;
        Error = null;
        return true;
    }

    internal bool Complete(KoumoriSnapRequest request, IReadOnlyList<KoumoriBoundary> boundaries)
    {
        if (!IsCurrent(request))
        {
            return false;
        }

        completedRange = request.Range;
        Boundaries = boundaries.Where(value => value.Sample >= request.Range.StartSample && value.Sample <= request.Range.EndSample).ToArray();
        PendingRequest = null;
        failedRange = null;
        Error = null;
        return true;
    }

    internal bool Fail(KoumoriSnapRequest request, string error)
    {
        if (!IsCurrent(request))
        {
            return false;
        }

        failedRange = request.Range;
        Error = error;
        PendingRequest = null;
        return true;
    }

    internal void CancelPending()
    {
        PendingRequest = null;
        failedRange = null;
        cancelledRange = null;
        Error = null;
    }

    internal bool Cancel(KoumoriSnapRequest request)
    {
        if (!IsCurrent(request))
        {
            return false;
        }

        cancelledRange = request.Range;
        PendingRequest = null;
        failedRange = null;
        Error = null;
        return true;
    }

    internal KoumoriSnapState GetState(KoumoriAnalysisRange required)
    {
        if (Contains(completedRange, required))
        {
            return HasCandidates(required, KoumoriBoundaryKind.Start) || HasCandidates(required, KoumoriBoundaryKind.End)
                ? KoumoriSnapState.Ready : KoumoriSnapState.ReadyEmpty;
        }

        if (Contains(failedRange, required))
        {
            return KoumoriSnapState.Failed;
        }

        return Contains(PendingRequest?.Range, required) ? KoumoriSnapState.Analyzing : KoumoriSnapState.Idle;
    }

    internal bool HasCandidates(KoumoriAnalysisRange required, KoumoriBoundaryKind kind) =>
        Boundaries.Any(value => value.Kind == kind && value.Sample >= required.StartSample && value.Sample <= required.EndSample);

    internal static KoumoriAnalysisRange RequiredRange(double viewStart, double visibleDuration, int maximumMilliseconds, long sampleCount)
    {
        var padding = maximumMilliseconds / 1000d;
        var first = Math.Clamp((long)Math.Floor((viewStart - padding) * KoumoriAnalyzer.SAMPLE_RATE), 0, sampleCount);
        var last = Math.Clamp((long)Math.Ceiling((viewStart + visibleDuration + padding) * KoumoriAnalyzer.SAMPLE_RATE), first, sampleCount);
        return new(first, last);
    }

    private bool IsCurrent(KoumoriSnapRequest request) =>
        ReferenceEquals(PendingRequest, request) && contextIdentity == request.ContextIdentity;

    private static bool Contains(KoumoriAnalysisRange? coverage, KoumoriAnalysisRange required) =>
        coverage is { } range && range.StartSample <= required.StartSample && range.EndSample >= required.EndSample;
}
