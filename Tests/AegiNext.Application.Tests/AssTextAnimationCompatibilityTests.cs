using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssTextAnimationCompatibilityTests
{
    private static readonly int[] shadowMasks = [1, 2];
    private static readonly int[] colorMasks = [7, 8];
    [Fact]
    public void DifferentVisibleRunsRetainIndependentTypographyAndGeometryAnimations()
    {
        var document = Import(@"{\fsp2\t(\fsp10\fscx150\frz20)}a{\r\fsp4\t(\fsp20\fscx200\frz40)}b");
        var line = Assert.Single(document.Subtitles);
        var ranges = line.AnimationRanges;
        Assert.Equal(2, ranges.Length);
        var tracks = Assert.Single(document.Layers).Tracks;
        Assert.All(ranges, range => Assert.Equal(SubtitleAnimationPivot.SUBTITLE_ANCHOR, range.Pivot));
        Assert.Equal(6, Value(tracks, AnimationProperty.LETTER_SPACING, ranges[0].Id).Scalar, 9);
        Assert.Equal(12, Value(tracks, AnimationProperty.LETTER_SPACING, ranges[1].Id).Scalar, 9);
        Assert.Equal(1.25, Value(tracks, AnimationProperty.SCALE, ranges[0].Id).Vector.X, 9);
        Assert.Equal(new SceneVector3(0, 0, -20), Value(tracks, AnimationProperty.ROTATION_3D, ranges[1].Id).Vector3);
    }

    [Fact]
    public void RelativeFontSizeUsesOrderedMultiplicationAndShadowAxesPreserveSourceOrder()
    {
        var document = Import(@"{\fs20\xshad2\yshad4\t(\fs+10\xshad10)\t(1000,2000,\fs30\yshad12)}ab");
        var tracks = Assert.Single(document.Layers).Tracks;
        var font = Assert.Single(tracks, track => track.Property == AnimationProperty.FONT_SIZE);
        Assert.Equal(AnimationTransformMode.MULTIPLY_BY, font.Transforms[0].Mode);
        Assert.Equal(30, SceneEvaluator.EvaluateScalarTrack(font, new(1)), 9);
        var shadow = Assert.Single(tracks, track => track.Property == AnimationProperty.SHADOW_OFFSET);
        Assert.Equal(new ScenePoint(6, 4), SceneEvaluator.EvaluateVectorTrack(shadow, new(1)));
        Assert.Equal(shadowMasks, shadow.Transforms.Select(operation => operation.ComponentMask));
    }

    [Fact]
    public void ColorAndAlphaUseIndependentMasksAndEncodedRgbInterpolation()
    {
        var document = Import(@"{\1c&H000000&\1a&H00&\t(\1c&HFFFFFF&)\t(1000,2000,\1a&HFF&)}ab");
        var track = Assert.Single(Assert.Single(document.Layers).Tracks, track => track.Property == AnimationProperty.FILL);
        Assert.Equal(AnimationColorSpace.SRGB, track.ColorSpace);
        Assert.Equal(colorMasks, track.Transforms.Select(operation => operation.ComponentMask));
        var color = SceneEvaluator.EvaluateColorTrack(track, new(1));
        Assert.Equal(0.21404114048223255, color.Red, 9);
        Assert.Equal(1, color.Alpha, 9);
        Assert.Equal(0.5, SceneEvaluator.EvaluateColorTrack(track, new(3, 2)).Alpha, 9);
    }

    [Fact]
    public void FixedBorderBlurAlsoAnimatesShadowWhileBorderCrossingReportsLoss()
    {
        var document = Import(@"{\bord2\t(\blur4)}ab");
        var tracks = Assert.Single(document.Layers).Tracks;
        Assert.Contains(tracks, track => track.Property == AnimationProperty.STROKE_BLUR);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.SHADOW_BLUR);
        var written = AssSubtitleFormat.Write(document);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowBlur");
        var crossing = Parse(@"{\bord0\t(\bord2\blur4)}ab");
        Assert.Contains(crossing.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformAppearanceAnimation");
    }

    [Fact]
    public void ScopedStylesAndEncodedColorOperationsRoundTripThroughAssExport()
    {
        var document = Import(@"{\1c&H000000&\t(\fs40\1c&HFFFFFF&\1a&H80&)}a{\r\t(\fs30\xshad8)}b");
        var written = AssSubtitleFormat.Write(document);
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var originalRanges = document.Subtitles[0].AnimationRanges;
        var copiedRanges = copy.Subtitles[0].AnimationRanges;
        Assert.Equal(originalRanges.Length, copiedRanges.Length);
        Assert.Equal(Value(document.Layers[0].Tracks, AnimationProperty.FONT_SIZE, originalRanges[0].Id),
            Value(copy.Layers[0].Tracks, AnimationProperty.FONT_SIZE, copiedRanges[0].Id));
        var original = Value(document.Layers[0].Tracks, AnimationProperty.FILL, originalRanges[0].Id).Color;
        var copied = Value(copy.Layers[0].Tracks, AnimationProperty.FILL, copiedRanges[0].Id).Color;
        Assert.Equal(original.Red, copied.Red, 4);
        Assert.Equal(original.Alpha, copied.Alpha, 4);
    }

    [Fact]
    public void KaraokePrimarySecondaryAndGlobalAlphaKeepAllFourChannels()
    {
        var document = Import(@"{\k200\1c&H000000&\2c&HFFFFFF&\t(\1c&HFFFFFF&\2c&H000000&)\t(\alpha&H80&)}ab");
        var tracks = document.Layers[0].Tracks;
        Assert.Contains(tracks, track => track.Property == AnimationProperty.FILL && track.Target.State == SubtitleAnimationState.ACTIVE);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.FILL && track.Target.State == SubtitleAnimationState.INACTIVE);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.STROKE);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.SHADOW_COLOR);
        Assert.All(tracks.Where(track => track.InitialValue?.IsColor == true), track =>
            Assert.Equal(1 - 64d / 255, SceneEvaluator.EvaluateColorTrack(track, new(1)).Alpha, 9));
        var secondary = Assert.Single(tracks, track => track.Property == AnimationProperty.FILL && track.Target.State == SubtitleAnimationState.INACTIVE);
        Assert.Equal(0.21404114048223255, SceneEvaluator.EvaluateColorTrack(secondary, new(1)).Red, 9);
    }

    [Theory]
    [InlineData(@"\1a&H40&", 7)]
    [InlineData(@"\1c&HFF0000&", 8)]
    public void StaticColorResetsOnlyItsOwnComponents(string reset, int remainingMask)
    {
        var document = Import(@"{\t(\1c&H000000&\1a&HFF&)" + reset + "}ab");
        var track = Assert.Single(document.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL);
        Assert.Equal(remainingMask, Assert.Single(track.Transforms).ComponentMask);
    }

    [Theory]
    [InlineData(@"{\t(\fs40\xshad8)\r}ab")]
    [InlineData(@"ab{\t(\fs40\xshad8\1c&H000000&)}")]
    [InlineData(@"{\t(\fs40)\fs20}ab")]
    public void ResetsAndInvisibleTailTransformsDoNotCreateAnimations(string text)
    {
        Assert.Empty(Import(text).Layers[0].Tracks);
    }

    [Fact]
    public void NegativeShadowAxesAndMultipleOperationsRemainIndependent()
    {
        var document = Import(@"{\xshad2\yshad4\t(\xshad-6)\t(\yshad12)\t(1000,2000,\shad8)}ab");
        var track = Assert.Single(document.Layers[0].Tracks, track => track.Property == AnimationProperty.SHADOW_OFFSET);
        Assert.Equal(new ScenePoint(0, 8), SceneEvaluator.EvaluateVectorTrack(track, new(1)));
        Assert.Equal(new ScenePoint(2, 9), SceneEvaluator.EvaluateVectorTrack(track, new(3, 2)));
    }

    [Fact]
    public void NoBorderBlurAnimatesFillAndShadowTogether()
    {
        var tracks = Import(@"{\bord0\t(\blur4)}ab").Layers[0].Tracks;
        Assert.Contains(tracks, track => track.Property == AnimationProperty.FILL_BLUR);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.SHADOW_BLUR);
        Assert.DoesNotContain(tracks, track => track.Property == AnimationProperty.STROKE_BLUR);
    }

    [Fact]
    public void NativeLinearColorExportSamplesEncodedRgbWithinOneByte()
    {
        var document = Import("ab");
        var track = new AnimationTrack(AnimationProperty.FILL,
            [new(new(0), SceneColor.Black), new(new(2), SceneColor.White)]);
        document = document with { Layers = [document.Layers[0] with { Tracks = [track] }] };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var converted = Assert.Single(copy.Layers[0].Tracks, candidate => candidate.Property == AnimationProperty.FILL);
        for (var milliseconds = 10; milliseconds < 2000; milliseconds += 37)
        {
            var expected = SceneEvaluator.EvaluateColorTrack(track, new(milliseconds, 1000));
            var actual = SceneEvaluator.EvaluateColorTrack(converted, new(milliseconds, 1000));
            Assert.InRange(Math.Abs(Encode(expected.Red) - Encode(actual.Red)), 0, 1d / 255);
        }
    }

    [Fact]
    public void SamplingBudgetProducesAnExplicitLossDiagnostic()
    {
        var document = Import("ab");
        var line = document.Subtitles[0] with { End = new(20) };
        var keys = Enumerable.Range(0, 5001).Select(index => new Keyframe(new(index, 500),
            index % 2 == 0 ? SceneColor.Black : SceneColor.White)).ToImmutableArray();
        document = document with
        {
            Subtitles = [line],
            Layers = [document.Layers[0] with { End = line.End, Tracks = [new(AnimationProperty.FILL, keys)] }]
        };
        var result = AssSubtitleFormat.Write(document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSamplingLimit");
        Assert.True(result.Text.Split(@"\t(").Length <= 4097);
    }

    [Fact]
    public void AdvancedProjectionPreservesUnchangedTracksAndReplacesExplicitAnimationChanges()
    {
        var document = Import(@"{\t(\fs40)}a{\r\t(\fs30)}b");
        var line = document.Subtitles[0];
        var layer = document.Layers[0];
        var projection = AssTextProjection.Create(line, layer: layer);
        Assert.Contains(@"\t(", projection.Source, StringComparison.Ordinal);
        var unchanged = AssTextProjection.Apply(line, projection.Source, layer: layer);
        Assert.Same(line, unchanged.Line);
        var textEdit = AssTextProjection.Apply(line, projection.Source + "c", layer: layer);
        Assert.Null(textEdit.TextAnimationTracks);
        var edited = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id, textEdit);
        Assert.Equal(line.AnimationRanges[1].Id, edited.Subtitles[0].AnimationRanges[1].Id);
        Assert.True(layer.Tracks.SequenceEqual(edited.Layers[0].Tracks));
        var animationEdit = AssTextProjection.Apply(line, projection.Source.Replace(@"\fs40", @"\fs50", StringComparison.Ordinal), layer: layer);
        Assert.NotNull(animationEdit.TextAnimationTracks);
        var changed = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id, animationEdit);
        Assert.Equal(35, Value(changed.Layers[0].Tracks, AnimationProperty.FONT_SIZE, changed.Subtitles[0].AnimationRanges[0].Id).Scalar, 9);
    }

    [Fact]
    public void NativeInstantAtOriginExportsItsCompletedValueWithoutAZeroEndTransform()
    {
        var document = Import("ab");
        var track = new AnimationTrack(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 20,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, MediaTime.Zero, 40)]
        };
        document = document with { Layers = [document.Layers[0] with { Tracks = [track] }] };
        var written = AssSubtitleFormat.Write(document);
        Assert.DoesNotContain(@"\t(0,0,", written.Text, StringComparison.Ordinal);
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var font = Assert.Single(copy.Layers[0].Tracks, candidate => candidate.Property == AnimationProperty.FONT_SIZE);
        Assert.Equal(40, SceneEvaluator.EvaluateScalarTrack(font, MediaTime.Zero));
    }

    [Fact]
    public void NativeRangeCentersAndOverlappingGeometryReportTheirExportLimits()
    {
        var document = Import("ab");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Scale = new(2, 2) };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1) { Rotation = 20 };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [first, second] }] };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.RangePivot");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.RangeOverlap");
    }

    [Fact]
    public void AdvancedProjectionAnimationChangesKeepUnrepresentedNeutralRangeIdentity()
    {
        var document = Import(@"{\t(\fs40)}ab");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        document = document with { Subtitles = [line] };
        var projection = AssTextProjection.Create(line, layer: document.Layers[0]);
        var edited = AssTextProjection.Apply(line,
            projection.Source.Replace(@"\fs40", @"\fs50", StringComparison.Ordinal), layer: document.Layers[0]);
        Assert.Contains(edited.Line.AnimationRanges, candidate => candidate.Id == range.Id);
        ProjectValidator.Validate(ProjectEditingOperations.ApplyAssTextEdit(document, line.Id, edited));
    }

    [Fact]
    public void RotatedShadowAxesUseInverseGeometryAndRoundTripTheirScreenOffsets()
    {
        var document = Import(@"{\frz30\fscx150\fscy200\t(\xshad10)\t(\yshad6)}ab");
        var layer = document.Layers[0];
        var track = Assert.Single(layer.Tracks, candidate => candidate.Property == AnimationProperty.SHADOW_OFFSET);
        var local = SceneEvaluator.EvaluateVectorTrack(track, new(1));
        var radians = layer.Transform.Rotation * Math.PI / 180;
        var x = local.X * layer.Transform.Scale.X;
        var y = local.Y * layer.Transform.Scale.Y;
        Assert.Equal(6, Math.Cos(radians) * x - Math.Sin(radians) * y, 9);
        Assert.Equal(4, Math.Sin(radians) * x + Math.Cos(radians) * y, 9);
        var written = AssSubtitleFormat.Write(document);
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var converted = Assert.Single(copy.Layers[0].Tracks, candidate => candidate.Property == AnimationProperty.SHADOW_OFFSET);
        var actual = SceneEvaluator.EvaluateVectorTrack(converted, new(1));
        Assert.Equal(local.X, actual.X, 6);
        Assert.Equal(local.Y, actual.Y, 6);
    }

    [Fact]
    public void NativeSrgbAlphaMultiplicationExportsItsEvaluatedValues()
    {
        var document = Import("ab");
        var track = new AnimationTrack(AnimationProperty.FILL, [])
        {
            InitialValue = SceneColor.White with { Alpha = 0.8 }, ColorSpace = AnimationColorSpace.SRGB,
            Transforms = [new(Guid.NewGuid(), new(0), new(2), SceneColor.White with { Alpha = 0.5 })
            {
                ComponentMask = 8, Mode = AnimationTransformMode.MULTIPLY_BY
            }]
        };
        document = document with { Layers = [document.Layers[0] with { Tracks = [track] }] };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
        var copy = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var converted = Assert.Single(copy.Layers[0].Tracks, candidate => candidate.Property == AnimationProperty.FILL);
        Assert.InRange(Math.Abs(SceneEvaluator.EvaluateColorTrack(converted, new(1)).Alpha - 0.6), 0, 1d / 255);
    }

    [Fact]
    public void UnalignedInstantStrokeAndShadowUseOrdinaryTracksWithoutFalseLossReports()
    {
        var parsed = Parse(@"{\k25}{\t(200,200,\bord6\xshad8\3a&H80&)\k50}a");
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        var clip = Assert.Single(parsed.Clips);
        var border = Assert.Single(clip.Tracks, candidate => candidate.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Equal(2, SceneEvaluator.EvaluateScalarTrack(border, new(199, 1000)));
        Assert.Equal(6, SceneEvaluator.EvaluateScalarTrack(border, new(200, 1000)));
        var shadow = Assert.Single(clip.Tracks, candidate => candidate.Property == AnimationProperty.SHADOW_OFFSET);
        Assert.Equal(8, SceneEvaluator.EvaluateVectorTrack(shadow, new(200, 1000)).X);
    }

    [Fact]
    public void AdvancedTextAnimationRewritesPreserveWholeLayerGeometryTracks()
    {
        var document = Import(@"{\t(\fs40)}ab");
        var geometry = new AnimationTrack(AnimationProperty.SCALE,
            [new(new(0), new ScenePoint(1, 1)), new(new(2), new ScenePoint(2, 1.5))]);
        var opacity = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 1), new(new(2), 0.5)]);
        var layer = document.Layers[0] with { Tracks = document.Layers[0].Tracks.Add(geometry).Add(opacity) };
        document = document with { Layers = [layer] };
        var line = document.Subtitles[0];
        var projection = AssTextProjection.Create(line, layer: layer);
        Assert.DoesNotContain(@"\fscx", projection.Source, StringComparison.Ordinal);
        var unchanged = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id,
            AssTextProjection.Apply(line, projection.Source, layer: layer));
        Assert.Same(document, unchanged);
        var changed = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id,
            AssTextProjection.Apply(line, projection.Source.Replace(@"\fs40", @"\fs50", StringComparison.Ordinal), layer: layer));
        Assert.Same(geometry, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.SCALE));
        Assert.Same(opacity, Assert.Single(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.OPACITY));
        Assert.Equal(35, SceneEvaluator.EvaluateScalarTrack(Assert.Single(changed.Layers[0].Tracks,
            track => track.Property == AnimationProperty.FONT_SIZE), new(1)));
    }

    private static double Encode(double value) => value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1d / 2.4) - 0.055;

    private static AnimationValue Value(IEnumerable<AnimationTrack> tracks, AnimationProperty property, Guid? rangeId) =>
        SceneEvaluator.EvaluateTrack(Assert.Single(tracks, track => track.Property == property && track.Target.TextRangeId == rangeId), new(1));

    private static ProjectDocument Import(string text)
    {
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, Parse(text), "ASS");
        ProjectValidator.Validate(document);
        return document;
    }

    private static AssImportResult Parse(string text) => AssSubtitleFormat.Parse($$"""
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
        Dialogue: 0,0:00:10.00,0:00:12.00,Default,{{text}}
        """, 640, 360);
}
