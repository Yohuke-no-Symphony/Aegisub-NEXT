using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptProjectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ProjectionPropertiesCompileAsIndependentScalarStreamsWithoutWrappingAngles(int version)
    {
        var script = EffectScriptParser.Parse(Source(version));
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 100, 40),
            End = new(4)
        };

        var result = EffectScriptCompiler.CompileTarget(script, layer);

        Assert.Equal(3, result.Tracks.Length);
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_X), 0, 360, 720);
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_Y), 0, -45, -90);
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE), 312.5, 468.75, 625);
        ProjectValidator.Validate(new() { Layers = [layer with { Tracks = result.Tracks }] });
    }

    [Fact]
    public void RelativeProjectionFramesUseOriginalNativeBaseValues()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(4),
            Transform = new() { RotationX = 30, RotationY = -20, PerspectiveDistance = 500 }
        };
        var result = EffectScriptCompiler.CompileTarget(EffectScriptParser.Parse(Source(2)), layer);

        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_X), 30, 390, 750);
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_Y), -20, -65, -110);
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE), 500, 750, 1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000001)]
    public void PerspectiveDistanceRejectsInvalidResolvedFrames(double distance)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(4) };
        var source = Source(2).Replace("perspective-distance factor(2)",
            "perspective-distance " + distance.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.CompileTarget(EffectScriptParser.Parse(source), layer));
        Assert.Empty(layer.Tracks);
    }

    [Fact]
    public void GroupedProjectionUsesNormalRangesAndPreservesUnrelatedRotation()
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var rotation = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 25d), new(new(4), 50d)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [rotation] };
        var script = EffectScriptParser.Parse(Source(2).Replace("scope whole current", "scope whole current\nunit grapheme", StringComparison.Ordinal));

        var result = EffectScriptComposer.ComposeTarget(script, layer, subtitle: line);

        Assert.Equal(2, result.Subtitle!.AnimationRanges.Length);
        Assert.Equal(rotation, result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION));
        foreach (var range in result.Subtitle.AnimationRanges)
        {
            var tracks = result.Tracks.Where(track => track.Target.TextRangeId == range.Id).ToArray();
            Assert.Equal(3, tracks.Length);
            Assert.All(tracks, track => Assert.Equal(SubtitleAnimationState.NORMAL, track.Target.State));
            AssertScalar(tracks.Single(track => track.Property == AnimationProperty.ROTATION_X), 0, 360, 720);
        }
        ProjectValidator.Validate(new()
        {
            Subtitles = [result.Subtitle],
            Layers = [layer with { Tracks = result.Tracks }]
        });
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    public void ProjectionPropertiesRejectKaraokeStates(string state)
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var source = Source(2).Replace("scope whole current", $"scope whole current\nstate {state}", StringComparison.Ordinal);
        var script = EffectScriptParser.Parse(source);

        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.CompileTarget(script, layer, subtitle: line));
        Assert.Empty(layer.Tracks);
        Assert.Empty(line.AnimationRanges);
    }

    private static string Source(int version)
    {
        var scope = version == 2 ? "scope whole current" : string.Empty;
        var scopeEnd = version == 2 ? "end" : string.Empty;
        return $$"""
            effect "plane-turn" version {{version}}
            short-clip compress
            {{scope}}
            segment turn flex 1
                at 0 rotation-x base
                at 0 rotation-y base
                at 0 perspective-distance base
                at 1 rotation-x offset(720)
                at 1 rotation-y offset(-90)
                at 1 perspective-distance factor(2)
            end
            {{scopeEnd}}
            """;
    }

    private static void AssertScalar(AnimationTrack track, double first, double middle, double last)
    {
        Assert.Equal(AnimationValueKind.SCALAR, track.Keyframes[0].Value.Kind);
        Assert.Equal(first, SceneEvaluator.EvaluateScalarTrack(track, MediaTime.Zero), 9);
        Assert.Equal(middle, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 9);
        Assert.Equal(last, SceneEvaluator.EvaluateScalarTrack(track, new(4)), 9);
    }
}
