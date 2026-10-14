using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptMaskTests
{
    [Theory]
    [InlineData("mask-morph", 5, 1)]
    [InlineData("mask-morph", 3, 5)]
    [InlineData("mask-morph", 2, 5)]
    [InlineData("mask-morph", 1, 1000)]
    [InlineData("mask-slide", 5, 1)]
    [InlineData("mask-slide", 3, 5)]
    [InlineData("mask-slide", 2, 5)]
    [InlineData("mask-slide", 1, 1000)]
    public void ShippedMaskExamplesCompileAtLongExactShortAndTinyDurations(string name, int numerator, int denominator)
    {
        var script = EffectScriptParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name + ".aegifx")));
        var layer = new ProjectLayer
        {
            End = new(numerator, denominator),
            Mask = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(20, 30), OutHandle = new(5, 6) }] }] }
        };

        var tracks = EffectScriptCompiler.Compile(script, layer);

        Assert.NotEmpty(tracks);
        Assert.All(tracks, track => Assert.Equal(layer.End, track.Keyframes[^1].Time));
        Assert.All(tracks, track => Assert.Equal(AnimationValueKind.VECTOR, SceneEvaluator.EvaluateTrack(track, layer.End / 2).Kind));
    }

    [Fact]
    public void RectangleAndWholeMaskPropertiesCompileAgainstExistingGeometry()
    {
        var script = EffectScriptParser.Parse("""
            effect "mask-reveal" version 1
            short-clip compress
            segment reveal flex 1
                at 0 mask-rectangle-top-left base power(2)
                at 1 mask-rectangle-top-left offset(100, 20)
                at 0 mask-rectangle-bottom-right base
                at 1 mask-rectangle-bottom-right offset(200, 40)
                at 0 mask-position base
                at 1 mask-position offset(30, 40)
                at 0 mask-scale base
                at 1 mask-scale factor(2, 3)
                at 0 mask-rotation base
                at 1 mask-rotation offset(90)
            end
            """);
        var mask = new RectangleClipMask
        {
            TopLeft = new(10, 20), BottomRight = new(300, 200),
            Placement = new() { Pivot = new(0.5, 0.5), Offset = new(155, 110) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(290, 180) },
            Transform = new() { Position = new(5, 6), Scale = new(2, 0.5), Rotation = 10 }
        };
        var tracks = EffectScriptCompiler.Compile(script, new() { End = new(2), Mask = mask });

        Assert.Equal(5, tracks.Length);
        var topLeft = tracks.Single(track => track.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        Assert.Equal(KeyframeInterpolation.POWER, topLeft.Keyframes[0].Interpolation);
        Assert.Equal(2, topLeft.Keyframes[0].Exponent);
        Assert.Equal(new ScenePoint(35, 25), SceneEvaluator.EvaluateTrack(topLeft, new(1)).Vector);
        Assert.Equal(new ScenePoint(4, 1.5), tracks.Single(track => track.Property == AnimationProperty.MASK_SCALE).Keyframes[^1].Value.Vector);
        Assert.Equal(100, tracks.Single(track => track.Property == AnimationProperty.MASK_ROTATION).Keyframes[^1].Value.Scalar);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(2, 5)]
    [InlineData(1, 1000)]
    public void NodeSelectorsResolveIndependentStableTargetsAndContentClock(int numerator, int denominator)
    {
        var script = EffectScriptParser.Parse("""
            effect "mask-morph" version 1
            short-clip compress
            segment enter fixed 300ms
                at 0 mask-node(1,1).position base power(1.5)
                at 1 mask-node(1,1).position offset(40, 0)
                at 0 mask-node(1,2).position base
                at 1 mask-node(1,2).position offset(0, 60)
                at 0 mask-node(1,1).in-handle base
                at 1 mask-node(1,1).in-handle offset(-3, 4)
                at 0 mask-node(1,1).out-handle base
                at 1 mask-node(1,1).out-handle offset(5, -6)
            end
            segment stay flex 1
            end
            """);
        var nodes = new MaskNode[]
        {
            new() { Position = new(10, 20), InHandle = new(-1, 2), OutHandle = new(3, -4) },
            new() { Position = new(100, 30) }
        };
        var duration = new MediaTime(numerator, denominator);
        var layer = new ProjectLayer
        {
            Start = new(10), End = new MediaTime(10) + duration, AnimationOffset = new(7),
            Mask = new VectorClipMask { Contours = [new() { Nodes = [.. nodes] }] }
        };
        var tracks = EffectScriptCompiler.Compile(script, layer);

        Assert.Equal(4, tracks.Length);
        Assert.Equal(2, tracks.Count(track => track.Property == AnimationProperty.MASK_NODE_POSITION));
        Assert.Contains(tracks, track => track.Target == new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodes[0].Id));
        Assert.Contains(tracks, track => track.Target == new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodes[1].Id));
        Assert.All(tracks, track => Assert.Equal(new MediaTime(7), track.Keyframes[0].Time));
        Assert.All(tracks, track => Assert.Equal(new MediaTime(7) + duration, track.Keyframes[^1].Time));
        Assert.Equal(new ScenePoint(-4, 6), tracks.Single(track => track.Property == AnimationProperty.MASK_NODE_IN_HANDLE).Keyframes[^1].Value.Vector);
        Assert.Equal(new ScenePoint(8, -10), tracks.Single(track => track.Property == AnimationProperty.MASK_NODE_OUT_HANDLE).Keyframes[^1].Value.Vector);
    }

    [Theory]
    [InlineData("mask-node(0,1).position")]
    [InlineData("mask-node(1,0).position")]
    [InlineData("mask-node(-1,1).position")]
    [InlineData("mask-position (1, 2) power(-2)")]
    [InlineData("mask-position (1, 2) power(0)")]
    public void InvalidSelectorsOrPowerReportSourceLocation(string declaration)
    {
        var line = declaration.Contains(' ', StringComparison.Ordinal) ? declaration : declaration + " base";
        var source = $"effect \"invalid-mask\" version 1\nshort-clip compress\nsegment all flex 1\n at 0 {line}\n at 1 mask-position base\nend";

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(source));

        Assert.Equal(4, error.Line);
        Assert.True(error.Column > 0);
    }

    [Theory]
    [InlineData("mask-node(2,1).position")]
    [InlineData("mask-node(1,2).in-handle")]
    [InlineData("mask-rectangle-top-left")]
    public void MissingGeometryTargetsFailAtTheDeclaringFrame(string property)
    {
        var script = EffectScriptParser.Parse($"effect \"missing-mask\" version 1\nshort-clip compress\nsegment all flex 1\n at 0 {property} base\n at 1 {property} base\nend");
        var target = new ProjectLayer { Mask = new VectorClipMask { Contours = [new() { Nodes = [new()] }] } };

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, target));

        Assert.Equal(4, error.Line);
        Assert.True(error.Column > 0);
    }
}
