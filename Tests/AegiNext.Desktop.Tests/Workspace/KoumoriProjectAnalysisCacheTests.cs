using AegiNext.Desktop.Workspace;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class KoumoriProjectAnalysisCacheTests
{
    [Fact]
    public void WholeAudioPreparationKeepsCandidatesAvailableAcrossViewportChanges()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("project:media:model");
        Assert.True(cache.TryRequest(new(0, 1600000), 1600000, out var request));
        KoumoriBoundary[] boundaries = [new(16000, KoumoriBoundaryKind.Start, 0.9f, 0), new(1500000, KoumoriBoundaryKind.End, 0.9f, 1)];

        Assert.True(cache.Complete(request!, boundaries));

        var start = KoumoriSnapCache.RequiredRange(0, 2, 250, 1600000);
        var end = KoumoriSnapCache.RequiredRange(92, 4, 250, 1600000);
        Assert.False(cache.TryRequest(start, 1600000, out _));
        Assert.False(cache.TryRequest(end, 1600000, out _));
        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(start));
        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(end));
        Assert.True(cache.HasCandidates(start, KoumoriBoundaryKind.Start));
        Assert.True(cache.HasCandidates(end, KoumoriBoundaryKind.End));
        Assert.Equal(boundaries, cache.Boundaries);
    }

    [Fact]
    public void CancellingWholeAudioPreventsAutomaticRetryUntilExplicitReset()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("project:media:model");
        Assert.True(cache.TryRequest(new(0, 1600000), 1600000, out var request));

        Assert.True(cache.Cancel(request!));

        var required = KoumoriSnapCache.RequiredRange(20, 5, 250, 1600000);
        Assert.Equal(KoumoriSnapState.Idle, cache.GetState(required));
        Assert.False(cache.TryRequest(required, 1600000, out _));
        Assert.False(cache.Complete(request!, [new(350000, KoumoriBoundaryKind.Start, 0.9f, 0)]));
        Assert.False(cache.Fail(request!, "late failure"));
        cache.CancelPending();
        Assert.True(cache.TryRequest(required, 1600000, out _));
    }

    [Fact]
    public void LateCancellationDoesNotSuppressTheReplacementProjectRequest()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("old-project");
        Assert.True(cache.TryRequest(new(0, 1600000), 1600000, out var old));
        cache.SetContext("new-project");
        var required = KoumoriSnapCache.RequiredRange(20, 5, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var current));

        Assert.False(cache.Cancel(old!));

        Assert.Same(current, cache.PendingRequest);
        Assert.Equal(KoumoriSnapState.Analyzing, cache.GetState(required));
    }
}
