using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssBlurAnimationProjectionTests
{
    [Theory]
    [InlineData(0, AnimationProperty.FILL_BLUR)]
    [InlineData(2, AnimationProperty.STROKE_BLUR)]
    public void EditingCoupledBlurUpdatesTheVisibleChannelAndShadowWithoutDuplicateTargets(
        int border, AnimationProperty visibleChannel)
    {
        var document = Import($@"{{\bord{border}\t(\blur4)}}ab");

        var changed = Edit(document, @"\blur4", @"\blur6");

        Assert.Equal(3 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, visibleChannel), new(1)), 9);
        Assert.Equal(3 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.SHADOW_BLUR), new(1)), 9);
    }

    [Theory]
    [InlineData(0, AnimationProperty.FILL_BLUR)]
    [InlineData(2, AnimationProperty.STROKE_BLUR)]
    public void RemovingCoupledBlurRemovesBothAnimationChannels(int border, AnimationProperty visibleChannel)
    {
        var document = Import($@"{{\bord{border}\t(\blur4)}}ab");

        var changed = Edit(document, @"\t(0,2000,1,\blur4)", string.Empty);

        Assert.DoesNotContain(changed.Layers[0].Tracks,
            track => track.Property == visibleChannel || track.Property == AnimationProperty.SHADOW_BLUR);
    }

    [Fact]
    public void EditingOneScopedBlurRetainsBothRangeIdentitiesAndTheOtherRangeValue()
    {
        var document = Import(@"{\bord2\t(\blur4)}a{\r\bord2\t(\blur8)}b");
        var originalRanges = document.Subtitles[0].AnimationRanges;
        Assert.Equal(2, originalRanges.Length);

        var changed = Edit(document, @"\blur4", @"\blur6");

        Assert.Equal(originalRanges.Select(range => (range.Id, range.Utf16Start, range.Utf16Length)),
            changed.Subtitles[0].AnimationRanges.Select(range => (range.Id, range.Utf16Start, range.Utf16Length)));
        Assert.Equal(3 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.SHADOW_BLUR, originalRanges[0].Id), new(1)), 9);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.SHADOW_BLUR, originalRanges[1].Id), new(1)), 9);
        Assert.Equal(3 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.STROKE_BLUR, originalRanges[0].Id), new(1)), 9);
        Assert.Equal(4 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.STROKE_BLUR, originalRanges[1].Id), new(1)), 9);
    }

    [Theory]
    [InlineData(@"\fs40", @"\fs50", 2)]
    [InlineData(@"\blur4", @"\blur6", 3)]
    public void EditingRepresentedAnimationPreservesAnIndependentNativeShadowBlur(
        string before, string after, int expectedVisibleBlur)
    {
        var document = Import(@"{\bord2\t(\blur4\fs40)}ab");
        var independent = new AnimationTrack(AnimationProperty.SHADOW_BLUR,
            [new(MediaTime.Zero, 1), new(new(2), 7)]);
        document = ReplaceShadowBlur(document, independent);

        var changed = Edit(document, before, after);

        Assert.Same(independent, Track(changed, AnimationProperty.SHADOW_BLUR));
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(independent, new(1)), 9);
        Assert.Equal(expectedVisibleBlur * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.STROKE_BLUR), new(1)), 9);
    }

    [Fact]
    public void RemovingRepresentedBlurPreservesAnIndependentNativeShadowBlur()
    {
        var document = Import(@"{\bord2\t(\blur4)}ab");
        var independent = new AnimationTrack(AnimationProperty.SHADOW_BLUR,
            [new(MediaTime.Zero, 1), new(new(2), 7)]);
        document = ReplaceShadowBlur(document, independent);

        var changed = Edit(document, @"\t(0,2000,1,\blur4)", string.Empty);

        Assert.Same(independent, Track(changed, AnimationProperty.SHADOW_BLUR));
        Assert.DoesNotContain(changed.Layers[0].Tracks, track => track.Property == AnimationProperty.STROKE_BLUR);
    }

    [Fact]
    public void EditingScopedBlurPreservesAnIndependentNativeShadowAndItsRangeIdentity()
    {
        var document = Import(@"{\bord2\t(\blur4)}a{\r\bord2\t(\blur8)}b");
        var range = document.Subtitles[0].AnimationRanges[0];
        var independent = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: range.Id),
            [new(MediaTime.Zero, 1), new(new(2), 7)]);
        document = ReplaceShadowBlur(document, independent);

        var changed = Edit(document, @"\blur4", @"\blur6");

        Assert.Contains(changed.Subtitles[0].AnimationRanges,
            candidate => candidate.Id == range.Id && candidate.Utf16Start == range.Utf16Start &&
                candidate.Utf16Length == range.Utf16Length);
        Assert.Same(independent, Track(changed, AnimationProperty.SHADOW_BLUR, range.Id));
        Assert.Equal(3 * AssBlurConversion.SigmaPerUnit,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.STROKE_BLUR, range.Id), new(1)), 9);
    }

    [Theory]
    [InlineData(2, AnimationProperty.FILL_BLUR)]
    [InlineData(0, AnimationProperty.STROKE_BLUR)]
    public void MatchingAnUnrepresentedBlurChannelDoesNotMakeNativeShadowBlurReplaceable(
        int border, AnimationProperty hiddenChannel)
    {
        var document = Import($@"{{\bord{border}\t(\fs40)}}ab");
        var hidden = new AnimationTrack(hiddenChannel, [new(MediaTime.Zero, 1), new(new(2), 7)]);
        var independent = hidden with { Target = new(AnimationProperty.SHADOW_BLUR) };
        var layer = document.Layers[0];
        document = document with { Layers = [layer with { Tracks = layer.Tracks.Add(hidden).Add(independent) }] };

        var changed = Edit(document, @"\fs40", @"\fs50");

        Assert.Same(independent, Track(changed, AnimationProperty.SHADOW_BLUR));
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(independent, new(1)), 9);
    }

    [Fact]
    public void EditingFontSizePreservesNativeShadowBlurWhenBorderCrossingMakesBlurUnprojectable()
    {
        var document = Import(@"{\bord0\t(\fs40)}ab");
        var border = new AnimationTrack(AnimationProperty.STROKE_WIDTH,
            [new(MediaTime.Zero, 0), new(new(2), 2)]);
        var fill = new AnimationTrack(AnimationProperty.FILL_BLUR,
            [new(MediaTime.Zero, 1), new(new(2), 7)]);
        var shadow = fill with { Target = new(AnimationProperty.SHADOW_BLUR) };
        var layer = document.Layers[0];
        document = document with { Layers = [layer with { Tracks = layer.Tracks.Add(border).Add(fill).Add(shadow) }] };

        var changed = Edit(document, @"\fs40", @"\fs50");

        Assert.Same(shadow, Track(changed, AnimationProperty.SHADOW_BLUR));
        Assert.Equal(4, SceneEvaluator.EvaluateScalarTrack(shadow, new(1)), 9);
        Assert.Equal(35,
            SceneEvaluator.EvaluateScalarTrack(Track(changed, AnimationProperty.FONT_SIZE), new(1)), 9);
    }

    [Fact]
    public void EditingFontSizePreservesShadowPrecedenceAndIdentitiesForRangesWithTheSameBounds()
    {
        var document = Import(@"{\bord2\t(\fs40)}ab");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var last = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var firstShadow = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: first.Id),
            [new(MediaTime.Zero, 9), new(new(2), 11)]);
        var lastBorder = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_BLUR, TextRangeId: last.Id),
            [new(MediaTime.Zero, 2), new(new(2), 6)]);
        var lastShadow = lastBorder with { Target = lastBorder.Target with { Property = AnimationProperty.SHADOW_BLUR } };
        var line = document.Subtitles[0] with { AnimationRanges = [first, last] };
        var layer = document.Layers[0];
        document = document with
        {
            Subtitles = [line],
            Layers = [layer with { Tracks = layer.Tracks.Add(firstShadow).Add(lastBorder).Add(lastShadow) }]
        };
        ProjectValidator.Validate(document);

        var changed = Edit(document, @"\fs40", @"\fs50");

        var changedLine = changed.Subtitles[0];
        var evaluated = SceneEvaluator.EvaluateLayer(changed.Layers[0], changedLine, new(1));
        var style = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, changedLine.Style, 0,
            SubtitleAnimationState.NORMAL);
        Assert.Equal(4, style.ShadowBlur, 9);
        Assert.Equal(new[] { first.Id, last.Id }, changedLine.AnimationRanges.Select(range => range.Id));
        Assert.Same(firstShadow, Track(changed, AnimationProperty.SHADOW_BLUR, first.Id));
        Assert.Same(lastShadow, Track(changed, AnimationProperty.SHADOW_BLUR, last.Id));
    }

    [Fact]
    public void RestoredDuplicateBoundsRemainBeforeTheFollowingNativeShadowRange()
    {
        var document = Import(@"{\bord2\t(\fs40)}ab");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var last = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var firstShadow = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: first.Id),
            [new(MediaTime.Zero, 9), new(new(2), 11)]);
        var secondBorder = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_BLUR, TextRangeId: second.Id),
            [new(MediaTime.Zero, 2), new(new(2), 6)]);
        var secondShadow = secondBorder with { Target = secondBorder.Target with { Property = AnimationProperty.SHADOW_BLUR } };
        var lastShadow = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: last.Id),
            [new(MediaTime.Zero, 20), new(new(2), 22)]);
        var line = document.Subtitles[0] with { AnimationRanges = [first, second, last] };
        var layer = document.Layers[0];
        document = document with
        {
            Subtitles = [line],
            Layers = [layer with { Tracks = layer.Tracks.Add(firstShadow).Add(secondBorder).Add(secondShadow).Add(lastShadow) }]
        };
        ProjectValidator.Validate(document);

        var changed = Edit(document, @"\fs40", @"\fs50");

        var changedLine = changed.Subtitles[0];
        var evaluated = SceneEvaluator.EvaluateLayer(changed.Layers[0], changedLine, new(1));
        for (var offset = 0; offset < changedLine.Text.Length; offset++)
        {
            var style = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, changedLine.Style, offset,
                SubtitleAnimationState.NORMAL);
            Assert.Equal(21, style.ShadowBlur, 9);
        }
        Assert.Equal(new[] { first.Id, second.Id, last.Id }, changedLine.AnimationRanges.Select(range => range.Id));
        Assert.Same(firstShadow, Track(changed, AnimationProperty.SHADOW_BLUR, first.Id));
        Assert.Same(secondShadow, Track(changed, AnimationProperty.SHADOW_BLUR, second.Id));
        Assert.Same(lastShadow, Track(changed, AnimationProperty.SHADOW_BLUR, last.Id));
    }

    [Fact]
    public void EditingFontSizePreservesNativeGeometryForUnprojectableShadowRanges()
    {
        var document = Import(@"{\bord2\t(\fs40)}ab");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Scale = new(2, 1.5), Rotation = 10, Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR
        };
        var last = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Scale = new(3, 2), Rotation = 20, Pivot = SubtitleAnimationPivot.RANGE_CENTER
        };
        var firstShadow = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: first.Id),
            [new(MediaTime.Zero, 9), new(new(2), 11)]);
        var lastBorder = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_BLUR, TextRangeId: last.Id),
            [new(MediaTime.Zero, 2), new(new(2), 6)]);
        var lastShadow = lastBorder with { Target = lastBorder.Target with { Property = AnimationProperty.SHADOW_BLUR } };
        var line = document.Subtitles[0] with { AnimationRanges = [first, last] };
        var layer = document.Layers[0];
        document = document with
        {
            Subtitles = [line],
            Layers = [layer with { Tracks = layer.Tracks.Add(firstShadow).Add(lastBorder).Add(lastShadow) }]
        };
        ProjectValidator.Validate(document);

        var changed = Edit(document, @"\fs40", @"\fs50");

        Assert.Equal(new[] { first, last }, changed.Subtitles[0].AnimationRanges);
        Assert.DoesNotContain(changed.Layers[0].Tracks,
            track => track.Property is AnimationProperty.SCALE or AnimationProperty.ROTATION &&
                (track.Target.TextRangeId == first.Id || track.Target.TextRangeId == last.Id));
        Assert.Same(firstShadow, Track(changed, AnimationProperty.SHADOW_BLUR, first.Id));
        Assert.Same(lastShadow, Track(changed, AnimationProperty.SHADOW_BLUR, last.Id));
    }

    [Fact]
    public void EditingUniqueRangeGeometryKeepsIndependentShadowWhileApplyingTheRequestedScale()
    {
        var document = Import(@"{\bord2\t(\blur4)}a{\r\bord2\t(\blur8)}b");
        var line = document.Subtitles[0];
        var range = line.AnimationRanges[0] with { Scale = new(2, 1) };
        var independent = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.SHADOW_BLUR, TextRangeId: range.Id),
            [new(MediaTime.Zero, 1), new(new(2), 7)]);
        document = document with { Subtitles = [line with { AnimationRanges = line.AnimationRanges.SetItem(0, range) }] };
        document = ReplaceShadowBlur(document, independent);
        ProjectValidator.Validate(document);

        var changed = Edit(document, @"\fscx200", @"\fscx300");

        var changedRange = Assert.Single(changed.Subtitles[0].AnimationRanges, candidate => candidate.Id == range.Id);
        Assert.Equal(3, changedRange.Scale.X, 9);
        Assert.Equal(range.Pivot, changedRange.Pivot);
        Assert.Same(independent, Track(changed, AnimationProperty.SHADOW_BLUR, range.Id));
    }

    private static ProjectDocument ReplaceShadowBlur(ProjectDocument document, AnimationTrack shadow)
    {
        var layer = document.Layers[0];
        var tracks = layer.Tracks.Select(track => track.Target == shadow.Target ? shadow : track).ToImmutableArray();
        return document with { Layers = [layer with { Tracks = tracks }] };
    }

    private static AnimationTrack Track(ProjectDocument document, AnimationProperty property, Guid? rangeId = null)
    {
        return Assert.Single(document.Layers[0].Tracks,
            track => track.Property == property && track.Target.TextRangeId == rangeId &&
                track.Target.State == SubtitleAnimationState.NORMAL);
    }

    private static ProjectDocument Edit(ProjectDocument document, string before, string after)
    {
        var line = document.Subtitles[0];
        var layer = document.Layers[0];
        var projection = AssTextProjection.Create(line, layer: layer);
        Assert.Contains(before, projection.Source, StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line,
            projection.Source.Replace(before, after, StringComparison.Ordinal), layer: layer);
        var changed = ProjectEditingOperations.ApplyAssTextEdit(document, line.Id, edited);
        ProjectValidator.Validate(changed);
        return changed;
    }

    private static ProjectDocument Import(string text)
    {
        var source = $$"""
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
            """;
        return ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(source, 640, 360), "ASS");
    }
}
