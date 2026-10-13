using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssNumericTransformImportTests
{
    [Theory]
    [InlineData("\\t(\\fsp10)", 6)]
    [InlineData("\\t(2,\\fsp10)", 4)]
    [InlineData("\\t(500,1500,\\fsp10)", 6)]
    [InlineData("\\t(500,1500,2,\\fsp10)", 4)]
    [InlineData("\\t(0,0,2,\\fsp10)", 4)]
    public void AllTransformFormsUseAssTimingAndPowerInterpolation(string tags, double expected)
    {
        var document = Import(tags);
        var track = Track(document, AnimationProperty.LETTER_SPACING);

        Assert.False(track.IsOrdered);
        Assert.Equal(expected, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 10);
        Assert.All(track.Keyframes, key => Assert.True(key.Time >= MediaTime.Zero && key.Time <= new MediaTime(2)));
    }

    [Theory]
    [InlineData(-1000, 3000, 2.5, 4, 6.5)]
    [InlineData(-3000, -1000, 10, 10, 10)]
    [InlineData(3000, 5000, 2, 2, 2)]
    public void ClippingPowerCurvesPreservesTheOriginalPhase(int start, int end, double first, double middle, double last)
    {
        var document = Import($"\\t({start},{end},2,\\fsp10)");
        var track = Track(document, AnimationProperty.LETTER_SPACING);

        Assert.Equal(first, SceneEvaluator.EvaluateScalarTrack(track, MediaTime.Zero), 10);
        Assert.Equal(middle, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 10);
        Assert.Equal(last, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 10);
    }

    [Theory]
    [InlineData("\\t(500,500,\\fsp10)", 2, 10, 10)]
    [InlineData("\\t(500,1500,0,\\fsp10)", 2, 10, 10)]
    public void InstantAndZeroAccelerationTransformsUseEditableOrderedOperations(string tags,
        double before, double atBoundary, double after)
    {
        var track = Track(Import(tags), AnimationProperty.LETTER_SPACING);

        Assert.True(track.IsOrdered);
        Assert.Equal(before, SceneEvaluator.EvaluateScalarTrack(track, new(499, 1000)), 10);
        Assert.Equal(atBoundary, SceneEvaluator.EvaluateScalarTrack(track, new(1, 2)), 10);
        Assert.Equal(after, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 10);
    }

    [Fact]
    public void OverlappingTransformsRetainSourceOrderInsteadOfSortingByTime()
    {
        var track = Track(Import("\\fsp0\\t(0,2000,2,\\fsp20)\\t(500,1500,\\fsp10)"), AnimationProperty.LETTER_SPACING);

        Assert.True(track.IsOrdered);
        Assert.Equal(2, track.Transforms.Length);
        Assert.Equal(7.5, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 10);
        Assert.Equal(10, SceneEvaluator.EvaluateScalarTrack(track, new(19, 10)), 10);
    }

    [Theory]
    [InlineData("\\t(0,2000,\\fsp10)\\fsp4")]
    [InlineData("\\t(0,2000,\\fsp10)\\r")]
    [InlineData("\\t(0,2000,\\fsp10)\\t(0,2000,\\fsp)")]
    public void LaterStaticOrResetCancelsOnlyThatProperty(string cancelled)
    {
        var parsed = Parse("{" + cancelled + "\\t(0,2000,\\frz90)}a");
        var clip = Assert.Single(parsed.Clips);

        Assert.DoesNotContain(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.ROTATION);
    }

    [Fact]
    public void IdenticalTransformsRepeatedAfterStyleResetAreSemanticallyTheSameWholeLineAnimation()
    {
        var parsed = Parse("{\\t(0,2000,2,\\fsp10\\frz90)}a{\\r\\t(0,2000,2,\\fsp10\\frz90)}b");
        var tracks = Assert.Single(parsed.Clips).Tracks;

        Assert.Equal(2, tracks.Length);
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(Assert.Single(tracks, track => track.Property == AnimationProperty.LETTER_SPACING), new(1)), 10);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.InlineTransform");
    }

    [Fact]
    public void TrailingResetWithoutVisibleTextDoesNotDiscardTheObservedAnimation()
    {
        var parsed = Parse("{\\t(0,2000,\\fsp10)}a{\\r}");

        Assert.Single(Assert.Single(parsed.Clips).Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
    }

    [Fact]
    public void MixedVisibleRunsKeepTheirInconsistentPropertyInATextRange()
    {
        var parsed = Parse("{\\t(0,2000,\\fsp10\\frz90)}a{\\fsp4}b");
        var clip = Assert.Single(parsed.Clips);

        var spacing = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
        Assert.Equal(Assert.Single(clip.Line.AnimationRanges).Id, spacing.Target.TextRangeId);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.ROTATION);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.InlineTransform");
    }

    [Fact]
    public void SingleAxisAndAlignedVectorTransformsKeepTheUnanimatedAxisAndClock()
    {
        var single = Track(Import("\\fscy150\\t(0,2000,2,\\fscx200)"), AnimationProperty.SCALE);
        var both = Track(Import("\\t(0,2000,2,\\fscx200\\fscy300)"), AnimationProperty.SCALE);

        Assert.Equal(new ScenePoint(1.25, 1.5), SceneEvaluator.EvaluateVectorTrack(single, new(1)));
        Assert.Equal(new ScenePoint(1.25, 1.5), SceneEvaluator.EvaluateVectorTrack(both, new(1)));
    }

    [Fact]
    public void IndependentContinuousAxesUseExactNativeComponentCurves()
    {
        var track = Track(Import("\\t(-500,1500,2,\\fscx300)\\t(500,2500,3,\\fscy500)"), AnimationProperty.SCALE);

        Assert.False(track.IsOrdered);
        Assert.Contains(track.Keyframes, frame => !frame.ComponentCurves.IsEmpty);
        foreach (var milliseconds in new[] { 0, 250, 500, 750, 1000, 1500, 1999 })
        {
            var time = milliseconds / 1000d;
            var actual = SceneEvaluator.EvaluateVectorTrack(track, new(milliseconds, 1000));
            Assert.Equal(1 + 2 * Math.Pow(Math.Clamp((time + 0.5) / 2, 0, 1), 2), actual.X, 10);
            Assert.Equal(1 + 4 * Math.Pow(Math.Clamp((time - 0.5) / 2, 0, 1), 3), actual.Y, 10);
        }
    }

    [Fact]
    public void IndependentOverlappingAxisOperationsKeepTheirOrderedComponentMasks()
    {
        var parsed = Parse("{\\t(0,1500,\\fscx200)\\t(500,2000,\\fscx300)\\t(0,2000,\\fscy400\\fsp10)}a");
        var clip = Assert.Single(parsed.Clips);

        var scale = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.SCALE);
        Assert.True(scale.IsOrdered);
        Assert.Equal(1, scale.Transforms[0].ComponentMask);
        Assert.Equal(2, scale.Transforms[^1].ComponentMask);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
    }

    [Fact]
    public void ZeroScaleEntranceIsValidAndDoesNotProduceNonfiniteAppearance()
    {
        var document = Import("\\fscx0\\fscy0\\t(0,2000,\\fscx100\\fscy100\\bord6)");
        var layer = Assert.Single(document.Layers);
        var evaluated = SceneEvaluator.EvaluateLayer(layer, document.Subtitles[0], new(1));

        Assert.Equal(new ScenePoint(0, 0), layer.Transform.Scale);
        Assert.Equal(new ScenePoint(0.5, 0.5), evaluated.Transform.Scale);
        Assert.Equal(4, evaluated.StrokeWidth);
        Assert.True(double.IsFinite(document.Subtitles[0].Style.StrokeWidth));
    }

    [Theory]
    [InlineData("\\bord0", AnimationProperty.FILL_BLUR)]
    [InlineData("\\bord2", AnimationProperty.STROKE_BLUR)]
    public void BlurAnimationUsesStableBorderPresenceAndAnimatesShadowBlur(string border, AnimationProperty property)
    {
        var parsed = Parse("{" + border + "\\blur2\\t(0,2000,\\blur6)}a");
        var clip = Assert.Single(parsed.Clips);
        var track = Assert.Single(clip.Tracks, track => track.Property == property);

        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, SceneEvaluator.EvaluateScalarTrack(track, new(1)), 10);
        Assert.Equal(2 * AssBlurConversion.SigmaPerUnit, clip.Line.InlineSpans[0].Style.ShadowBlur!.Value, 10);
        var shadow = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.SHADOW_BLUR);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, SceneEvaluator.EvaluateScalarTrack(shadow, new(1)), 10);
    }

    [Fact]
    public void BorderCrossingZeroDropsOnlyTheUnrepresentableBlurRouting()
    {
        var parsed = Parse("{\\bord0\\t(0,2000,\\bord4\\blur6\\fsp10)}a");
        var clip = Assert.Single(parsed.Clips);

        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
        Assert.DoesNotContain(clip.Tracks, track => track.Property is AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformAppearanceAnimation");
    }

    [Fact]
    public void MixedMaskNumericAndUnsupportedTagsAreDispatchedWithoutLosingSupportedProperties()
    {
        var parsed = Parse("{\\clip(0,0,100,100)\\t(0,2000,2,\\clip(100,100,200,200)\\fsp10\\fax30)}a");
        var clip = Assert.Single(parsed.Clips);

        Assert.Equal(3, clip.Tracks.Length);
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING), new(1)), 10);
        Assert.Equal(new ScenePoint(25, 25), SceneEvaluator.EvaluateVectorTrack(Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT), new(1)));
        Assert.Single(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    [Fact]
    public void NegativeKaraokeRebasesNumericMaskAndMoveWithoutChangingTheirVisiblePhase()
    {
        var parsed = Parse("{\\kt-50\\k100\\move(0,0,100,200)\\clip(0,0,100,100)\\t(-1000,3000,2,\\fsp10\\clip(100,100,200,200))}a");
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        ProjectValidator.Validate(document);
        var layer = Assert.Single(document.Layers);

        Assert.Equal(new MediaTime(1, 2), layer.AnimationOffset);
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(Track(document, AnimationProperty.LETTER_SPACING), new(3, 2)), 10);
        Assert.Equal(new ScenePoint(50, 100), SceneEvaluator.EvaluateVectorTrack(Track(document, AnimationProperty.POSITION), new(3, 2)));
        Assert.Equal(new ScenePoint(25, 25), SceneEvaluator.EvaluateVectorTrack(Track(document, AnimationProperty.MASK_RECTANGLE_TOP_LEFT), new(3, 2)));
    }

    [Fact]
    public void MatchingInstantKaraokeAppearanceHasPriorityOverWholeLineBorderAndBlurTracks()
    {
        var parsed = Parse("{\\kt50\\k100\\t(500,500,\\bord6\\blur4\\fsp10)}a");
        var clip = Assert.Single(parsed.Clips);
        var active = Assert.Single(clip.Line.KaraokeStyleSpans).ActiveStyle!;

        Assert.Equal(6, active.StrokeWidth);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit, active.StrokeBlur!.Value, 10);
        Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.LETTER_SPACING);
        Assert.DoesNotContain(clip.Tracks, track => track.Property is AnimationProperty.STROKE_WIDTH or AnimationProperty.STROKE_BLUR);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    [Theory]
    [InlineData("\\t(0,2000,\\fsp5000)")]
    [InlineData("\\t(0,2000,\\bord5000)")]
    [InlineData("\\t(0,2000,\\fscx-10)")]
    [InlineData("\\t(0,2000,\\blur1000)")]
    public void OutOfRangeNumericAnimationOnlyLosesItsOwnProperty(string invalid)
    {
        var parsed = Parse("{" + invalid + "\\t(0,2000,\\frz90)}a");
        var tracks = Assert.Single(parsed.Clips).Tracks;

        Assert.Single(tracks, track => track.Property == AnimationProperty.ROTATION);
        if (invalid.Contains("fscx-10", StringComparison.Ordinal))
        {
            Assert.Equal(0.45, SceneEvaluator.EvaluateVectorTrack(Assert.Single(tracks,
                track => track.Property == AnimationProperty.SCALE), new(1)).X, 9);
            Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
        }
        else if (invalid.Contains("blur1000", StringComparison.Ordinal))
        {
            Assert.Equal(100 * AssBlurConversion.SigmaPerUnit, SceneEvaluator.EvaluateScalarTrack(Assert.Single(tracks,
                track => track.Property == AnimationProperty.STROKE_BLUR), new(1)), 9);
            Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
        }
        else
        {
            Assert.Single(tracks);
            Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
        }
    }

    [Fact]
    public void InvalidScaleAxisKeepsTheOtherAxisAnimation()
    {
        var parsed = Parse("{\\t(0,2000,\\fscx-10\\fscy300)}a");
        var track = Assert.Single(Assert.Single(parsed.Clips).Tracks, track => track.Property == AnimationProperty.SCALE);

        var value = SceneEvaluator.EvaluateVectorTrack(track, new(1));
        Assert.Equal(0.45, value.X, 9);
        Assert.Equal(2, value.Y, 9);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
    }

    [Theory]
    [InlineData("\\t(1500,500,\\fsp10)")]
    [InlineData("\\t(-1,\\fsp10)")]
    [InlineData("\\t(0,2147483648,\\fsp10)")]
    public void UnsupportedTimingKeepsTextAndIndependentValidTransforms(string invalid)
    {
        var parsed = Parse("{" + invalid + "\\t(0,2000,\\frz90)}a");

        Assert.Equal("a", Assert.Single(parsed.Lines).Text);
        Assert.Single(Assert.Single(parsed.Clips).Tracks, track => track.Property == AnimationProperty.ROTATION);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformTiming");
    }

    [Fact]
    public void ProjectSourceDoesNotAcquireExternalWholeLineNumericTransforms()
    {
        var original = new SubtitleLine { Start = new(10), End = new(12) };
        var parsed = new AssTextParser(original, new Dictionary<string, AssStyleDefinition>(), SceneColor.White,
            projectSource: true).Parse("{\\t(0,2000,\\fsp10\\frz90)}a");

        Assert.Empty(parsed.NumericTracks);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    private static AnimationTrack Track(ProjectDocument document, AnimationProperty property) =>
        Assert.Single(Assert.Single(document.Layers).Tracks, track => track.Property == property);

    private static ProjectDocument Import(string tags)
    {
        var parsed = Parse("{" + tags + "}a");
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
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Spacing, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,100,100,2,2,2,2
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:10.00,0:00:12.00,Default,{{text}}
        """, 640, 360);
}
