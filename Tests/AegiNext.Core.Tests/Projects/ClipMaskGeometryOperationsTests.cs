using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ClipMaskGeometryOperationsTests
{
    [Theory]
    [InlineData(0, 3, 0.5)]
    [InlineData(0, 3, 0.2)]
    [InlineData(2, 3, 0.8)]
    [InlineData(0, 1, 0.3)]
    public void SubdivisionPreservesClosedCurveIdentityTransformAndOtherContours(int segmentIndex, int nodeCount, double splitProgress)
    {
        var nodes = Enumerable.Range(0, nodeCount).Select(index => new MaskNode
        {
            Position = new(100 + index * 150, 200 + index * 40),
            InHandle = new(-80, -60), OutHandle = new(90, -70)
        }).ToImmutableArray();
        var contour = new MaskContour { Nodes = nodes };
        var other = new MaskContour { Nodes = [new() { Position = new(10, 20) }] };
        var mask = new VectorClipMask
        {
            Inverted = true,
            Placement = new() { Pivot = new(0.25, 0.75), Offset = new(31, 43) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(600, 400) },
            Transform = new() { Rotation = 27 }, Contours = [contour, other]
        };
        var changed = ClipMaskGeometryOperations.SubdivideSegment(mask, contour.Id, nodes[segmentIndex].Id, splitProgress);
        Assert.Equal(mask.Transform, changed.Transform);
        Assert.Equal(mask.Placement, changed.Placement);
        Assert.Equal(mask.ReferenceFrame, changed.ReferenceFrame);
        Assert.Equal(mask.Inverted, changed.Inverted);
        Assert.Same(other, changed.Contours[1]);
        var result = changed.Contours[0];
        Assert.Equal(contour.Id, result.Id);
        Assert.Equal(nodeCount + 1, result.Nodes.Length);
        var inserted = result.Nodes[segmentIndex + 1];
        Assert.DoesNotContain(nodes, node => node.Id == inserted.Id);
        Assert.Equal(nodes.Select(node => node.Id), result.Nodes.Where(node => node.Id != inserted.Id).Select(node => node.Id));
        var first = result.Nodes[segmentIndex];
        var next = result.Nodes[(segmentIndex + 2) % result.Nodes.Length];
        for (var sample = 0; sample <= 100; sample++)
        {
            var progress = sample / 100d;
            var expected = Evaluate(nodes[segmentIndex], nodes[(segmentIndex + 1) % nodeCount], progress);
            var actual = progress <= splitProgress ? Evaluate(first, inserted, progress / splitProgress) : Evaluate(inserted, next, (progress - splitProgress) / (1 - splitProgress));
            Assert.Equal(expected.X, actual.X, 8);
            Assert.Equal(expected.Y, actual.Y, 8);
        }
    }

    [Fact]
    public void MissingSegmentAndTotalNodeBudgetFailWithoutChangingTheSource()
    {
        var contour = new MaskContour { Nodes = [new() { Position = new(10, 20) }] };
        var mask = new VectorClipMask { Contours = [contour] };
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipMaskGeometryOperations.SubdivideSegment(mask, Guid.NewGuid(), contour.Nodes[0].Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipMaskGeometryOperations.SubdivideSegment(mask, contour.Id, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipMaskGeometryOperations.SubdivideSegment(mask, contour.Id, contour.Nodes[0].Id, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipMaskGeometryOperations.SubdivideSegment(mask, contour.Id, contour.Nodes[0].Id, double.NaN));
        var other = new MaskContour { Nodes = Enumerable.Range(0, 9999).Select(_ => new MaskNode()).ToImmutableArray() };
        var full = mask with { Contours = [contour, other] };
        Assert.Throws<InvalidOperationException>(() => ClipMaskGeometryOperations.SubdivideSegment(full, contour.Id, contour.Nodes[0].Id));
        Assert.Single(contour.Nodes);
        Assert.Equal(10000, full.Contours.Sum(item => item.Nodes.Length));
    }

    private static ScenePoint Evaluate(MaskNode first, MaskNode next, double t)
    {
        var u = 1 - t;
        return new(
            u * u * u * first.Position.X + 3 * u * u * t * (first.Position.X + first.OutHandle.X) +
            3 * u * t * t * (next.Position.X + next.InHandle.X) + t * t * t * next.Position.X,
            u * u * u * first.Position.Y + 3 * u * u * t * (first.Position.Y + first.OutHandle.Y) +
            3 * u * t * t * (next.Position.Y + next.InHandle.Y) + t * t * t * next.Position.Y);
    }

    [Fact]
    public void RelativeMaskHandlesDoNotInheritTheMotionPathAbsoluteControlPointLimit()
    {
        var first = new MaskNode { Position = new(900_000_000, 0), OutHandle = new(400_000_000, 20) };
        var next = new MaskNode { Position = new(900_000_000, 100), InHandle = new(-400_000_000, -20) };
        var contour = new MaskContour { Nodes = [first, next] };
        var mask = new VectorClipMask { Contours = [contour] };
        var changed = ClipMaskGeometryOperations.SubdivideSegment(mask, contour.Id, first.Id);
        Assert.Equal(Evaluate(first, next, 0.5), changed.Contours[0].Nodes[1].Position);
    }
}
