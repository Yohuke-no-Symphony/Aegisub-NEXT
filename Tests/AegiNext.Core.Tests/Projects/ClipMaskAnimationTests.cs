using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class ClipMaskAnimationTests
{
    [Fact]
    public void NodesAndRelativeHandlesEvaluateIndependentlyFromSubtitleTransforms()
    {
        var first = new MaskNode { Position = new(10, 20), InHandle = new(-2, 0) };
        var second = new MaskNode { Position = new(30, 40) };
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [first, second] }],
            Placement = new() { Pivot = new(0.5, 0.5), Offset = new(20, 30) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(20, 20) }
        };
        var layer = new ProjectLayer
        {
            Transform = new(X: 900, Y: 800, ScaleX: 2, Rotation: 60), Mask = mask,
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), [new(new(0), new ScenePoint(10, 20)), new(new(2), new ScenePoint(20, 40))]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id), [new(new(0), new ScenePoint(30, 40)), new(new(2), new ScenePoint(50, 60))]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_IN_HANDLE, first.Id), [new(new(0), new ScenePoint(-2, 0)), new(new(2), new ScenePoint(-6, 8))]),
                new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(20, 10))])
            ]
        };
        var evaluated = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(1)));
        Assert.Equal(new ScenePoint(15, 30), evaluated.Contours[0].Nodes[0].Position);
        Assert.Equal(new ScenePoint(40, 50), evaluated.Contours[0].Nodes[1].Position);
        Assert.Equal(new ScenePoint(-4, 4), evaluated.Contours[0].Nodes[0].InHandle);
        Assert.Equal(new ScenePoint(10, 5), evaluated.Transform.Position);
        Assert.Equal(mask.Placement, evaluated.Placement);
        Assert.Equal(mask.ReferenceFrame, evaluated.ReferenceFrame);
        Assert.Equal(first.Id, evaluated.Contours[0].Nodes[0].Id);
        Assert.Equal(mask.Contours[0].Id, evaluated.Contours[0].Id);
        Assert.Equal(new ScenePoint(10, 20), first.Position);
    }

    [Fact]
    public void SceneReturnsTheEvaluatedRectangleMaskAlongsideOrdinaryAnimation()
    {
        var line = new SubtitleLine { End = new(4) };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 80) },
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(4), new ScenePoint(100, 50))]),
                new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, [new(new(0), new ScenePoint(0, 0)), new(new(4), new ScenePoint(40, 20))])
            ]
        };
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(new ProjectDocument { Subtitles = [line], Layers = [layer] }, new(2)));
        Assert.Equal(new ScenePoint(50, 25), evaluated.Transform.Position);
        Assert.Equal(new ScenePoint(20, 10), Assert.IsType<RectangleClipMask>(evaluated.Mask).TopLeft);
    }

    [Theory]
    [InlineData(0.125)]
    [InlineData(1.75)]
    [InlineData(12.5)]
    public void PowerCroppingRetainsEveryOriginalSampleAndExponent(double exponent)
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION,
            [new(new(0), 12, KeyframeInterpolation.POWER) { Exponent = exponent }, new(new(8), 212)]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Start = new(1), End = new(7), AnimationOffset = new(1), Tracks = [track] };
        var clipped = LayerAnimationTiming.Clip(LayerAnimationTiming.Clip(layer) with { Start = new(2), End = new(6), AnimationOffset = new(2) });
        Assert.Equal(exponent, clipped.Tracks[0].Keyframes[0].Exponent);
        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(40 + index, 20);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, time), SceneEvaluator.EvaluateScalarTrack(clipped.Tracks[0], time), 9);
        }
    }

    [Fact]
    public void OrderedTransformsBlendTheCurrentValueInSourceOrder()
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION, [])
        {
            InitialValue = 0,
            Transforms = [new(Guid.NewGuid(), new(0), new(4), 100, 2), new(Guid.NewGuid(), new(1), new(3), 200)]
        };
        Assert.Equal(112.5, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 10);
        Assert.Equal(200, SceneEvaluator.EvaluateScalarTrack(track, new(5)));
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(-1)));
        var reversed = track with { Transforms = track.Transforms.Reverse().ToImmutableArray() };
        Assert.Equal(100, SceneEvaluator.EvaluateScalarTrack(reversed, new(2)), 10);
        var instant = track with { Transforms = [new(Guid.NewGuid(), new(1), new(1), 50)] };
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(instant, new(999, 1000)));
        Assert.Equal(50, SceneEvaluator.EvaluateScalarTrack(instant, new(1)));
    }

    [Fact]
    public void OrderedTransformsRetainTheirPhaseOnCropAndScaleBothTimesOnStretch()
    {
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(-1), new(4), 100, 0.75);
        var track = new AnimationTrack(AnimationProperty.ROTATION, []) { InitialValue = 0, Transforms = [operation] };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Start = new(10), End = new(14), Tracks = [track] };
        var crop = LayerAnimationTiming.Retime(layer, new(11), new(13), TimelineEditMode.CROP);
        Assert.Same(track, Assert.Single(crop.Tracks));
        Assert.Equal(new MediaTime(1), crop.AnimationOffset);
        Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, new(2)), SceneEvaluator.EvaluateScalarTrack(crop.Tracks[0], new(2)));
        var stretched = LayerAnimationTiming.Retime(crop, new(11), new(15), TimelineEditMode.STRETCH);
        Assert.Equal(new MediaTime(2), stretched.AnimationOffset);
        Assert.Equal(new MediaTime(-2), stretched.Tracks[0].Transforms[0].Start);
        Assert.Equal(new MediaTime(8), stretched.Tracks[0].Transforms[0].End);
        Assert.Equal(operation.Id, stretched.Tracks[0].Transforms[0].Id);
        Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, new(2)), SceneEvaluator.EvaluateScalarTrack(stretched.Tracks[0], new(4)), 10);
    }

    [Fact]
    public void TargetValidationRejectsMissingNodesAndMixedRepresentations()
    {
        var node = new MaskNode();
        var mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] };
        var line = new SubtitleLine();
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End, Mask = mask };
        ProjectDocument With(AnimationTrack track) => new() { Subtitles = [line], Layers = [layer with { Tracks = [track] }] };
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id);
        var track = new AnimationTrack(target, [new(new(0), new ScenePoint(1, 2))]);
        ProjectValidator.Validate(With(track));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(With(track with { Target = target with { NodeId = Guid.NewGuid() } })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(With(track with { Target = new(AnimationProperty.POSITION, node.Id) })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(With(track with
        {
            InitialValue = new ScenePoint(0, 0), Transforms = [new(Guid.NewGuid(), new(0), new(1), new ScenePoint(1, 2))]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(With(new(target, [])
        {
            InitialValue = new ScenePoint(0, 0), Transforms = [new(Guid.NewGuid(), new(0), new(1), new ScenePoint(1, 2), -1)]
        })));
    }

    [Fact]
    public void ZeroAccelerationPreservesAssImmediateChangeAtTheOperationStart()
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION, [])
        {
            InitialValue = 10, Transforms = [new(Guid.NewGuid(), new(1), new(3), 90, 0)]
        };
        ProjectValidator.Validate(new() { Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Tracks = [track] }] });
        Assert.Equal(10, SceneEvaluator.EvaluateScalarTrack(track, new(999, 1000)));
        Assert.Equal(90, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
        Assert.Equal(90, SceneEvaluator.EvaluateScalarTrack(track, new(2)));
        Assert.Equal(90, SceneEvaluator.EvaluateScalarTrack(track, new(3)));
    }

    [Fact]
    public void NodeTracksUseThePathBudgetInsteadOfTheOrdinaryTrackLimit()
    {
        var nodes = Enumerable.Range(0, 129).Select(index => new MaskNode { Position = new(index, index) }).ToImmutableArray();
        var line = new SubtitleLine();
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Mask = new VectorClipMask { Contours = [new() { Nodes = nodes }] },
            Tracks = nodes.Select(node => new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id),
                [new(new(0), node.Position)])).ToImmutableArray()
        };
        ProjectValidator.Validate(new() { Subtitles = [line], Layers = [layer] });
        var evaluated = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(0)));
        Assert.Same(layer.Mask, evaluated);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(2)]
    [InlineData(64)]
    public void PowerWithTinyClippedPhaseRemainsFiniteAndMonotonic(double exponent)
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY,
            [new(new(0), 0, KeyframeInterpolation.POWER) { CurveStart = 1 - 1e-15, CurveEnd = 1, Exponent = exponent }, new(new(1), 1)]);
        var previous = 0d;
        for (var sample = 0; sample <= 20; sample++)
        {
            var value = SceneEvaluator.EvaluateScalarTrack(track, new(sample, 20));
            Assert.True(double.IsFinite(value));
            Assert.InRange(value, previous, 1);
            Assert.Equal(sample / 20d, value, 10);
            previous = value;
        }
    }
}
