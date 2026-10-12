using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssOpacityExportTests
{
    [Theory]
    [InlineData(0, 255)]
    [InlineData(0.8, 51)]
    public void ConstantOpacityUsesAnEventFadeWithoutReplacingAnyStyleAlpha(double opacity, int alpha)
    {
        var line = Line() with
        {
            Style = Line().Style with
            {
                Fill = SceneColor.White with { Alpha = 102 / 255d },
                Stroke = SceneColor.Black with { Alpha = 153 / 255d },
                ShadowColor = SceneColor.Black with { Alpha = 204 / 255d }
            },
            InlineSpans = [new(1, 1, new() { Fill = SceneColor.White with { Alpha = 51 / 255d } })],
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)]
        };
        var layer = Layer(line) with { Opacity = opacity };
        var document = Document(line, layer);
        var written = AssSubtitleFormat.Write(document);
        var baseline = AssSubtitleFormat.Write(Document(line, layer with { Opacity = 1 }));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Contains($"\\fade({alpha},{alpha},{alpha},0,0,0,0)", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"\\fade\("));
        Assert.Equal(AlphaTags(Assert.Single(Bodies(baseline.Text))), AlphaTags(body));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityPrecision");
        Assert.Equal(opacity > 0, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.OpacityComposition"));
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(opacity, layer.Opacity);
    }

    [Fact]
    public void ConstantOpacityTrackOverridesTheLayerValueAndCanRemoveAnOtherwiseUnnecessaryFade()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Opacity = 0.25,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 1), new(new(4), 1)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));

        Assert.DoesNotContain("\\fade(", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\fad(", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityPrecision");
        Assert.Equal(0.25, layer.Opacity);
    }

    [Fact]
    public void LinearEntranceAndExitKeepThePlateauAndUseOneEventEnvelope()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Opacity = 0.2,
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(0), 0), new(new(1, 2), 1), new(new(7, 2), 1), new(new(4), 0)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Contains("\\fade(255,0,255,0,500,3500,4000)", body, StringComparison.Ordinal);
        Assert.Equal(0.5, OpacityAt(body, 250), 12);
        Assert.Equal(1, OpacityAt(body, 2000));
        Assert.Equal(0.5, OpacityAt(body, 3750), 12);
        Assert.Single(Regex.Matches(body, @"\\fade\("));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityApproximation");
    }

    [Fact]
    public void ADelayedSingleRampPreservesTheConstantValuesBeforeAndAfterIt()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(0), 0.2), new(new(1), 0.2), new(new(3), 0.8), new(new(4), 0.8)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Equal(0.2, OpacityAt(body, 500), 12);
        Assert.Equal(0.5, OpacityAt(body, 2000), 12);
        Assert.Equal(0.8, OpacityAt(body, 3500), 12);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityApproximation");
    }

    [Fact]
    public void CollinearLinearKeysDoNotConsumeExtraFadeSegments()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(0), 0), new(new(1, 2), 0.5), new(new(1), 1), new(new(3), 1), new(new(4), 0)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Contains("\\fade(255,0,255,0,1000,3000,4000)", body, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityApproximation");
    }

    [Theory]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("fade-in-out")]
    public void BuiltinFadeScriptsExportTheirFullTimingAndReportTheEasingApproximation(string id)
    {
        var editor = new ProjectEditor();
        var subtitleId = editor.AddSubtitle(new(0), new(4), "fade");
        editor.ApplyEffectScript(subtitleId, BuiltinEffectScripts.Get(id).Script);
        var document = editor.Snapshot;
        var written = AssSubtitleFormat.Write(document);
        var body = Assert.Single(Bodies(written.Text));

        Assert.Single(Regex.Matches(body, @"\\fade\("));
        Assert.Single(written.Diagnostics.Where(diagnostic => diagnostic.SubtitleId == subtitleId && diagnostic.Code == "Ass.OpacityApproximation"));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityAnimation");
        Assert.Equal(id == "fade-out" ? 1 : 0, OpacityAt(body, 0));
        Assert.Equal(1, OpacityAt(body, 2000));
        Assert.Equal(id == "fade-in" ? 1 : 0, OpacityAt(body, 4000));
        Assert.Same(document, editor.Snapshot);
    }

    [Fact]
    public void HoldKeysRemainInstantaneousInsteadOfBecomingLinearRamps()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(0), 0, KeyframeInterpolation.HOLD), new(new(1), 1, KeyframeInterpolation.HOLD), new(new(3), 0)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Contains("\\fade(255,0,255,1000,1000,3000,3000)", body, StringComparison.Ordinal);
        Assert.Equal(0, OpacityAt(body, 999));
        Assert.Equal(1, OpacityAt(body, 1000));
        Assert.Equal(1, OpacityAt(body, 2999));
        Assert.Equal(0, OpacityAt(body, 3000));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityApproximation");
    }

    [Fact]
    public void MoreThanTwoChangingSegmentsAreReportedWithoutFlatteningTheOscillation()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Opacity = 0.2,
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(0), 0), new(new(1), 1), new(new(2), 0), new(new(3), 1)])]
        };
        var document = Document(line, layer);
        var written = AssSubtitleFormat.Write(document);

        Assert.DoesNotContain("\\fade(", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.SubtitleId == line.Id && diagnostic.Code == "Ass.OpacityAnimation");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.SubtitleId == line.Id && diagnostic.Code == "Subtitle.Composition");
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(4, layer.Tracks[0].Keyframes.Length);
    }

    [Fact]
    public void NonoverlappingOrderedOperationsUseTheSameEnvelopeAsKeyframes()
    {
        var line = Line();
        var track = new AnimationTrack(AnimationProperty.OPACITY, [])
        {
            InitialValue = 0.2,
            Transforms = [new(Guid.NewGuid(), new(1), new(2), 1), new(Guid.NewGuid(), new(3), new(4), 0)]
        };
        var written = AssSubtitleFormat.Write(Document(line, Layer(line) with { Tracks = [track] }));
        var body = Assert.Single(Bodies(written.Text));

        Assert.Contains("\\fade(204,0,255,1000,2000,3000,4000)", body, StringComparison.Ordinal);
        Assert.Equal(0.2, OpacityAt(body, 500), 12);
        Assert.Equal(0.6, OpacityAt(body, 1500), 12);
        Assert.Equal(0.5, OpacityAt(body, 3500), 12);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityApproximation");
    }

    [Fact]
    public void OverlappingOrderedOperationsAreNotMistakenForIndependentLinearRamps()
    {
        var line = Line();
        var track = new AnimationTrack(AnimationProperty.OPACITY, [])
        {
            InitialValue = 0,
            Transforms = [new(Guid.NewGuid(), new(0), new(3), 1), new(Guid.NewGuid(), new(1), new(4), 0.2)]
        };
        var written = AssSubtitleFormat.Write(Document(line, Layer(line) with { Tracks = [track] }));

        Assert.DoesNotContain("\\fade(", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.OpacityAnimation");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void MaskSamplesUseTheOriginalContentPhaseInsteadOfRestartingEachFade()
    {
        var line = Line() with { Start = new(10), End = new(12) };
        var layer = Layer(line) with
        {
            AnimationOffset = new(1, 2),
            Mask = new VectorClipMask
            {
                Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }]
            },
            Tracks =
            [
                new(AnimationProperty.OPACITY, [new(new(1, 2), 0), new(new(5, 2), 128 / 255d)]),
                new(AnimationProperty.MASK_POSITION, [new(new(1, 2), new ScenePoint(0, 0)), new(new(5, 2), new ScenePoint(30, 0))])
            ]
        };
        var document = Document(line, layer) with { FrameRate = new(4, 1) };
        var written = AssSubtitleFormat.Write(document, timeOffset: new(3));
        var bodies = Bodies(written.Text);

        Assert.Equal(8, bodies.Length);
        for (var index = 0; index < bodies.Length; index++)
        {
            Assert.Single(Regex.Matches(bodies[index], @"\\fade\("));
            Assert.Equal((index * 16 + 8) / 255d, OpacityAt(bodies[index], 125), 12);
            Assert.All(FadeValues(bodies[index]).Skip(3), value => Assert.True(value >= 0));
        }
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.OpacityPrecision");
    }

    [Fact]
    public void OpacityUsesTheActualQuantizedEventStartWithBothTimeOffsets()
    {
        var line = Line() with { Start = new(11, 1000), End = new(2011, 1000) };
        var layer = Layer(line) with
        {
            AnimationOffset = new(1, 2),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0), new(new(5, 2), 1)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer), timeOffset: new(1, 1000));

        Assert.Contains("Dialogue: 0,0:00:00.01,0:00:02.02", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\fade(255,0,0,2,2002,2002,2002)", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.FadeTimeQuantization");
    }

    [Fact]
    public void AlphaAndSubmillisecondTimesReportTheirQuantizationOncePerSubtitle()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.OPACITY,
                [new(new(1, 3000), 0.5), new(new(5001, 3000), 1), new(new(7, 2), 1), new(new(4), 0.5)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));

        Assert.Single(written.Diagnostics.Where(diagnostic => diagnostic.SubtitleId == line.Id && diagnostic.Code == "Ass.OpacityPrecision"));
        Assert.Single(written.Diagnostics.Where(diagnostic => diagnostic.SubtitleId == line.Id && diagnostic.Code == "Ass.FadeTimeQuantization"));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    private static SubtitleLine Line() => new() { Text = "ab", End = new(4), Style = new() { ShadowBlur = 0 } };

    private static ProjectLayer Layer(SubtitleLine line) => new() { SubtitleId = line.Id, Start = line.Start, End = line.End };

    private static ProjectDocument Document(SubtitleLine line, ProjectLayer layer) => new() { Subtitles = [line], Layers = [layer] };

    private static string[] Bodies(string source) => source.Split('\n')
        .Where(row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).Select(row => row.Split(',', 10)[9]).ToArray();

    private static string[] AlphaTags(string body) => Regex.Matches(body, @"\\[1-4]a&H[0-9A-F]+&")
        .Select(match => match.Value).ToArray();

    private static double[] FadeValues(string body)
    {
        var match = Assert.Single(Regex.Matches(body, @"\\fade\(([^)]+)\)"));
        return match.Groups[1].Value.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static double OpacityAt(string body, double milliseconds)
    {
        var values = FadeValues(body);
        var alpha = values[0];
        if (milliseconds >= values[4])
        {
            alpha = values[1];
        }
        else if (milliseconds >= values[3])
        {
            alpha += (values[1] - values[0]) * (milliseconds - values[3]) / (values[4] - values[3]);
        }
        if (milliseconds >= values[6])
        {
            alpha = values[2];
        }
        else if (milliseconds >= values[5])
        {
            alpha += (values[2] - values[1]) * (milliseconds - values[5]) / (values[6] - values[5]);
        }
        return 1 - alpha / 255;
    }
}
