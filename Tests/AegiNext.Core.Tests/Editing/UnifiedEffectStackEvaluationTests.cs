using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Editing;

public sealed class UnifiedEffectStackEvaluationTests
{
    [Theory]
    [InlineData("offset(10, -3)", 15, 4)]
    [InlineData("factor(2, 3)", 10, 21)]
    [InlineData("vec2(30, 40)", 30, 40)]
    public void NativeAbsoluteAndScriptOperationsExecuteInTheirVisualStackOrder(string value, double x, double y)
    {
        var native = new AnimationTrack(AnimationProperty.POSITION, [new(new(0), new ScenePoint(5, 7))]);
        var script = Operation("position", value);
        var layer = Shape() with { Tracks = [native], Effects = [script] };
        var scriptAbove = layer with { EffectRowOrder = [new(InstanceId: script.Id), new(native.Target)] };
        var nativeAbove = layer with { EffectRowOrder = [new(native.Target), new(InstanceId: script.Id)] };

        Assert.Equal(new ScenePoint(x, y), SceneEvaluator.EvaluateLayer(scriptAbove, null, new(1, 2)).Transform.Position);
        Assert.Equal(new ScenePoint(5, 7), SceneEvaluator.EvaluateLayer(nativeAbove, null, new(1, 2)).Transform.Position);
        Assert.Same(native, layer.Tracks[0]);
    }

    [Fact]
    public void AManualPropertyBetweenTwoMultiPropertyInstancesOverridesOnlyItsOwnChannel()
    {
        var position = new AnimationTrack(AnimationProperty.POSITION, [new(new(0), new ScenePoint(5, 0))]);
        var opacity = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.4d)]);
        var first = Instance("at 0 position offset(10, 0)\nat 1 position offset(10, 0)\nat 0 opacity factor(0.5)\nat 1 opacity factor(0.5)");
        var last = Instance("at 0 position factor(2, 1)\nat 1 position factor(2, 1)\nat 0 opacity offset(0.1)\nat 1 opacity offset(0.1)");
        var layer = Shape() with
        {
            Tracks = [position, opacity], Effects = [first, last],
            EffectRowOrder = [new(InstanceId: last.Id), new(position.Target), new(InstanceId: first.Id), new(opacity.Target)]
        };

        var between = SceneEvaluator.EvaluateLayer(layer, null, new(1, 2));
        var below = SceneEvaluator.EvaluateLayer(layer with
        {
            EffectRowOrder = [new(InstanceId: last.Id), new(InstanceId: first.Id), new(position.Target), new(opacity.Target)]
        }, null, new(1, 2));

        Assert.Equal(10, between.Transform.Position.X);
        Assert.Equal(30, below.Transform.Position.X);
        Assert.Equal(0.3, between.Opacity, 8);
        Assert.Equal(0.3, below.Opacity, 8);
    }

    [Fact]
    public void ManualOnlyWholeAndRangeStylesFollowTheSameStackInsteadOfFixedScopePriority()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "AB", End = new(1), AnimationRanges = [range] };
        var whole = new AnimationTrack(AnimationProperty.FILL, [new(new(0), new SceneColor(0, 1, 0, 1))]);
        var scoped = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id),
            [new(new(0), new SceneColor(1, 0, 0, 1))]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [whole, scoped],
            EffectRowOrder = [new(whole.Target), new(scoped.Target)] };
        var wholeLast = SceneEvaluator.EvaluateLayer(layer, line, new(1, 2));
        var rangeLast = SceneEvaluator.EvaluateLayer(layer with { EffectRowOrder = [new(scoped.Target), new(whole.Target)] }, line, new(1, 2));

        Assert.Equal(new SceneColor(0, 1, 0, 1), Style(wholeLast, line, 0).Fill);
        Assert.Equal(new SceneColor(1, 0, 0, 1), Style(rangeLast, line, 0).Fill);
        Assert.Equal(new SceneColor(0, 1, 0, 1), Style(rangeLast, line, 1).Fill);
    }

    [Fact]
    public void ARangeStateNativeRowIsAppliedBetweenWholeStateScriptRowsOnlyForMatchingGraphemes()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "AB", End = new(1), AnimationRanges = [range], Style = new() { Fill = SceneColor.White } };
        var target = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        var native = new AnimationTrack(target, [new(new(0), new SceneColor(0.5, 0.25, 0.75, 1))]);
        var lower = Operation("fill", "rgba(0.25, 0.5, 0.75, 1)", "state active");
        var upper = Operation("fill", "base", "state active");
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [native], Effects = [lower, upper],
            EffectRowOrder = [new(InstanceId: upper.Id), new(target), new(InstanceId: lower.Id)] };
        var evaluated = SceneEvaluator.EvaluateLayer(layer, line, new(1, 2));

        Assert.Equal(new SceneColor(0.5, 0.25, 0.75, 1), Style(evaluated, line, 0, SubtitleAnimationState.ACTIVE).Fill);
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), Style(evaluated, line, 1, SubtitleAnimationState.ACTIVE).Fill);
        Assert.Equal(SceneColor.White, Style(evaluated, line, 0).Fill);
        var moved = SceneEvaluator.EvaluateLayer(layer with
        {
            EffectRowOrder = [new(InstanceId: upper.Id), new(InstanceId: lower.Id), new(target)]
        }, line, new(1, 2));
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), Style(moved, line, 0, SubtitleAnimationState.ACTIVE).Fill);
    }

    [Fact]
    public void NodeNativeAssignmentsCanOverrideAnEarlierScriptBeforeTheNextFactor()
    {
        var node = new MaskNode { Position = new(1, 2) };
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id);
        var native = new AnimationTrack(target, [new(new(0), new ScenePoint(5, 7))]);
        var lower = Operation("mask-node(1,1).position", "offset(10, 0)");
        var upper = Operation("mask-node(1,1).position", "factor(2, 3)");
        var layer = Shape() with { Mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] },
            Tracks = [native], Effects = [lower, upper],
            EffectRowOrder = [new(InstanceId: upper.Id), new(target), new(InstanceId: lower.Id)] };

        var mask = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(1, 2)));

        Assert.Equal(new ScenePoint(10, 21), mask.Contours[0].Nodes[0].Position);
        Assert.Equal(new ScenePoint(1, 2), node.Position);
    }

    [Fact]
    public void FullRotationAndAxisAssignmentsOverwriteOnlyTheComponentsReachedLater()
    {
        var full = new AnimationTrack(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(10, 20, 30))]);
        var axisScript = Operation("rotation-x", "offset(7)");
        var fullLast = Shape() with { Tracks = [full], Effects = [axisScript],
            EffectRowOrder = [new(full.Target), new(InstanceId: axisScript.Id)] };
        var axis = new AnimationTrack(AnimationProperty.ROTATION_X, [new(new(0), 7d)]);
        var fullScript = Operation("rotation", "vec3(40, 50, 60)");
        var axisLast = Shape() with { Tracks = [axis], Effects = [fullScript],
            EffectRowOrder = [new(axis.Target), new(InstanceId: fullScript.Id)] };

        var first = SceneEvaluator.EvaluateLayer(fullLast, null, new(1, 2)).Transform;
        var second = SceneEvaluator.EvaluateLayer(axisLast, null, new(1, 2)).Transform;

        Assert.Equal(new SceneVector3(10, 20, 30), new(first.RotationX, first.RotationY, first.Rotation));
        Assert.Equal(new SceneVector3(7, 50, 60), new(second.RotationX, second.RotationY, second.Rotation));
    }

    [Fact]
    public void BaseReadsTheCurrentNonlinearUpstreamAtEveryFrameAndReorderingDoesNotRecompileItsPlan()
    {
        var native = new AnimationTrack(AnimationProperty.POSITION,
            [new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.POWER) { Exponent = 2 }, new(new(1), new ScenePoint(16, 0))]);
        var script = Instance("at 0 position base\nat 1 position factor(2, 1)");
        var layer = Shape() with { Tracks = [native], Effects = [script],
            EffectRowOrder = [new(InstanceId: script.Id), new(native.Target)] };
        var cache = new EffectCompilationCache();
        var prepared = new PreparedProjectScene(new() { Layers = [layer] }, cache);
        var reordered = new PreparedProjectScene(new() { Layers = [layer with
        {
            EffectRowOrder = [new(native.Target), new(InstanceId: script.Id)]
        }] }, cache);

        Assert.Equal(1.25, SceneEvaluator.EvaluateLayer(prepared, layer.Id, new(1, 4)).Transform.Position.X, 8);
        Assert.Equal(15.75, SceneEvaluator.EvaluateLayer(prepared, layer.Id, new(3, 4)).Transform.Position.X, 8);
        Assert.Equal(9, SceneEvaluator.EvaluateLayer(reordered, layer.Id, new(3, 4)).Transform.Position.X, 8);
        Assert.Equal(1, cache.CompilationCount);
    }

    [Fact]
    public void MaskGesturesInvertOnlyTheScriptSuffixAfterTheNativeRowAndAnOverriddenZeroFactorDoesNotLockEditing()
    {
        var target = new AnimationTrackTarget(AnimationProperty.MASK_POSITION);
        var native = new AnimationTrack(target, [new(new(0), new ScenePoint(10, 20))]);
        var absolute = Operation("mask-position", "vec2(100, 200)");
        var factor = Operation("mask-position", "factor(2, 3)");
        var offset = Operation("mask-position", "offset(5, 7)");
        var layer = Shape() with { Mask = new RectangleClipMask { BottomRight = new(80, 60) }, Tracks = [native],
            Effects = [absolute, factor, offset], EffectRowOrder =
                [new(InstanceId: offset.Id), new(InstanceId: factor.Id), new(target), new(InstanceId: absolute.Id)] };
        var context = ClipMaskEffectEditContext.Create(layer, null, new(1, 2));
        var visible = Assert.IsType<RectangleClipMask>(context.VisibleMask);

        Assert.Equal(new ScenePoint(25, 67), visible.Transform.Position);
        Assert.True(context.CanEdit(target));
        Assert.True(context.TryGetNativePose(visible with { Transform = visible.Transform with { Position = new(31, 79) } }, out var pose));
        Assert.Equal(new ScenePoint(13, 24), pose.Transform.Position);
        var stillAbsolute = ClipMaskEffectEditContext.Create(layer with
        {
            EffectRowOrder = [new(InstanceId: offset.Id), new(InstanceId: factor.Id), new(InstanceId: absolute.Id), new(target)]
        }, null, new(1, 2));
        Assert.False(stillAbsolute.CanEdit(target));
        var nativeLast = ClipMaskEffectEditContext.Create(layer with
        {
            EffectRowOrder = [new(target), new(InstanceId: offset.Id), new(InstanceId: factor.Id), new(InstanceId: absolute.Id)]
        }, null, new(1, 2));
        Assert.True(nativeLast.CanEdit(target));
        Assert.Equal(new ScenePoint(10, 20), nativeLast.VisibleMask!.Transform.Position);
    }

    private static SubtitleStyle Style(EvaluatedLayer layer, SubtitleLine line, int offset,
        SubtitleAnimationState state = SubtitleAnimationState.NORMAL) =>
        SubtitleAnimationEvaluation.ApplyStyleAnimations(layer, line.Style, offset, state);

    private static ProjectLayer Shape() => new()
    {
        Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(1), Transform = new(1, 2)
    };

    private static EffectInstance Operation(string property, string value, string fields = "") =>
        Instance($"at 0 {property} {value}\nat 1 {property} {value}", fields);

    private static EffectInstance Instance(string frames, string fields = "") => new()
    {
        Name = "Unified stack", End = new(1), Source = $$"""
            effect "unified-stack" version 3
            short-clip compress
            scope whole current
                {{fields}}
                segment all flex 1
                    {{frames}}
                end
            end
            """
    };
}
