using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptColorTests
{
    private const string COLOR_SCRIPT = """
        effect "hdr-color" version 1
        short-clip compress
        segment enter fixed 200ms
            at 0 fill rgba(-0.5, 4, 8, 0.2) ease-in
            at 1 fill base
            at 0 stroke base
            at 1 stroke rgba(8, 2, 1, 0.7)
        end
        segment hold flex 1
            at 0 fill base hold
            at 1 fill base
            at 0 stroke rgba(8, 2, 1, 0.7) hold
            at 1 stroke rgba(8, 2, 1, 0.7)
        end
        segment exit fixed 200ms
            at 0 fill base
            at 1 fill rgba(-0.5, 4, 8, 0.2) ease-out
            at 0 stroke rgba(8, 2, 1, 0.7)
            at 1 stroke base
        end
        """;

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 1)]
    public void RgbaScriptsKeepCompleteLinearColorsAtFixedFlexBoundariesForShortAndLongClips(int numerator, int denominator)
    {
        var script = EffectScriptRegressionParser.Parse(COLOR_SCRIPT);
        var duration = new MediaTime(numerator, denominator);
        var target = new ProjectLayer { End = duration, SubtitleId = Guid.NewGuid(), Fill = new(99, 99, 99), Stroke = new(88, 88, 88) };
        var style = new SubtitleStyle { Fill = new(2, 3, 5, 0.8), Stroke = new(0.25, 0.5, 0.75, 0.6) };
        var tracks = EffectScriptCompiler.Compile(script, target, style);
        var fill = tracks.Single(track => track.Property == AnimationProperty.FILL);
        var stroke = tracks.Single(track => track.Property == AnimationProperty.STROKE);
        Assert.Equal(2, tracks.Length);
        Assert.All(tracks.SelectMany(track => track.Keyframes), frame => Assert.Equal(AnimationValueKind.COLOR, frame.Value.Kind));
        Assert.Equal(MediaTime.Zero, fill.Keyframes[0].Time);
        Assert.Equal(duration, fill.Keyframes[^1].Time);
        Assert.Equal(duration, stroke.Keyframes[^1].Time);
        Assert.Equal(new SceneColor(-0.5, 4, 8, 0.2), fill.Keyframes[0].Value.Color);
        Assert.Equal(style.Fill, fill.Keyframes[1].Value.Color);
        Assert.Equal(style.Stroke, stroke.Keyframes[0].Value.Color);
        Assert.Equal(style.Stroke, stroke.Keyframes[^1].Value.Color);
        Assert.Equal(style.Fill, SceneEvaluator.EvaluateColorTrack(fill, new(numerator, denominator * 2)));
        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, target));
    }

    [Theory]
    [InlineData("fill 1")]
    [InlineData("fill (1, 2)")]
    [InlineData("opacity rgba(1, 2, 3, 1)")]
    [InlineData("position rgba(1, 2, 3, 1)")]
    [InlineData("fill rgba(1, 2, 3)")]
    [InlineData("stroke rgba(1, 2, 3, 1.01)")]
    [InlineData("fill rgba(-65505, 2, 3, 1)")]
    [InlineData("fill offset(1, 2)")]
    [InlineData("stroke factor(1)")]
    [InlineData("fill offset(rgba(1, 2, 3, 1))")]
    public void WrongDimensionsUnsupportedColorExpressionsAndOutOfRangeComponentsHaveSourceDiagnostics(string expression)
    {
        var source = $"effect \"invalid-color\" version 1\nshort-clip compress\nsegment all flex 1\n    at 0 {expression}\n    at 1 {expression}\nend";
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source));
        Assert.Equal(4, error.Line);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public void ProgrammaticColorOffsetAndFactorAreRejectedBeforeCompilation()
    {
        var parsed = EffectScriptRegressionParser.Parse(COLOR_SCRIPT);
        foreach (var kind in new[] { EffectScriptValueKind.OFFSET, EffectScriptValueKind.FACTOR })
        {
            var segment = parsed.Segments[0];
            var frame = segment.Keyframes[0] with { Value = new(kind, AnimationValue.FromColor(new(1, 2, 3, 0.5))) };
            var changed = parsed with { Segments = parsed.Segments.SetItem(0, segment with { Keyframes = segment.Keyframes.SetItem(0, frame) }) };
            Assert.Throws<EffectScriptException>(() => EffectScriptValidator.Validate(changed));
        }
    }
}
