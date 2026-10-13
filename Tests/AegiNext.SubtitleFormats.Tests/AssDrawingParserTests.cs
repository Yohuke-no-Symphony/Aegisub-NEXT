using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssDrawingParserTests
{
    [Fact]
    public void IndependentMovesCreateClosedContoursWithoutMoveOnlyContours()
    {
        var result = AssDrawingParser.Parse("m 0 0 l 10 0 10 10 m 40 50 l 50 50 50 60 m 200 300");

        Assert.Equal(2, result.Contours.Length);
        Assert.Equal(new(200, 300), result.BoundsMaximum);
        Assert.All(result.Contours, contour => Assert.Equal(3, contour.Nodes.Length));
    }

    [Fact]
    public void NonClosingMoveChangesPenButDoesNotSplitAnActiveContour()
    {
        var result = AssDrawingParser.Parse("m 0 0 l 10 0 n 50 50 l 10 10");
        var contour = Assert.Single(result.Contours);

        Assert.Equal([new ScenePoint(0, 0), new(10, 0), new(10, 10)], contour.Nodes.Select(node => node.Position));
        Assert.Equal(new(50, 50), result.BoundsMaximum);
    }

    [Fact]
    public void NonClosingMoveAffectsTheSourceControlForFollowingCubic()
    {
        var result = AssDrawingParser.Parse("m 0 0 n 10 10 b 20 10 20 20 10 20");
        var contour = Assert.Single(result.Contours);

        Assert.Equal(new(10, 10), contour.Nodes[0].Position);
        Assert.Equal(new(10, 0), contour.Nodes[0].OutHandle);
    }

    [Theory]
    [InlineData("n 0 0 l 10 0 10 10", false)]
    [InlineData("l 10 10 n 0 0 l 10 0 10 10", false)]
    [InlineData("m n 0 0 l 10 0 10 10", true)]
    [InlineData("m 0 0", false)]
    [InlineData("m 0 0 s 10 0 10", false)]
    public void InitialAndIncompleteCommandsFollowLibassTokenRules(string drawing, bool hasGeometry)
    {
        Assert.Equal(hasGeometry, AssDrawingParser.Parse(drawing).HasDrawableGeometry);
    }

    [Fact]
    public void UnknownCharactersAndIncompleteBezierTailAreIgnored()
    {
        var result = AssDrawingParser.Parse("junk! m 0 0 l 10 0 10 10 b 5 5 6 6 ?");
        var contour = Assert.Single(result.Contours);

        Assert.Equal(3, contour.Nodes.Length);
        Assert.Equal(default, contour.Nodes[^1].OutHandle);
    }

    [Fact]
    public void LooseSplineExtensionLooksBackAtThreeExistingTokens()
    {
        var result = AssDrawingParser.Parse("m 0 0 l 10 0 20 10 p 30 10");
        var contour = Assert.Single(result.Contours);

        Assert.Equal(4, contour.Nodes.Length);
        Assert.Equal(new(20, 8.328125), contour.Nodes[^1].Position);
        Assert.Equal(new(-3.328125, -1.65625), contour.Nodes[^1].InHandle);
    }

    [Fact]
    public void SplineClosingTokenSurvivesInterveningCommandsAndRepeatedCloseIsIgnored()
    {
        var closed = AssDrawingParser.Parse("m 0 0 s 10 0 20 10 30 10 l 40 40 c c");
        var once = AssDrawingParser.Parse("m 0 0 s 10 0 20 10 30 10 l 40 40 c");

        Assert.Equal(once.PointCount, closed.PointCount);
        Assert.Equal(once.Contours[0].Nodes.Select(node => (node.Position, node.InHandle, node.OutHandle)),
            closed.Contours[0].Nodes.Select(node => (node.Position, node.InHandle, node.OutHandle)));
        Assert.Equal(40, closed.BoundsMaximum.X);
    }

    [Theory]
    [InlineData(0.0078125, 0)]
    [InlineData(0.0234375, 0.03125)]
    [InlineData(-0.0078125, 0)]
    [InlineData(-0.0234375, -0.03125)]
    public void SourceCoordinatesUseSigned26Point6RoundToEven(double source, double expected)
    {
        var drawing = FormattableString.Invariant($"m {source} 0 l 10 0 10 10");

        Assert.Equal(expected, AssDrawingParser.Parse(drawing).Contours[0].Nodes[0].Position.X);
    }

    [Fact]
    public void ControlBoundsIncludeUnconvertedSplineControls()
    {
        var result = AssDrawingParser.Parse("m -100 -100 s 0 0 10 10 20 20");

        Assert.Equal(new(-100, -100), result.BoundsMinimum);
        Assert.NotEqual(result.BoundsMinimum, result.Contours[0].Nodes[0].Position);
    }

    [Theory]
    [InlineData("m NaN 0 l 10 10")]
    [InlineData("m 0 Infinity l 10 10")]
    [InlineData("m 1e999 0 l 10 10")]
    [InlineData("m 33554432 0 l 10 10")]
    public void UnsafeNumericCoordinatesAreRejected(string drawing)
    {
        Assert.Throws<InvalidDataException>(() => AssDrawingParser.Parse(drawing));
    }

    [Fact]
    public void ExpandedDrawingTokensHaveOneTotalBudget()
    {
        var drawing = "m 0 0 l " + string.Join(' ', Enumerable.Repeat("1 1", 10000));

        Assert.Throws<InvalidDataException>(() => AssDrawingParser.Parse(drawing));
    }
}
