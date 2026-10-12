using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssSubtitleScalingTests
{
    [Theory]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(1080, 1920)]
    public void ResampledImportRecordsTargetReferenceSizeIncludingStyleReset(int width, int height)
    {
        var source = AssBoundarySource.File("a{\\rAlternate}b");
        var imported = AssSubtitleFormat.Parse(source, width, height);
        var line = Assert.Single(imported.Lines);

        Assert.Equal(width, line.Style.Scaling.ReferenceWidth);
        Assert.Equal(height, line.Style.Scaling.ReferenceHeight);
        Assert.Equal(1, SubtitleStyleScaling.GetScaleFactor(line.Style.Scaling, width, height));
        Assert.Same(line, SubtitleStyleScaling.ToCanvasUnits(line, width, height));
        var reset = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);
        Assert.Equal(30d * height / 360, reset.FontSize, 9);
        Assert.Equal(line.Style.Scaling, reset.Scaling);
    }

    [Fact]
    public void UnknownStyleFallbackUsesTargetReferenceSize()
    {
        var source = AssBoundarySource.File("ab").Replace("Default,,0,0,0,,ab", "Missing,,0,0,0,,ab", StringComparison.Ordinal);
        var line = Assert.Single(AssSubtitleFormat.Parse(source, 1280, 720).Lines);

        Assert.Equal(new SubtitleScalingSettings { ReferenceWidth = 1280, ReferenceHeight = 720 }, line.Style.Scaling);
        Assert.Equal(40, line.Style.FontSize);
    }

    [Fact]
    public void ExportBakesStyleInlineAndKaraokeDimensionsAndKeepsCanvasPosition()
    {
        var line = Line() with
        {
            Style = Line().Style with
            {
                FontSize = 40, LetterSpacing = 6, StrokeWidth = 4, ShadowOffset = new(8, -10),
                StrokeBlur = 4 * AssBlurConversion.SigmaPerUnit,
                ShadowBlur = 4 * AssBlurConversion.SigmaPerUnit,
                Margins = new(24, 30, 36),
                Position = new() { Anchor = new(0, 0), Pivot = new(0.5, 1), Offset = new(100, 80) }
            },
            InlineSpans = [new(1, 1, new() { FontSize = 60, LetterSpacing = 8 })],
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP }],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 10 })]
        };
        var document = Document(line) with
        {
            Layers = [Layer(line) with { Transform = new() { Position = new(11, 13) } }]
        };
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal)).Split(',');
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 960, 540).Lines);

        Assert.Equal("20", style[2]);
        Assert.Equal("3", style[13]);
        Assert.Equal("2", style[16]);
        Assert.Equal("12", style[19]);
        Assert.Equal("15", style[20]);
        Assert.Equal("18", style[21]);
        Assert.Contains("\\pos(61,53)", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\fs30", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\fsp4", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\bord5", written.Text, StringComparison.Ordinal);
        Assert.Equal(20, imported.Style.FontSize);
        Assert.Equal(line, document.Subtitles[0]);
        Assert.Equal(new ScenePoint(11, 13), document.Layers[0].Transform.Position);
        Assert.Equal(written.Text, AssSubtitleFormat.Write(document).Text);
    }

    [Fact]
    public void ExportScalesAppearanceAnimationValuesAndPreservesFontMultipliersAndMotion()
    {
        var line = Line();
        var font = new AnimationTrack(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 40,
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(1), 80),
                new(Guid.NewGuid(), new(1), new(2), 1.5) { Mode = AnimationTransformMode.MULTIPLY_BY }
            ]
        };
        var spacing = new AnimationTrack(AnimationProperty.LETTER_SPACING,
            [new(new(0), 4), new(new(2), 8)]);
        var shadow = new AnimationTrack(AnimationProperty.SHADOW_OFFSET,
            [new(new(0), new ScenePoint(4, 6)), new(new(2), new ScenePoint(8, 10))]);
        var motion = new AnimationTrack(AnimationProperty.POSITION,
            [new(new(0), new ScenePoint(11, 13)), new(new(2), new ScenePoint(31, 33))]);
        var layer = Layer(line) with { Tracks = [font, spacing, shadow, motion] };
        var document = Document(line) with { Layers = [layer] };
        var written = AssSubtitleFormat.Write(document);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 960, 540).Clips);

        Assert.Contains("\\fs20", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\fs40", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\fs+5", written.Text, StringComparison.Ordinal);
        foreach (var property in new[] { AnimationProperty.FONT_SIZE, AnimationProperty.LETTER_SPACING, AnimationProperty.SHADOW_OFFSET })
        {
            var before = Assert.Single(layer.Tracks, track => track.Property == property);
            var after = Assert.Single(imported.Tracks, track => track.Property == property);
            var expected = SceneEvaluator.EvaluateTrack(before, new(3, 2));
            var actual = SceneEvaluator.EvaluateTrack(after, imported.ContentOffset + new MediaTime(3, 2));
            for (var component = 0; component < expected.ComponentCount; component++)
            {
                Assert.Equal(expected.GetComponent(component) * 0.5, actual.GetComponent(component), 7);
            }
        }
        Assert.Contains("\\move(11,13,31,33,0,2000)", written.Text, StringComparison.Ordinal);
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(1.5, font.Transforms[1].Value.Scalar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedDimensionsMayExceedStorageLimitsWhileSourceAndImportStayStrict(bool requireMeasurement)
    {
        var line = Line() with
        {
            Style = Line().Style with
            {
                FontSize = 4096, StrokeWidth = 4096,
                Position = requireMeasurement ? null : Line().Style.Position
            },
            InlineSpans = [new(1, 1, new() { FontSize = 4096 })],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 4096 })]
        };
        var document = Document(line) with
        {
            Width = 3840, Height = 2160,
            Layers = [Layer(line) with { Transform = new() { Position = new(1, 0) } }]
        };
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(1920, 2100), default, default, new(100, 100)));
        var written = AssSubtitleFormat.Write(document, placementMeasurer: measurer);

        Assert.Contains("\\fs8192", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\bord8192", written.Text, StringComparison.Ordinal);
        if (requireMeasurement)
        {
            var measuredDocument = Assert.IsType<ProjectDocument>(measurer.Document);
            Assert.Same(document, measuredDocument);
            ProjectValidator.Validate(measuredDocument);
            var measured = Assert.IsType<SubtitleLine>(measurer.Line);
            Assert.Equal(8192, measured.Style.FontSize);
            Assert.Equal(8192, Assert.Single(measured.InlineSpans).Style.FontSize);
            Assert.Equal(8192, measured.KaraokeStyleSpans[0].ActiveStyle!.StrokeWidth);
            Assert.Equal(3840, measured.Style.Scaling.ReferenceWidth);
            Assert.Equal(2160, measured.Style.Scaling.ReferenceHeight);
            Assert.Equal(4096, document.Subtitles[0].Style.FontSize);
        }
        else
        {
            Assert.Null(measurer.Line);
        }
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(written.Text, 3840, 2160));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(document with
        {
            Subtitles = [line with { Style = line.Style with { FontSize = 4097 } }]
        }));
    }

    private static SubtitleLine Line() => new()
    {
        Text = "ab", Start = new(0), End = new(2),
        Style = new()
        {
            WrapMode = SubtitleWrapMode.NO_WRAP, ShadowBlur = 0,
            Position = new() { Anchor = new(0, 0), Pivot = new(0.5, 1), Offset = new(0, 0) }
        }
    };

    private static ProjectDocument Document(SubtitleLine line) => new()
    {
        Width = 960, Height = 540, Subtitles = [line], Layers = [Layer(line)]
    };

    private static ProjectLayer Layer(SubtitleLine line) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
    };
}
