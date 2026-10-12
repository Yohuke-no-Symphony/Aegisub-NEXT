using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssShadowClampImportTests
{
    private const double MAX_ERROR = 1d / 255;
    private static readonly int[] sampleMilliseconds = [0, 500, 1000, 1500, 2000];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 0)]
    [InlineData(1000, 0)]
    [InlineData(1500, 5)]
    [InlineData(2000, 10)]
    public void ShadClampsAfterInterpolatingNegativeInitialOffset(int milliseconds, double expected)
    {
        var parsed = Parse(@"{\xshad-10\yshad-10\t(0,2000,\shad10)}ab");
        var document = Import(parsed);

        AssertShadow(document, 0, new(milliseconds, 1000), new(expected, expected));
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.AnimationSampling" or "Ass.AnimationSamplingLimit");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(500, 5)]
    [InlineData(1000, 0)]
    [InlineData(1500, 0)]
    [InlineData(2000, 0)]
    public void ShadClampsAfterInterpolatingNegativeTarget(int milliseconds, double expected)
    {
        var document = Import(@"{\xshad10\yshad10\t(0,2000,\shad-10)}ab");

        AssertShadow(document, 0, new(milliseconds, 1000), new(expected, expected));
    }

    [Theory]
    [InlineData("xshad", true)]
    [InlineData("yshad", false)]
    public void LaterAxisOperationCanMakeTheClampedShadResultNegative(string axis, bool horizontal)
    {
        var document = Import(@"{\xshad-10\yshad-10\t(0,2000,\shad10)\t(0,2000,\" + axis + "-6)}ab");

        AssertSamples(document, (_, fraction) =>
        {
            var clamped = Math.Max(-10 + 20 * fraction, 0);
            return Axes(horizontal, clamped + (-6 - clamped) * fraction, clamped);
        });
    }

    [Theory]
    [InlineData("xshad", true)]
    [InlineData("yshad", false)]
    public void SourceOrderChangesTheValueBeforeShadClamps(string axis, bool horizontal)
    {
        var document = Import(@"{\xshad-10\yshad-10\t(0,2000,\" + axis + @"-6)\t(0,2000,\shad10)}ab");

        AssertSamples(document, (_, fraction) => Axes(horizontal,
            Math.Max((-10 + 4 * fraction) * (1 - fraction) + 10 * fraction, 0),
            Math.Max(-10 + 20 * fraction, 0)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 0)]
    [InlineData(1000, 0)]
    [InlineData(1250, 0)]
    [InlineData(1500, 0)]
    [InlineData(1750, 5)]
    [InlineData(2000, 10)]
    public void DelayedShadClampsTheCurrentValueEvenWhenItsProgressIsZero(int milliseconds, double expected)
    {
        var document = Import(@"{\xshad-10\yshad-10\t(1000,2000,\shad10)}ab");

        AssertShadow(document, 0, new(milliseconds, 1000), new(expected, expected));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("0.5", 0.5)]
    public void ShadUsesAccelerationBeforeClamping(string accelerationText, double acceleration)
    {
        var document = Import(@"{\xshad-10\yshad-10\t(0,2000," + accelerationText + @",\shad10)}ab");

        AssertSamples(document, (_, fraction) =>
        {
            var value = Math.Max(-10 + 20 * Math.Pow(fraction, acceleration), 0);
            return new(value, value);
        });
    }

    [Theory]
    [InlineData("xshad", true)]
    [InlineData("yshad", false)]
    public void StaticAxisResetRemovesOnlyItsOwnInheritedShadClamp(string axis, bool horizontal)
    {
        var document = Import(@"{\xshad-10\yshad-10\t(0,2000,\shad10)}a{\" + axis + "-5}b");

        AssertSamples(document, (offset, fraction) =>
        {
            var value = Math.Max(-10 + 20 * fraction, 0);
            return Axes(horizontal, offset == 0 ? value : -5, value);
        });
    }

    [Fact]
    public void ShadClampsInAssCoordinatesBeforeInverseRotationAndScale()
    {
        var document = Import(@"{\frz30\fscx150\fscy200\xshad-10\yshad-4\t(0,2000,\shad10)}ab");

        AssertSamples(document, (_, fraction) => new(Math.Max(-10 + 20 * fraction, 0), Math.Max(-4 + 14 * fraction, 0)));
    }

    [Fact]
    public void ShadSamplingUsesTheRebasedContentClockOfNegativeKaraokeStarts()
    {
        var document = Import(@"{\kt-100\k200\xshad-10\yshad-10\t(0,2000,\shad10)}ab");

        Assert.Equal(new MediaTime(1), Assert.Single(document.Layers).AnimationOffset);
        AssertSamples(document, (_, fraction) =>
        {
            var value = Math.Max(-10 + 20 * fraction, 0);
            return new(value, value);
        });
    }

    [Fact]
    public void PositiveShadThatNeverNeedsClampingKeepsItsOrderedTrack()
    {
        var parsed = Parse(@"{\xshad2\yshad2\t(0,2000,\shad10)}ab");
        var document = Import(parsed);

        Assert.True(Assert.Single(Assert.Single(document.Layers).Tracks,
            track => track.Property == AnimationProperty.SHADOW_OFFSET).IsOrdered);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.AnimationSampling" or "Ass.AnimationSamplingLimit");
        AssertSamples(document, (_, fraction) => new(2 + 8 * fraction, 2 + 8 * fraction));
    }

    [Theory]
    [InlineData("xshad", true)]
    [InlineData("yshad", false)]
    public void IndependentNegativeShadowAxisStaysUnclampedWithoutShad(string axis, bool horizontal)
    {
        var parsed = Parse(@"{\xshad-10\yshad-10\t(0,2000,\" + axis + "-6)}ab");
        var document = Import(parsed);

        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code is "Ass.AnimationSampling" or "Ass.AnimationSamplingLimit");
        AssertSamples(document, (_, fraction) => Axes(horizontal, -10 + 4 * fraction, -10));
    }

    [Fact]
    public void StaticSignedShadClampsItsResult()
    {
        var document = Import(@"{\shad-10}ab");

        AssertSamples(document, (_, _) => new(0, 0));
    }

    [Fact]
    public void EmptyShadInsideTransformRestoresTheStyleAndRemovesInheritedOperations()
    {
        var parsed = Parse(@"{\xshad-10\yshad-10\t(0,2000,\shad10)\t(0,2000,\shad)}ab");
        var document = Import(parsed);

        Assert.DoesNotContain(Assert.Single(document.Layers).Tracks, track => track.Property == AnimationProperty.SHADOW_OFFSET);
        AssertSamples(document, (_, _) => new(0, 0));
    }

    [Fact]
    public void InstantShadClampsBeforeItsInstantAndChangesExactlyAtTheBoundary()
    {
        var document = Import(@"{\xshad-10\yshad-10\t(1000,1000,\shad10)}ab");

        AssertShadow(document, 0, new(0), new(0, 0));
        AssertShadow(document, 0, new(1999, 2000), new(0, 0));
        AssertShadow(document, 0, new(1), new(10, 10));
        AssertShadow(document, 0, new(2), new(10, 10));
    }

    [Fact]
    public void InstantNegativeShadTargetClampsAtItsBoundary()
    {
        var document = Import(@"{\xshad10\yshad10\t(1000,1000,\shad-10)}ab");

        AssertShadow(document, 0, new(1999, 2000), new(10, 10));
        AssertShadow(document, 0, new(1), new(0, 0));
        AssertShadow(document, 0, new(2), new(0, 0));
    }

    [Fact]
    public void KaraokeAlignedInstantShadCannotKeepNegativeInactiveOffsetsByExcludingItsNumericOperation()
    {
        var document = Import(@"{\k100}a{\xshad-10\yshad-10\t(1000,1000,\shad10)\k100}b");

        AssertShadow(document, 1, new(0), new(0, 0), SubtitleAnimationState.INACTIVE);
        AssertShadow(document, 1, new(1999, 2000), new(0, 0), SubtitleAnimationState.INACTIVE);
        AssertShadow(document, 1, new(1), new(10, 10), SubtitleAnimationState.ACTIVE);
        AssertShadow(document, 1, new(2), new(10, 10), SubtitleAnimationState.ACTIVE);
    }

    [Theory]
    [InlineData(@"{\xshad-10\yshad-10\t(0,2000,\shad10)}ab", -10, 10)]
    [InlineData(@"{\xshad10\yshad10\t(0,2000,\shad-10)}ab", 10, -10)]
    public void SignedShadRoundTripKeepsTheClampedSourceInterpolation(string source, double initial, double target)
    {
        var copy = RoundTrip(Import(source));

        AssertSamples(copy, (_, fraction) =>
        {
            var value = Math.Max(initial + (target - initial) * fraction, 0);
            return new(value, value);
        });
    }

    [Theory]
    [InlineData("xshad", true)]
    [InlineData("yshad", false)]
    public void ShadRoundTripKeepsTheLaterIndependentNegativeAxis(string axis, bool horizontal)
    {
        var copy = RoundTrip(Import(@"{\xshad-10\yshad-10\t(0,2000,\shad10)\t(0,2000,\" + axis + "-6)}ab"));

        AssertSamples(copy, (_, fraction) =>
        {
            var clamped = Math.Max(-10 + 20 * fraction, 0);
            return Axes(horizontal, clamped + (-6 - clamped) * fraction, clamped);
        });
    }

    [Theory]
    [InlineData(@"{\xshad-10\yshad-10\t(1000,1000,\shad10)}ab", 0, 10)]
    [InlineData(@"{\xshad10\yshad10\t(1000,1000,\shad-10)}ab", 10, 0)]
    public void InstantShadRoundTripKeepsItsHeldValueUntilTheExactBoundary(string source, double before, double after)
    {
        var copy = RoundTrip(Import(source));

        AssertShadow(copy, 0, new(0), new(before, before));
        AssertShadow(copy, 0, new(1999, 2000), new(before, before));
        AssertShadow(copy, 0, new(1), new(after, after));
        AssertShadow(copy, 0, new(2), new(after, after));
    }

    [Fact]
    public void ShadRoundTripKeepsTheRebasedNegativeKaraokeContentClock()
    {
        var copy = RoundTrip(Import(@"{\kt-100\k200\xshad-10\yshad-10\t(0,2000,\shad10)}ab"));

        Assert.Equal(new MediaTime(1), Assert.Single(copy.Layers).AnimationOffset);
        AssertSamples(copy, (_, fraction) =>
        {
            var value = Math.Max(-10 + 20 * fraction, 0);
            return new(value, value);
        });
    }

    [Fact]
    public void ShadRoundTripKeepsTheClampedOffsetsInRotatedAndScaledSourceCoordinates()
    {
        var copy = RoundTrip(Import(@"{\frz30\fscx150\fscy200\xshad-10\yshad-4\t(0,2000,\shad10)}ab"));

        AssertSamples(copy, (_, fraction) => new(Math.Max(-10 + 20 * fraction, 0), Math.Max(-4 + 14 * fraction, 0)));
    }

    private static void AssertSamples(ProjectDocument document, Func<int, double, ScenePoint> expected)
    {
        foreach (var milliseconds in sampleMilliseconds)
        {
            foreach (var offset in new[] { 0, 1 })
            {
                AssertShadow(document, offset, new(milliseconds, 1000), expected(offset, milliseconds / 2000d));
            }
        }
    }

    private static void AssertShadow(ProjectDocument document, int offset, MediaTime contentTime, ScenePoint expected,
        SubtitleAnimationState state = SubtitleAnimationState.NORMAL)
    {
        var layer = Assert.Single(document.Layers);
        var subtitle = Assert.Single(document.Subtitles);
        var evaluated = SceneEvaluator.EvaluateLayer(layer, subtitle, contentTime + layer.AnimationOffset);
        var style = subtitle.Style;
        var span = subtitle.InlineSpans.FirstOrDefault(candidate => candidate.Utf16Start <= offset && offset < candidate.Utf16Start + candidate.Utf16Length);
        if (span is not null)
        {
            style = span.Style.ApplyTo(style);
        }
        if (state != SubtitleAnimationState.NORMAL)
        {
            style = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, style, offset, SubtitleAnimationState.NORMAL);
        }
        if (state == SubtitleAnimationState.ACTIVE)
        {
            var segment = subtitle.Karaoke.FirstOrDefault(candidate => candidate.Utf16Start <= offset && offset < candidate.Utf16Start + candidate.Utf16Length);
            style = KaraokeVisualStyleResolver.ResolveActive(style, subtitle.KaraokeStyle, segment,
                KaraokeVisualStyleResolver.RangeStyleAt(subtitle, offset, KaraokeVisualState.ACTIVE));
        }
        else if (state == SubtitleAnimationState.INACTIVE)
        {
            style = KaraokeVisualStyleResolver.RangeStyleAt(subtitle, offset, KaraokeVisualState.INACTIVE)?.ApplyTo(style) ?? style;
        }
        var shadow = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, style, offset, state).ShadowOffset;
        var radians = evaluated.Transform.Rotation * Math.PI / 180;
        var x = shadow.X * evaluated.Transform.Scale.X;
        var y = shadow.Y * evaluated.Transform.Scale.Y;
        var actual = new ScenePoint(Math.Cos(radians) * x - Math.Sin(radians) * y,
            Math.Sin(radians) * x + Math.Cos(radians) * y);
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, MAX_ERROR);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, MAX_ERROR);
    }

    private static ScenePoint Axes(bool horizontal, double first, double second) => horizontal ? new(first, second) : new(second, first);

    private static ProjectDocument RoundTrip(ProjectDocument document) =>
        Import(AssSubtitleFormat.Parse(AssSubtitleFormat.Write(document).Text, 640, 360));

    private static ProjectDocument Import(string text) => Import(Parse(text));

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
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,{{text}}
        """, 640, 360);
}
