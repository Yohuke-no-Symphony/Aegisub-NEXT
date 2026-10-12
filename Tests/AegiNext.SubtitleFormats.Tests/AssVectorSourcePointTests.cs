using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssVectorSourcePointTests
{
    [Fact]
    public void ClosedSplineUsesTheLastMoveSourcePointAfterAnExistingSegment()
    {
        var mask = AssMaskDrawing.Parse("m 10 10 l 20 10 n 50 50 80 80 s 30 10 30 30 10 30 c", false, 1, 1);
        var nodes = Assert.Single(mask.Contours).Nodes;

        Assert.Equal(6, nodes.Length);
        Assert.Equal(new ScenePoint(10, 10), nodes[0].Position);
        Assert.Equal(new ScenePoint(20, 10), nodes[1].Position);
        Assert.Equal(25, nodes[3].Position.X, 8);
        Assert.Equal(230d / 6, nodes[3].Position.Y, 8);
        Assert.Equal(new ScenePoint(60, 60), nodes[4].Position);
    }

    [Fact]
    public void ConsecutiveSplinesUseSourceControlPointsInsteadOfEvaluatedEndpoints()
    {
        var mask = AssMaskDrawing.Parse("m 0 0 s 60 0 60 60 0 60 s -60 60 -60 0 0 0 c", false, 1, 1);
        var nodes = Assert.Single(mask.Contours).Nodes;

        Assert.Equal(6, nodes.Length);
        Assert.Equal(new ScenePoint(50, 50), nodes[1].Position);
        Assert.Equal(new ScenePoint(-10, 50), nodes[4].Position);
        Assert.Equal(new ScenePoint(-50, 50), nodes[5].Position);
    }

    [Fact]
    public void MoveBeforeAnUnclosedSplineDoesNotInsertAnExtraConnection()
    {
        var withMove = AssMaskDrawing.Parse("m 10 10 l 20 10 n 80 80 s 30 10 30 30 10 30", false, 1, 1);
        var withoutMove = AssMaskDrawing.Parse("m 10 10 l 20 10 s 30 10 30 30 10 30", false, 1, 1);

        Assert.Equal(withoutMove.Contours[0].Nodes.Select(node => (node.Position, node.InHandle, node.OutHandle)),
            withMove.Contours[0].Nodes.Select(node => (node.Position, node.InHandle, node.OutHandle)));
    }

    [Theory]
    [InlineData("m 0 0 n 30 40 l 60 40 60 60", 30, 40)]
    [InlineData("m 0 0 n 10 20 n 30 40 l 60 40 60 60", 30, 40)]
    [InlineData("m 0 0 l 10 0 n 30 40 l 60 40 60 60", 0, 0)]
    [InlineData("m 0 0 l 10 0 n 30 40 b 20 0 40 20 60 60", 0, 0)]
    public void NonClosingMoveKeepsItsExistingContourAndOnlyChangesAnUnstartedOrigin(string drawing, double x, double y)
    {
        var nodes = Assert.Single(AssMaskDrawing.Parse(drawing, false, 1, 1).Contours).Nodes;

        Assert.Equal(new ScenePoint(x, y), nodes[0].Position);
        Assert.Equal(new ScenePoint(60, 60), nodes[^1].Position);
        if (x == 0)
        {
            Assert.Equal(new ScenePoint(10, 0), nodes[1].Position);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0.5)]
    public void SplineExtensionsAndClosingPointsUpdateTheNextSplineSource(bool inverted, double scale)
    {
        var mask = AssMaskDrawing.Parse("m 0 0 s 60 0 60 60 0 60 p -60 60 c s -60 0 0 0 60 0 c", inverted, scale, scale);
        var nodes = Assert.Single(mask.Contours).Nodes;

        Assert.Equal(inverted, mask.Inverted);
        Assert.Equal(10, nodes.Length);
        Assert.Equal(40 * scale, nodes[8].Position.X, 8);
        Assert.Equal(40 * scale, nodes[8].Position.Y, 8);
    }
}
