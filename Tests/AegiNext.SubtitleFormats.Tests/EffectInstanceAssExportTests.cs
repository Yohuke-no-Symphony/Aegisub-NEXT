using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class EffectInstanceAssExportTests
{
    [Fact]
    public void MixedInlineTypographyIsSampledPerSafeTextRunWithoutChangingTheSource()
    {
        var line = new SubtitleLine
        {
            Text = "Ae\u0301😀B", End = new(2), Style = new() { FontSize = 20 },
            InlineSpans = [new(1, 2, new() { FontSize = 40 }), new(3, 2, new() { FontSize = 60 })]
        };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Effects = [Instance(AnimationProperty.FONT_SIZE, "factor(1)", "factor(2)")]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();

        var projected = AssEffectInstanceProjection.Create(document, diagnostics);

        var expected = SceneEvaluator.EvaluateLayer(new PreparedProjectScene(document), layer.Id, new(1));
        var actual = SceneEvaluator.EvaluateLayer(projected.Layers[0], projected.Subtitles[0], new(1));
        foreach (var offset in new[] { 0, 1, 3, 5 })
        {
            var style = line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset &&
                offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;
            Assert.Equal(SubtitleAnimationEvaluation.ApplyStyleAnimations(expected, style, offset, SubtitleAnimationState.NORMAL).FontSize,
                SubtitleAnimationEvaluation.ApplyStyleAnimations(actual, style, offset, SubtitleAnimationState.NORMAL).FontSize, 6);
        }
        var boundaries = new SubtitleTextBoundaries(line.Text);
        Assert.All(projected.Subtitles[0].AnimationRanges, range =>
        {
            Assert.True(boundaries.Contains(range.Utf16Start));
            Assert.True(boundaries.Contains(range.Utf16Start + range.Utf16Length));
        });
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.EffectInstances");
        Assert.Contains("\\t(", written.Text, StringComparison.Ordinal);
        Assert.Same(line, document.Subtitles[0]);
        Assert.Same(layer, document.Layers[0]);
        Assert.Single(layer.Effects);
        Assert.Empty(layer.Tracks);
        Assert.Empty(line.AnimationRanges);
    }

    [Fact]
    public void CurrentUpstreamValuesAndInstanceBoundaryJumpsArePreservedInTemporaryTracks()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var native = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.2), new(new(2), 0.8)]);
        var instance = Instance(AnimationProperty.OPACITY, "factor(0.5)", "factor(0.5)") with
        {
            Start = new(1, 2), End = new(3, 2)
        };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [native], Effects = [instance] };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();

        var projected = AssEffectInstanceProjection.Create(document, diagnostics);
        var track = Assert.Single(projected.Layers[0].Tracks);

        Assert.Equal(0.2, SceneEvaluator.EvaluateScalarTrack(track, new(0)), 8);
        Assert.Equal(0.175, SceneEvaluator.EvaluateScalarTrack(track, new(1, 2)), 8);
        Assert.Equal(0.25, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 8);
        Assert.Equal(0.65, SceneEvaluator.EvaluateScalarTrack(track, new(3, 2)), 8);
        Assert.Contains(track.Keyframes, frame => frame.Time == new MediaTime(1, 2));
        Assert.Empty(projected.Layers[0].Effects);
        Assert.Same(native, layer.Tracks[0]);
    }

    [Fact]
    public void RootPositionSamplingRetainsTheMotionPathWithoutAddingItsOffsetTwice()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var path = new MotionPath(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(2));
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End, Transform = new(5, 7), MotionPath = path,
            Effects = [Instance(AnimationProperty.POSITION, "offset(10, 0)", "offset(10, 0)")]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        var projected = AssEffectInstanceProjection.Create(document, ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>());
        var actual = SceneEvaluator.EvaluateLayer(projected.Layers[0], projected.Subtitles[0], new(1));
        var expected = SceneEvaluator.EvaluateLayer(new PreparedProjectScene(document), layer.Id, new(1));

        Assert.Equal(expected.Transform.Position, actual.Transform.Position);
        Assert.Same(path, projected.Layers[0].MotionPath);
        Assert.Equal(new ScenePoint(15, 7), SceneEvaluator.EvaluateVectorTrack(projected.Layers[0].Tracks[0], new(1)));
    }

    [Fact]
    public void ComponentRotationsAreCanonicalizedWithoutReplacingUnchangedNativeTracks()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var untouched = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.8), new(new(2), 0.8)]);
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End, Tracks = [untouched],
            Effects = [Instance(AnimationProperty.ROTATION_X, "offset(10)", "offset(20)")]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        var projected = AssEffectInstanceProjection.Create(document, ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>());

        Assert.Same(untouched, projected.Layers[0].Tracks.Single(track => track.Property == AnimationProperty.OPACITY));
        var rotation = Assert.Single(projected.Layers[0].Tracks, track => track.Property == AnimationProperty.ROTATION_3D);
        Assert.Equal(new SceneVector3(15, 0, 0), SceneEvaluator.EvaluateVector3Track(rotation, new(1)));
        Assert.DoesNotContain(projected.Layers[0].Tracks, track => track.Property == AnimationProperty.ROTATION_X);
    }

    [Fact]
    public void SubMillisecondInstanceBoundaryReportsItsSamplingLimit()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Effects = [Instance(AnimationProperty.OPACITY, "0.5", "0.5") with { Start = new(1, 3000), End = new(1) }]
        };

        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer] });

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSamplingLimit");
        Assert.Single(layer.Effects);
    }

    [Fact]
    public void SrtAndMuxPreflightReportOnlyEnabledInstanceLoss()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Effects = [Instance(AnimationProperty.OPACITY, "0.5", "0.5")]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        Assert.Contains(SubtitleFormatLossAnalysis.ForSrt(document), diagnostic => diagnostic.Code == "Srt.EffectInstances");
        var disabled = document with { Layers = [layer with { Effects = [layer.Effects[0] with { IsEnabled = false }] }] };
        Assert.DoesNotContain(SubtitleFormatLossAnalysis.ForSrt(disabled), diagnostic => diagnostic.Code == "Srt.EffectInstances");
        Assert.DoesNotContain(AssSubtitleFormat.Write(disabled).Diagnostics, diagnostic => diagnostic.Code == "Ass.EffectInstances");
    }

    [Fact]
    public void TwoHundredFiftySixGraphemeColorGroupsReuseTheirExistingTemporaryRanges()
    {
        var line = new SubtitleLine { Text = new string('x', 256), End = new(2) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Effects = [new()
            {
                Name = "Per character color", End = new(2), Source = """
                    effect "ass-color-groups" version 3
                    short-clip compress
                    scope letters current
                        unit grapheme
                        segment all flex 1
                            at 0 fill rgba(0.1, 0.2, 0.3, 1)
                            at 1 fill rgba(0.1, 0.2, 0.3, 1)
                        end
                    end
                    """
            }]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        var projected = AssEffectInstanceProjection.Create(document, ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>());

        Assert.Equal(256, projected.Subtitles[0].AnimationRanges.Length);
        Assert.Equal(256, projected.Layers[0].Tracks.Length);
        Assert.Empty(line.AnimationRanges);
        Assert.Contains("Dialogue:", AssSubtitleFormat.Write(document).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuousFractionalKnotsDoNotProduceFalseDiscontinuityLimits()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.2), new(new(1, 3), 0.3), new(new(2), 0.8)])],
            Effects = [Instance(AnimationProperty.OPACITY, "factor(0.5)", "factor(0.5)")]
        };
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();

        _ = AssEffectInstanceProjection.Create(new() { Subtitles = [line], Layers = [layer] }, diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSamplingLimit");
    }

    private static EffectInstance Instance(AnimationProperty property, string first, string last)
    {
        var name = property switch
        {
            AnimationProperty.OPACITY => "opacity", AnimationProperty.POSITION => "position",
            AnimationProperty.ROTATION_X => "rotation-x", _ => "font-size"
        };
        return new()
        {
            Name = "ASS test", Start = new(0), End = new(2),
            Source = $$"""
                effect "ass-instance" version 3
                short-clip compress
                scope clip current
                    segment all flex 1
                        at 0 {{name}} {{first}}
                        at 1 {{name}} {{last}}
                    end
                end
                """
        };
    }
}
