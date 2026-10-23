using AegiNext.Desktop.Workspace;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class KoumoriSnapCacheTests
{
    [Fact]
    public void NearbyViewportChangesReuseThePendingAlignedRange()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);

        Assert.True(cache.TryRequest(required, 1600000, out var first));
        Assert.Equal(0, first!.Range.StartSample % (1024 * 160));
        Assert.Equal(0, first.Range.EndSample % (1024 * 160));
        Assert.Equal(KoumoriSnapState.Analyzing, cache.GetState(required));
        for (var index = 1; index <= 50; index++)
        {
            var moved = KoumoriSnapCache.RequiredRange(12 + index * 0.001, 3.01, 250, 1600000);
            Assert.False(cache.TryRequest(moved, 1600000, out _));
            Assert.Same(first, cache.PendingRequest);
        }
    }

    [Fact]
    public void CompletedCoverageReusesCandidatesWithoutSchedulingAnotherAnalysis()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var request));
        var boundary = new KoumoriBoundary(200000, KoumoriBoundaryKind.Start, 0.9f, 0);
        Assert.True(cache.Complete(request!, [boundary]));

        var moved = KoumoriSnapCache.RequiredRange(12.5, 3.2, 250, 1600000);
        Assert.False(cache.TryRequest(moved, 1600000, out _));
        Assert.Null(cache.PendingRequest);
        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(moved));
        Assert.Same(boundary, Assert.Single(cache.Boundaries));
        Assert.True(cache.HasCandidates(moved, KoumoriBoundaryKind.Start));
        Assert.False(cache.HasCandidates(moved, KoumoriBoundaryKind.End));
    }

    [Fact]
    public void ExtendingCoverageKeepsPreviousCandidatesUntilTheReplacementCompletes()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var original = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(original, 1600000, out var first));
        var boundary = new KoumoriBoundary(200000, KoumoriBoundaryKind.Start, 0.9f, 0);
        Assert.True(cache.Complete(first!, [boundary]));

        var expanded = KoumoriSnapCache.RequiredRange(12, 15, 250, 1600000);
        Assert.True(cache.TryRequest(expanded, 1600000, out var second));
        Assert.Equal(KoumoriSnapState.Analyzing, cache.GetState(expanded));
        Assert.Same(boundary, Assert.Single(cache.Boundaries));
        Assert.True(cache.HasCandidates(expanded, KoumoriBoundaryKind.Start));
        Assert.True(cache.Complete(second!, []));
        Assert.Empty(cache.Boundaries);
        Assert.Equal(KoumoriSnapState.ReadyEmpty, cache.GetState(expanded));
    }

    [Fact]
    public void ContextChangesImmediatelyClearCandidatesAndRejectLateCompletions()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("old-media:model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var old));
        Assert.True(cache.Complete(old!, [new(200000, KoumoriBoundaryKind.Start, 0.9f, 0)]));
        Assert.True(cache.TryRequest(KoumoriSnapCache.RequiredRange(30, 3, 250, 1600000), 1600000, out var pending));

        Assert.True(cache.SetContext("new-media:model"));
        Assert.Empty(cache.Boundaries);
        Assert.Null(cache.PendingRequest);
        Assert.False(cache.Complete(pending!, [new(500000, KoumoriBoundaryKind.End, 0.9f, 0)]));
        Assert.Equal(KoumoriSnapState.Idle, cache.GetState(required));
        Assert.True(cache.TryRequest(required, 1600000, out var current));
        Assert.False(cache.Fail(pending!, "stale failure"));
        Assert.Same(current, cache.PendingRequest);
    }

    [Fact]
    public void EmptyResultsAndOffscreenCandidatesHaveAnExplicitEmptyState()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var request));
        Assert.True(cache.Complete(request!, [new(300000, KoumoriBoundaryKind.End, 0.9f, 0)]));

        Assert.Equal(KoumoriSnapState.ReadyEmpty, cache.GetState(required));
        Assert.False(cache.HasCandidates(required, KoumoriBoundaryKind.Start));
        Assert.False(cache.HasCandidates(required, KoumoriBoundaryKind.End));
        Assert.False(cache.TryRequest(required, 1600000, out _));
    }

    [Fact]
    public void FailedExpansionRetainsUsableCandidatesAndDoesNotRetryOnEveryTick()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var original = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(original, 1600000, out var first));
        Assert.True(cache.Complete(first!, [new(200000, KoumoriBoundaryKind.Start, 0.9f, 0)]));
        var expanded = KoumoriSnapCache.RequiredRange(12, 15, 250, 1600000);
        Assert.True(cache.TryRequest(expanded, 1600000, out var second));

        Assert.True(cache.Fail(second!, "model unavailable"));
        Assert.Equal(KoumoriSnapState.Failed, cache.GetState(expanded));
        Assert.Equal("model unavailable", cache.Error);
        Assert.Single(cache.Boundaries);
        Assert.False(cache.TryRequest(expanded, 1600000, out _));
        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(original));
        cache.CancelPending();
        Assert.True(cache.TryRequest(expanded, 1600000, out _));
    }

    [Fact]
    public void RequiredAndAlignedRangesStayInsideMediaAtBothEnds()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var required = KoumoriSnapCache.RequiredRange(0, 10, 250, 16000);
        Assert.Equal(new(0, 16000), required);
        Assert.True(cache.TryRequest(required, 16000, out var request));
        Assert.Equal(new(0, 16000), request!.Range);
    }

    [Fact]
    public void SupersededViewportCallbacksCannotCompleteOrFailTheNewRequest()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        Assert.True(cache.TryRequest(KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000), 1600000, out var superseded));
        var currentRange = KoumoriSnapCache.RequiredRange(30, 3, 250, 1600000);
        Assert.True(cache.TryRequest(currentRange, 1600000, out var current));

        Assert.False(cache.Complete(superseded!, [new(200000, KoumoriBoundaryKind.Start, 0.9f, 0)]));
        Assert.False(cache.Fail(superseded!, "late failure"));
        Assert.Same(current, cache.PendingRequest);
        Assert.Null(cache.Error);
        Assert.Empty(cache.Boundaries);
        Assert.Equal(KoumoriSnapState.Analyzing, cache.GetState(currentRange));
        Assert.True(cache.Complete(current!, [new(500000, KoumoriBoundaryKind.End, 0.9f, 0)]));
        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(currentRange));
    }

    [Fact]
    public void CanceledExpansionKeepsCompletedCoverageAndRejectsItsLateCallbacks()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var original = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(original, 1600000, out var completed));
        var boundary = new KoumoriBoundary(200000, KoumoriBoundaryKind.Start, 0.9f, 0);
        Assert.True(cache.Complete(completed!, [boundary]));
        var expanded = KoumoriSnapCache.RequiredRange(12, 15, 250, 1600000);
        Assert.True(cache.TryRequest(expanded, 1600000, out var canceled));

        cache.CancelPending();

        Assert.Equal(KoumoriSnapState.Ready, cache.GetState(original));
        Assert.Same(boundary, Assert.Single(cache.Boundaries));
        Assert.True(cache.TryRequest(expanded, 1600000, out var retry));
        Assert.False(cache.Complete(canceled!, []));
        Assert.False(cache.Fail(canceled!, "canceled failure"));
        Assert.Same(retry, cache.PendingRequest);
        Assert.True(cache.Complete(retry!, []));
        Assert.Equal(KoumoriSnapState.ReadyEmpty, cache.GetState(expanded));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFirstTerminalCallbackCannotBeOverwritten(bool succeeds)
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("media:model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var request));
        if (succeeds)
        {
            Assert.True(cache.Complete(request!, []));
            Assert.False(cache.Fail(request!, "late failure"));
            Assert.Equal(KoumoriSnapState.ReadyEmpty, cache.GetState(required));
            Assert.Null(cache.Error);
        }
        else
        {
            Assert.True(cache.Fail(request!, "first failure"));
            Assert.False(cache.Complete(request!, []));
            Assert.Equal(KoumoriSnapState.Failed, cache.GetState(required));
            Assert.Equal("first failure", cache.Error);
        }
    }

    [Fact]
    public void ReturningToAnEarlierContextDoesNotReviveItsCanceledRequest()
    {
        var cache = new KoumoriSnapCache();
        cache.SetContext("first-model");
        var required = KoumoriSnapCache.RequiredRange(12, 3, 250, 1600000);
        Assert.True(cache.TryRequest(required, 1600000, out var canceled));
        cache.SetContext("second-model");
        cache.SetContext("first-model");
        Assert.True(cache.TryRequest(required, 1600000, out var current));

        Assert.False(cache.Complete(canceled!, [new(200000, KoumoriBoundaryKind.Start, 0.9f, 0)]));
        Assert.False(cache.Fail(canceled!, "late failure"));
        Assert.Same(current, cache.PendingRequest);
        Assert.Empty(cache.Boundaries);
        Assert.Null(cache.Error);
    }
}
