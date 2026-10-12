using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssOrderedColorPrecisionTests
{
    private static SceneColor ArbitraryColor => new(0.123, 0.456, 0.789, 0.567);
    private static SceneColor QuantizedColor => new(0.12213877222960187, 0.45641102318040466,
        0.7912979403326302, 0.5686274509803921);
    private static SceneColor ByteExactColor => new(0.21586050011389926, 0.05126945837404324,
        0.5271151257058131, 128d / 255);

    [Theory]
    [InlineData(AnimationProperty.FILL, 0, true, true)]
    [InlineData(AnimationProperty.FILL, 7, true, false)]
    [InlineData(AnimationProperty.FILL, 8, false, true)]
    [InlineData(AnimationProperty.FILL, 15, true, true)]
    [InlineData(AnimationProperty.STROKE, 0, true, true)]
    [InlineData(AnimationProperty.STROKE, 7, true, false)]
    [InlineData(AnimationProperty.STROKE, 8, false, true)]
    [InlineData(AnimationProperty.STROKE, 15, true, true)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 0, true, true)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 7, true, false)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 8, false, true)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 15, true, true)]
    public void DirectOrderedTargetsReportQuantizationOnlyForEmittedComponents(
        AnimationProperty property, int mask, bool rgbPrecision, bool alphaPrecision)
    {
        var track = Ordered(property, SceneColor.White, ArbitraryColor, mask);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        AssertPrecision(written, document.Subtitles[0].Id, rgbPrecision, alphaPrecision);
        AssertNoSampling(written);
        var expected = new SceneColor(rgbPrecision ? QuantizedColor.Red : 1,
            rgbPrecision ? QuantizedColor.Green : 1, rgbPrecision ? QuantizedColor.Blue : 1,
            alphaPrecision ? QuantizedColor.Alpha : 1);
        AssertColor(expected, RoundTripColor(written, property, new(2)));
        Assert.Equal(ArbitraryColor, track.Transforms[0].Value.Color);
    }

    [Theory]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.SHADOW_COLOR)]
    public void DirectOrderedInitialValueReportsBothQuantizedChannels(AnimationProperty property)
    {
        var track = Ordered(property, ArbitraryColor, SceneColor.White, 8);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        AssertPrecision(written, document.Subtitles[0].Id, true, true);
        AssertNoSampling(written);
        AssertColor(QuantizedColor, RoundTripColor(written, property, MediaTime.Zero));
        Assert.Equal(ArbitraryColor, track.InitialValue!.Value.Color);
    }

    [Theory]
    [InlineData(AnimationProperty.FILL, 7)]
    [InlineData(AnimationProperty.FILL, 8)]
    [InlineData(AnimationProperty.STROKE, 7)]
    [InlineData(AnimationProperty.STROKE, 8)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 7)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 8)]
    public void UnemittedTargetComponentsDoNotProducePrecisionDiagnostics(AnimationProperty property, int mask)
    {
        var target = mask == 7 ? ByteExactColor with { Alpha = ArbitraryColor.Alpha } :
            ArbitraryColor with { Alpha = ByteExactColor.Alpha };
        var track = Ordered(property, SceneColor.White, target, mask);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        AssertPrecision(written, document.Subtitles[0].Id, false, false);
        AssertNoSampling(written);
        var expected = mask == 7 ? ByteExactColor with { Alpha = 1 } :
            SceneColor.White with { Alpha = ByteExactColor.Alpha };
        AssertColor(expected, RoundTripColor(written, property, new(2)));
    }

    [Theory]
    [InlineData(AnimationProperty.FILL, 0)]
    [InlineData(AnimationProperty.FILL, 7)]
    [InlineData(AnimationProperty.FILL, 8)]
    [InlineData(AnimationProperty.FILL, 15)]
    [InlineData(AnimationProperty.STROKE, 0)]
    [InlineData(AnimationProperty.STROKE, 7)]
    [InlineData(AnimationProperty.STROKE, 8)]
    [InlineData(AnimationProperty.STROKE, 15)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 0)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 7)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 8)]
    [InlineData(AnimationProperty.SHADOW_COLOR, 15)]
    public void ByteRepresentableInitialAndTargetsDoNotReportPrecisionLoss(AnimationProperty property, int mask)
    {
        var track = Ordered(property, ByteExactColor, SceneColor.White, mask);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        AssertPrecision(written, document.Subtitles[0].Id, false, false);
        AssertNoSampling(written);
        AssertColor(ByteExactColor, RoundTripColor(written, property, MediaTime.Zero));
    }

    [Theory]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.SHADOW_COLOR)]
    public void SampledLinearColorRetainsSamplingAndAddsValuePrecisionDiagnostics(AnimationProperty property)
    {
        var track = new AnimationTrack(property,
            [new(MediaTime.Zero, SceneColor.Black), new(new(2), ArbitraryColor)]);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
        AssertPrecision(written, document.Subtitles[0].Id, true, true);
        AssertColor(QuantizedColor, RoundTripColor(written, property, new(2)));
    }

    [Theory]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.SHADOW_COLOR)]
    public void SampledPartialRgbUsesEvaluatedColorsWithoutReportingUnappliedAlpha(AnimationProperty property)
    {
        var track = Ordered(property, SceneColor.White, ArbitraryColor, 1);
        var document = Document(track);

        var written = AssSubtitleFormat.Write(document);

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
        AssertPrecision(written, document.Subtitles[0].Id, true, false);
        AssertColor(new(QuantizedColor.Red, 1, 1, 1), RoundTripColor(written, property, new(2)));
    }

    private static AnimationTrack Ordered(AnimationProperty property, SceneColor initial, SceneColor target, int mask)
    {
        return new(property, [])
        {
            InitialValue = initial,
            ColorSpace = AnimationColorSpace.SRGB,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), target) { ComponentMask = mask }]
        };
    }

    private static void AssertPrecision(SubtitleFormatWriteResult written, Guid subtitleId, bool rgb, bool alpha)
    {
        var colorDiagnostics = written.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.ColorPrecision").ToArray();
        var alphaDiagnostics = written.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.AlphaPrecision").ToArray();
        Assert.Equal(rgb ? 1 : 0, colorDiagnostics.Length);
        Assert.Equal(alpha ? 1 : 0, alphaDiagnostics.Length);
        Assert.All(colorDiagnostics.Concat(alphaDiagnostics), diagnostic => Assert.Equal(subtitleId, diagnostic.SubtitleId));
    }

    private static void AssertNoSampling(SubtitleFormatWriteResult written)
    {
        Assert.DoesNotContain(written.Diagnostics,
            diagnostic => diagnostic.Code is "Ass.AnimationSampling" or "Ass.AnimationSamplingLimit");
    }

    private static void AssertColor(SceneColor expected, SceneColor actual)
    {
        Assert.Equal(expected.Red, actual.Red, 12);
        Assert.Equal(expected.Green, actual.Green, 12);
        Assert.Equal(expected.Blue, actual.Blue, 12);
        Assert.Equal(expected.Alpha, actual.Alpha, 12);
    }

    private static SceneColor RoundTripColor(SubtitleFormatWriteResult written, AnimationProperty property, MediaTime time)
    {
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var track = Assert.Single(copy.Layers[0].Tracks,
            candidate => candidate.Property == property && candidate.Target.State == SubtitleAnimationState.NORMAL);
        return SceneEvaluator.EvaluateColorTrack(track, time);
    }

    private static ProjectDocument Document(AnimationTrack track)
    {
        var source = """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            LayoutResX: 640
            LayoutResY: 360
            WrapStyle: 1
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Spacing, Outline, Shadow, Alignment
            Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,100,100,2,2,2,2
            [Events]
            Format: Layer, Start, End, Style, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,Default,ab
            """;
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(source, 640, 360), "ASS");
        return document with { Layers = [document.Layers[0] with { Tracks = [track] }] };
    }
}
