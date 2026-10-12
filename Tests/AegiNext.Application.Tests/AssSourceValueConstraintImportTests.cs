using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssSourceValueConstraintImportTests
{
    private const double MAX_ERROR = 1d / 255;
    private static readonly int[] sampleMilliseconds = [0, 500, 1000, 1500, 1999, 2000];

    [Theory]
    [InlineData("", "0", 20)]
    [InlineData("", "-10", 20)]
    [InlineData("", "-20", 20)]
    [InlineData(@"\rOther", "0", 30)]
    [InlineData(@"\rOther", "-10", 30)]
    [InlineData(@"\rOther", "-20", 30)]
    public void StaticNonPositiveFontSizeRestoresTheCurrentResetStyle(string reset, string target, double baseline)
    {
        var document = Import("{" + reset + @"\fs40\fs" + target + "}ab");

        AssertSamples(document, (_, _, time) => AssertScalar(baseline, StyleAt(document, 0, time).FontSize));
    }

    [Theory]
    [InlineData("", "0", false, 0, 20)]
    [InlineData("", "-10", true, 0, 20)]
    [InlineData("", "-20", true, -1, 20)]
    [InlineData(@"\rOther", "0", false, 0, 30)]
    [InlineData(@"\rOther", "-10", true, 0, 30)]
    [InlineData(@"\rOther", "-20", true, -1, 30)]
    public void AnimatedNonPositiveFontSizeResetsAfterEachSourceInterpolation(string reset, string target,
        bool relative, double factor, double baseline)
    {
        var document = Import("{" + reset + @"\fs40\t(0,2000,\fs" + target + ")}ab");

        AssertSamples(document, (_, fraction, time) =>
        {
            var value = relative ? 40 * (1 + (factor - 1) * fraction) : 40 * (1 - fraction);
            AssertScalar(value <= 0 ? baseline : value, StyleAt(document, 0, time).FontSize);
        });
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData(@"\rOther", 30)]
    public void FontSizeResetFeedsTheFollowingOperationInSourceOrder(string reset, double baseline)
    {
        var document = Import("{" + reset + @"\fs40\t(0,2000,\fs-20)\t(0,2000,\fs+10)}ab");

        AssertSamples(document, (_, fraction, time) =>
        {
            var value = 40 * (1 - 2 * fraction);
            AssertScalar((value <= 0 ? baseline : value) * (1 + fraction), StyleAt(document, 0, time).FontSize);
        });
    }

    [Fact]
    public void FontSizeBelowTheNativePositiveFloorReportsItsApproximationAndKeepsTheLaterReset()
    {
        var parsed = Parse(@"{\fs0.02\t(0,2000,\fs0)}ab");
        var document = Import(parsed);

        Assert.InRange(StyleAt(document, 0, new(3, 2)).FontSize, 0.01, 0.02);
        AssertScalar(20, StyleAt(document, 0, new(2)).FontSize);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontSizeRange");
    }

    [Fact]
    public void FontSizeResetLeftLimitRemainsZeroThroughALaterCompletedPositiveMultiplication()
    {
        var snapshot = new AssTextAnimationSnapshot(40,
        [
            new(new(MediaTime.Zero, new(2), 1), -1, Mode: AnimationTransformMode.MULTIPLY_BY, NonPositiveFallback: 20),
            new(new(MediaTime.Zero, new(1), 1), 0.5, Mode: AnimationTransformMode.MULTIPLY_BY, NonPositiveFallback: 20)
        ]);

        Assert.Equal(0, AssSourceAnimationEvaluator.Evaluate(snapshot, new(1), true).Scalar);
        Assert.Equal(10, AssSourceAnimationEvaluator.Evaluate(snapshot, new(1)).Scalar);
    }

    [Fact]
    public void LaterFontSizeInterpolationAboveTheNativeFloorDoesNotReportAnUnusedIntermediateFloorLoss()
    {
        var parsed = Parse(@"{\fs40\t(0,2000,\fs0)\t(0,2000,\fs100)}ab");
        var document = Import(parsed);

        AssertSamples(document, (_, fraction, time) =>
        {
            var first = fraction == 1 ? 20 : 40 * (1 - fraction);
            AssertScalar(first + (100 - first) * fraction, StyleAt(document, 0, time).FontSize);
        });
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontSizeRange");
    }

    [Fact]
    public void FontSizeResetOutsideTheVisibleWindowDoesNotReportAnUnseenFloorLoss()
    {
        var parsed = Parse(@"{\fs40\t(0,200000,\fs0)}ab");
        var document = Import(parsed);

        AssertSamples(document, (_, fraction, time) => AssertScalar(40 * (1 - fraction / 100),
            StyleAt(document, 0, time).FontSize));
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontSizeRange");
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void StaticNegativeScaleClampsOnlyItsOwnAxis(string axis, string otherAxis, bool horizontal)
    {
        var document = Import("{\\" + axis + "-100\\" + otherAxis + "150}ab");

        AssertSamples(document, (_, _, time) => AssertPoint(Axes(horizontal, 0, 1.5), ScaleAt(document, 0, time)));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void NegativeScaleTargetClampsAfterInterpolationAndKeepsTheOtherAxesAnimation(string axis,
        string otherAxis, bool horizontal)
    {
        var document = Import(@"{\t(0,2000,\" + axis + "-100\\" + otherAxis + "300)}ab");

        AssertSamples(document, (_, fraction, time) => AssertPoint(
            Axes(horizontal, Math.Max(1 - 2 * fraction, 0), 1 + 2 * fraction), ScaleAt(document, 0, time)));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void ClampedScaleFeedsTheFollowingOperationInSourceOrder(string axis, string otherAxis, bool horizontal)
    {
        var document = Import("{\\" + otherAxis + @"150\t(0,2000,\" + axis + @"-100)\t(0,2000,\" + axis + "300)}ab");

        AssertSamples(document, (_, fraction, time) =>
        {
            var clamped = Math.Max(1 - 2 * fraction, 0);
            AssertPoint(Axes(horizontal, clamped + (3 - clamped) * fraction, 1.5), ScaleAt(document, 0, time));
        });
    }

    [Theory]
    [InlineData("fscx", "fscy", true, false)]
    [InlineData("fscy", "fscx", false, false)]
    [InlineData("fscx", "fscy", true, true)]
    [InlineData("fscy", "fscx", false, true)]
    public void LocalNegativeScaleKeepsTheLegalWholeLineAxisAndItsTextScope(string wholeLineAxis,
        string localAxis, bool horizontal, bool animated)
    {
        var local = animated ? @"\t(0,2000,\" + localAxis + "-100)" : "\\" + localAxis + "-100";
        var document = Import(@"{\t(0,2000,\" + wholeLineAxis + "200)}a{" + local + "}b");

        AssertSamples(document, (offset, fraction, time) => AssertPoint(Axes(horizontal, 1 + fraction,
            offset == 0 ? 1 : animated ? Math.Max(1 - 2 * fraction, 0) : 0), ScaleAt(document, offset, time)));
    }

    [Fact]
    public void StaticNegativeBorderClampsToZeroWithoutRejectingTheFile()
    {
        var document = Import(@"{\bord-2}ab");

        AssertSamples(document, (_, _, time) => AssertScalar(0, StyleAt(document, 0, time).StrokeWidth));
    }

    [Fact]
    public void NegativeBorderTargetClampsAfterItsInterpolation()
    {
        var document = Import(@"{\bord2\t(0,2000,\bord-2)}ab");

        AssertSamples(document, (_, fraction, time) => AssertScalar(Math.Max(2 - 4 * fraction, 0),
            StyleAt(document, 0, time).StrokeWidth));
    }

    [Fact]
    public void ClampedBorderFeedsTheFollowingOperationInSourceOrder()
    {
        var document = Import(@"{\bord2\t(0,2000,\bord-2)\t(0,2000,\bord6)}ab");

        AssertSamples(document, (_, fraction, time) =>
        {
            var clamped = Math.Max(2 - 4 * fraction, 0);
            AssertScalar(clamped + (6 - clamped) * fraction, StyleAt(document, 0, time).StrokeWidth);
        });
    }

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("100", 100)]
    [InlineData("101", 100)]
    [InlineData("700", 100)]
    public void StaticBlurUsesTheLibassZeroAndHundredBounds(string target, double expected)
    {
        var document = Import(@"{\blur2\blur" + target + "}ab");

        AssertSamples(document, (_, _, time) => AssertBlur(expected, StyleAt(document, 0, time)));
    }

    [Theory]
    [InlineData("-2", -2)]
    [InlineData("100", 100)]
    [InlineData("200", 200)]
    public void BlurAppliesItsSourceBoundsAfterInterpolation(string targetText, double target)
    {
        var document = Import(@"{\blur2\t(0,2000,\blur" + targetText + ")}ab");

        AssertSamples(document, (_, fraction, time) => AssertBlur(Math.Clamp(2 + (target - 2) * fraction, 0, 100),
            StyleAt(document, 0, time)));
    }

    [Theory]
    [InlineData("-2", -2, "6", 6)]
    [InlineData("200", 200, "-10", -10)]
    public void BothBlurBoundsFeedTheFollowingOperationInSourceOrder(string firstText, double first,
        string secondText, double second)
    {
        var document = Import(@"{\blur2\t(0,2000,\blur" + firstText + @")\t(0,2000,\blur" + secondText + ")}ab");

        AssertSamples(document, (_, fraction, time) =>
        {
            var clamped = Math.Clamp(2 + (first - 2) * fraction, 0, 100);
            AssertBlur(Math.Clamp(clamped + (second - clamped) * fraction, 0, 100), StyleAt(document, 0, time));
        });
    }

    [Theory]
    [InlineData(@"{\fs20\t(0,1000,\fs40)\t(500,2000,\fs60)}ab", AnimationProperty.FONT_SIZE)]
    [InlineData(@"{\t(0,1000,\fscx200)\t(500,2000,\fscx300)}ab", AnimationProperty.SCALE)]
    [InlineData(@"{\bord2\t(0,1000,\bord4)\t(500,2000,\bord6)}ab", AnimationProperty.STROKE_WIDTH)]
    [InlineData(@"{\blur2\t(0,1000,\blur4)\t(500,2000,\blur6)}ab", AnimationProperty.FILL_BLUR)]
    public void OrdinaryPositiveValuesKeepTheirOrderedFastPath(string source, AnimationProperty property)
    {
        var parsed = Parse(source);
        var document = Import(parsed);

        Assert.True(Assert.Single(Assert.Single(document.Layers).Tracks, track => track.Property == property).IsOrdered);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.AnimationSampling" or "Ass.AnimationSamplingLimit");
    }

    [Theory]
    [InlineData(@"{\fscx150\t(1000,1000,\fscx-100)}ab", AnimationProperty.SCALE, 1.5)]
    [InlineData(@"{\bord2\t(1000,1000,\bord-2)}ab", AnimationProperty.STROKE_WIDTH, 2)]
    [InlineData(@"{\blur2\t(1000,1000,\blur-2)}ab", AnimationProperty.FILL_BLUR, 2)]
    public void InstantSourceClampsKeepThePreviousValueUntilTheExactBoundary(string source,
        AnimationProperty property, double before)
    {
        var document = Import(source);

        AssertScalar(before, SourceValueAt(document, property, new(1999, 2000)));
        AssertScalar(0, SourceValueAt(document, property, new(1)));
    }

    [Fact]
    public void SourceConstraintsUseTheSameRebasedContentClockAsNegativeKaraoke()
    {
        var document = Import(@"{\kt-100\k200\fs40\t(0,2000,\fs0\fscx-100)}ab");

        Assert.Equal(new MediaTime(1), Assert.Single(document.Layers).AnimationOffset);
        AssertSamples(document, (_, fraction, time) =>
        {
            AssertScalar(fraction == 1 ? 20 : 40 * (1 - fraction), StyleAt(document, 0, time).FontSize);
            AssertPoint(new(Math.Max(1 - 2 * fraction, 0), 1), ScaleAt(document, 0, time));
        });
    }

    private static void AssertSamples(ProjectDocument document, Action<int, double, MediaTime> assertion)
    {
        foreach (var milliseconds in sampleMilliseconds)
        {
            foreach (var offset in new[] { 0, 1 })
            {
                assertion(offset, milliseconds / 2000d, new(milliseconds, 1000));
            }
        }
    }

    private static void AssertScalar(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, MAX_ERROR);

    private static void AssertPoint(ScenePoint expected, ScenePoint actual)
    {
        AssertScalar(expected.X, actual.X);
        AssertScalar(expected.Y, actual.Y);
    }

    private static void AssertBlur(double expected, SubtitleStyle actual)
    {
        AssertScalar(expected, actual.FillBlur / AssBlurConversion.SigmaPerUnit);
        AssertScalar(expected, actual.ShadowBlur / AssBlurConversion.SigmaPerUnit);
    }

    private static ScenePoint Axes(bool horizontal, double first, double second) => horizontal ? new(first, second) : new(second, first);

    private static double SourceValueAt(ProjectDocument document, AnimationProperty property, MediaTime time) => property switch
    {
        AnimationProperty.SCALE => ScaleAt(document, 0, time).X,
        AnimationProperty.STROKE_WIDTH => StyleAt(document, 0, time).StrokeWidth,
        _ => StyleAt(document, 0, time).FillBlur / AssBlurConversion.SigmaPerUnit
    };

    private static SubtitleStyle StyleAt(ProjectDocument document, int offset, MediaTime time)
    {
        var layer = Assert.Single(document.Layers);
        var subtitle = Assert.Single(document.Subtitles);
        var evaluated = SceneEvaluator.EvaluateLayer(layer, subtitle, time + layer.AnimationOffset);
        var span = subtitle.InlineSpans.FirstOrDefault(candidate => candidate.Utf16Start <= offset && offset < candidate.Utf16Start + candidate.Utf16Length);
        var style = span?.Style.ApplyTo(subtitle.Style) ?? subtitle.Style;
        return SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, style, offset, SubtitleAnimationState.NORMAL);
    }

    private static ScenePoint ScaleAt(ProjectDocument document, int offset, MediaTime time)
    {
        var layer = Assert.Single(document.Layers);
        var subtitle = Assert.Single(document.Subtitles);
        var evaluated = SceneEvaluator.EvaluateLayer(layer, subtitle, time + layer.AnimationOffset);
        var scale = evaluated.Transform.Scale;
        foreach (var range in evaluated.AnimationRanges)
        {
            if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length)
            {
                scale = new(scale.X * range.Scale.X, scale.Y * range.Scale.Y);
            }
        }
        return scale;
    }

    private static ProjectDocument Import(string source) => Import(Parse(source));

    private static ProjectDocument Import(AssImportResult parsed)
    {
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
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
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&HFF000000,&H00000000,100,100,0,0,2
        Style: Other,Noto Sans,30,&H00FFFFFF,&H000000FF,&HFF000000,&H00000000,100,100,0,0,2
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,{{text}}
        """, 640, 360);
}
