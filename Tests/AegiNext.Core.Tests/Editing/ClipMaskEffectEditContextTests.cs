using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Editing;

public sealed class ClipMaskEffectEditContextTests
{
    [Fact]
    public void VisibleDraftInvertsTheOrderedStackAgainstAnimatedNativeValuesWithoutBaking()
    {
        var track = new AnimationTrack(AnimationProperty.MASK_POSITION,
        [
            new(new(0), new ScenePoint(10, 20), KeyframeInterpolation.POWER) { Exponent = 2 },
            new(new(2), new ScenePoint(50, 100))
        ]);
        var layer = Rectangle() with
        {
            Tracks = [track],
            Effects = [Instance("mask-position offset(3, -4)"), Instance("mask-position factor(2, -3)"), Instance("mask-position offset(5, 6)")]
        };
        var cache = new EffectCompilationCache();
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1), cache);
        var visible = Assert.IsType<RectangleClipMask>(context.VisibleMask);
        Assert.Equal(new ScenePoint(51, -102), visible.Transform.Position);
        var draft = visible with { Transform = visible.Transform with { Position = new(63, -117) } };

        Assert.True(context.TryGetNativePose(draft, out var native));

        Assert.Equal(new ScenePoint(26, 45), native.Transform.Position);
        Assert.Equal(new ScenePoint(20, 40), context.NativeMask!.Transform.Position);
        var preview = SceneEvaluator.EvaluateMask(layer with { Mask = native, Tracks = [] }, new(1), effectCache: cache);
        Assert.Equal(draft, preview);
        Assert.Equal(3, cache.CompilationCount);
        Assert.Same(track, Assert.Single(layer.Tracks));
        Assert.Equal(new ScenePoint(0, 0), layer.Mask!.Transform.Position);
    }

    [Fact]
    public void ZeroMultiplierBlocksOnlyItsTargetAndUnchangedComponentsKeepNativeValues()
    {
        var layer = Rectangle() with { Effects = [Instance("mask-position factor(0, 2)")] };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var target = new AnimationTrackTarget(AnimationProperty.MASK_POSITION);
        Assert.False(context.CanEdit(target));
        Assert.False(context.CanEdit(target, 1));
        Assert.True(context.CanEdit(target, 2));
        Assert.True(context.CanEdit(new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)));
        var visible = context.VisibleMask!;
        var draft = visible with { Transform = visible.Transform with { Position = new(0, 30) } };

        Assert.True(context.TryGetNativePose(draft, out var native));
        Assert.Equal(new ScenePoint(0, 15), native.Transform.Position);
        Assert.False(context.TryGetNativePose(draft with { Transform = draft.Transform with { Position = new(1, 30) } }, out _));
    }

    [Fact]
    public void AbsoluteOverrideDoesNotDestroyTheUnchangedUpstreamPose()
    {
        var layer = Rectangle() with
        {
            Mask = new RectangleClipMask { Transform = new() { Position = new(17, 29) } },
            Effects = [Instance("mask-position (30, 40)"), Instance("mask-position offset(2, 3)")]
        };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));

        Assert.False(context.CanEdit(new(AnimationProperty.MASK_POSITION)));
        Assert.True(context.TryGetNativePose(context.VisibleMask!, out var native));
        Assert.Equal(layer.Mask, native);
        var resized = Assert.IsType<RectangleClipMask>(context.VisibleMask) with { BottomRight = new(200, 300) };
        Assert.True(context.TryGetNativePose(resized, out var resizedNative));
        Assert.Equal(new ScenePoint(17, 29), resizedNative.Transform.Position);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    public void OnlyEnabledInstancesInsideTheirHalfOpenContentTimeWindowAffectEditability(int contentTime, bool editable)
    {
        var layer = Rectangle() with
        {
            Start = new(10), End = new(13), AnimationOffset = new(7),
            Effects =
            [
                Instance("mask-position (20, 30)") with { Start = new(8), End = new(9) },
                Instance("mask-position (40, 50)") with { Start = new(7), End = new(10), IsEnabled = false }
            ]
        };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(contentTime));

        Assert.Equal(editable, context.CanEdit(new(AnimationProperty.MASK_POSITION)));
    }

    [Fact]
    public void SharedSegmentEndpointDoesNotAccumulateThePreviousChannelAgain()
    {
        var instance = Instance("mask-position factor(2, 2)") with
        {
            Source = "effect \"mask-segments\" version 3\nshort-clip compress\nscope clip current\n" +
                "segment first flex 1\nat 0 mask-position factor(2, 2)\nat 1 mask-position factor(2, 2)\nend\n" +
                "segment second flex 1\nat 0 mask-position factor(2, 2)\nat 1 mask-position factor(2, 2)\nend\nend"
        };
        var layer = Rectangle() with { Effects = [instance] };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var visible = context.VisibleMask!;
        var draft = visible with { Transform = visible.Transform with { Position = new(20, 40) } };

        Assert.True(context.TryGetNativePose(draft, out var native));
        Assert.Equal(new ScenePoint(10, 20), native.Transform.Position);
    }

    [Fact]
    public void NodeAndHandleEditsPreserveIdentitiesPlacementAndOtherTargets()
    {
        var node = new MaskNode { Position = new(10, 20), OutHandle = new(4, 5) };
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [node] }],
            Placement = new() { Pivot = new(0.2, 0.4) }, ReferenceFrame = new() { Size = new(300, 400) }
        };
        var layer = Rectangle() with { Mask = mask, Effects = [Instance("mask-node(1,1).out-handle factor(2, -3)")] };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_OUT_HANDLE, node.Id);
        var draft = ClipMaskAnimation.SetBaseValue(context.VisibleMask!, target, new ScenePoint(18, -30));

        Assert.True(context.TryGetNativePose(draft, out var native));
        var vector = Assert.IsType<VectorClipMask>(native);
        Assert.Equal(new ScenePoint(9, 10), vector.Contours[0].Nodes[0].OutHandle);
        Assert.Equal(node.Position, vector.Contours[0].Nodes[0].Position);
        Assert.Equal(node.Id, vector.Contours[0].Nodes[0].Id);
        Assert.Equal(mask.Placement, vector.Placement);
        Assert.Equal(mask.ReferenceFrame, vector.ReferenceFrame);
    }

    [Fact]
    public void AppendedContourKeepsStaticExistingGeometryAndTransformTracksUpstream()
    {
        var node = new MaskNode { Position = new(10, 20) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] };
        var layer = Rectangle() with
        {
            Mask = mask,
            Tracks = [new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(90, 100))])],
            Effects = [Instance("mask-position offset(10, 20)")]
        };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var visible = Assert.IsType<VectorClipMask>(context.VisibleMask);
        var added = new MaskContour { Nodes = [new() { Position = new(40, 50) }] };
        var draft = visible with { Contours = visible.Contours.Add(added) };

        Assert.False(context.IsTopologyLocked);
        Assert.True(context.TryGetTopologyDraft(draft, out var native));
        var vector = Assert.IsType<VectorClipMask>(native);
        Assert.Equal(mask.Transform, vector.Transform);
        Assert.Same(mask.Contours[0], vector.Contours[0]);
        Assert.Same(added, vector.Contours[1]);
        Assert.Equal(draft, SceneEvaluator.EvaluateMask(layer with { Mask = vector }, new(1)));
        Assert.False(context.TryGetTopologyDraft(draft with { Transform = draft.Transform with { Position = new(7, 8) } }, out _));
    }

    [Fact]
    public void EnabledNodeChannelsLockTopologyAcrossTheInstanceWindowWhileDisabledChannelsDoNot()
    {
        var layer = Rectangle() with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(10, 20) }] }] },
            Effects = [Instance("mask-node(1,1).position offset(3, 4)") with { Start = new(1), End = new(2) }]
        };
        Assert.True(ClipMaskEffectEditContext.Create(layer, null, MediaTime.Zero).IsTopologyLocked);
        Assert.False(ClipMaskEffectEditContext.Create(layer with { Effects = [layer.Effects[0] with { IsEnabled = false }] }, null, new(1)).IsTopologyLocked);
    }

    [Fact]
    public void UnrepresentableNativeValuesAreRejectedWithoutChangingTheVisibleDraft()
    {
        var layer = Rectangle() with { Effects = [Instance("mask-position factor(0.000001, 1)")] };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var draft = context.VisibleMask! with { Transform = context.VisibleMask!.Transform with { Position = new(10000, 0) } };

        Assert.False(context.TryGetNativePose(draft, out _));
        Assert.Equal(new ScenePoint(10000, 0), draft.Transform.Position);
    }

    [Fact]
    public void InverseRejectsAnInvalidIntermediateValueEvenWhenTheNativeAndFinalValuesFit()
    {
        var layer = Rectangle() with
        {
            Effects = [Instance("mask-position offset(999999999, 0)"), Instance("mask-position factor(0.1, 1)")]
        };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1));
        var draft = context.VisibleMask! with { Transform = context.VisibleMask!.Transform with { Position = new(100000000.9, 0) } };

        Assert.False(context.TryGetNativePose(draft, out _));
    }

    [Fact]
    public void FullNodeBudgetMapsSparseTargetsInOnePoseAndKeepsUntouchedNodeObjects()
    {
        var nodes = Enumerable.Range(0, 10000).Select(index => new MaskNode { Position = new(index, index / 2d) }).ToImmutableArray();
        var mask = new VectorClipMask { Contours = [new() { Nodes = nodes }] };
        var effect = Instance("mask-node(1,1).position offset(2, 3)") with
        {
            Source = "effect \"large-mask\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
                "at 0 mask-node(1,1).position factor(2, 3)\nat 1 mask-node(1,1).position factor(2, 3)\n" +
                "at 0 mask-node(1,10000).out-handle offset(3, 4)\nat 1 mask-node(1,10000).out-handle offset(3, 4)\nend\nend"
        };
        var context = ClipMaskEffectEditContext.Create(Rectangle() with { Mask = mask, Effects = [effect] }, null, new(1));
        var draft = ClipMaskAnimation.SetBaseValue(context.VisibleMask!, new(AnimationProperty.MASK_NODE_POSITION, nodes[0].Id), new ScenePoint(20, 30));

        Assert.True(context.TryGetNativePose(draft, out var native));

        var mapped = Assert.IsType<VectorClipMask>(native).Contours[0].Nodes;
        Assert.Equal(new ScenePoint(10, 10), mapped[0].Position);
        Assert.Equal(default, mapped[^1].OutHandle);
        Assert.Same(nodes[5000], mapped[5000]);
        Assert.Equal(10000, mapped.Length);
        Assert.Equal(nodes.Select(node => node.Id), mapped.Select(node => node.Id));
    }

    private static ProjectLayer Rectangle() => new()
    {
        Kind = LayerKind.SHAPE, End = new(2),
        Mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 200) }
    };

    private static EffectInstance Instance(string value) => new()
    {
        Name = "Mask gesture", End = new(2),
        Source = "effect \"mask-edit\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
            "at 0 " + value + "\nat 1 " + value + "\nend\nend"
    };
}
