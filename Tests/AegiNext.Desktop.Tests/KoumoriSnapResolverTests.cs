using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriSnapResolverTests
{
    [Fact]
    public void UsesOnlyTheRequestedEdgeKind()
    {
        var input = new MediaTime(1);
        KoumoriBoundary[] candidates = [new(16160, KoumoriBoundaryKind.End, 0.9f, 0), new(16320, KoumoriBoundaryKind.Start, 0.8f, 0)];
        var result = KoumoriSnapResolver.Resolve(input, KoumoriBoundaryKind.Start, candidates, 100, 250, new(input, null));
        Assert.Equal(new MediaTime(102, 100), result.Value);
    }

    [Fact]
    public void OrdinaryBoundaryWinsAnEqualDistanceTie()
    {
        var input = new MediaTime(1);
        var ordinary = new TimelineSnapResult(new(98, 100), new(98, 100));
        var result = KoumoriSnapResolver.Resolve(input, KoumoriBoundaryKind.Start,
            [new(16320, KoumoriBoundaryKind.Start, 1, 0)], 100, 250, ordinary);
        Assert.Equal(ordinary, result);
    }

    [Theory]
    [InlineData(20, 240, true)]
    [InlineData(20, 260, false)]
    [InlineData(100, 70, true)]
    [InlineData(100, 90, false)]
    public void RespectsBothTheMillisecondLimitAndEightDipTolerance(double pixelsPerSecond, int offsetMilliseconds, bool expectedSnap)
    {
        var input = new MediaTime(1);
        var candidate = new KoumoriBoundary(16000 + offsetMilliseconds * 16, KoumoriBoundaryKind.End, 0.8f, 0);
        var result = KoumoriSnapResolver.Resolve(input, KoumoriBoundaryKind.End, [candidate], pixelsPerSecond, 250, new(input, null));
        Assert.Equal(expectedSnap, result.Boundary.HasValue);
        Assert.Equal(expectedSnap ? candidate.Time : input, result.Value);
    }
}
