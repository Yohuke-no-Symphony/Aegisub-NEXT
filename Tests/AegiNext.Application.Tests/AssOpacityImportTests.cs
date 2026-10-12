using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssOpacityImportTests
{
    [Fact]
    public void OrdinaryFadUsesEditableLinearKeysAndKeepsTheIndependentChannelAlphas()
    {
        var (parsed, document, layer) = Import("{\\fad(200,300)\\1a&H10&\\2a&H20&\\3a&H30&\\4a&H40&\\k100}字");
        var track = Assert.Single(layer.Tracks);
        var line = Assert.Single(document.Subtitles);
        var style = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);
        Assert.Single(line.Karaoke);
        var karaoke = Assert.Single(line.KaraokeStyleSpans);

        Assert.Equal(AnimationProperty.OPACITY, track.Property);
        Assert.False(track.IsOrdered);
        Assert.Equal(new[] { MediaTime.Zero, new MediaTime(1, 5), new(17, 10), new(2) }, track.Keyframes.Select(key => key.Time));
        Assert.Equal(1, layer.Opacity);
        Assert.Equal(0.5, OpacityAt(layer, new(1, 10)), 12);
        Assert.Equal(1, OpacityAt(layer, new(1)), 12);
        Assert.Equal(0.5, OpacityAt(layer, new(37, 20)), 12);
        Assert.Equal(223d / 255, style.Fill.Alpha, 12);
        Assert.Equal(207d / 255, style.Stroke.Alpha, 12);
        Assert.Equal(191d / 255, style.ShadowColor.Alpha, 12);
        Assert.Equal(239d / 255, karaoke.ActiveStyle!.Fill!.Value.Alpha, 12);
        Assert.Equal(223d / 255, karaoke.InactiveStyle!.Fill!.Value.Alpha, 12);
        Assert.Contains(parsed.Diagnostics, item => item.Code == "Ass.OpacityComposition");
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
    }

    [Fact]
    public void OrdinaryFadeRetainsPartialOpacityAtAllFivePhases()
    {
        var (_, _, layer) = Import("{\\fade(255,51,204,200,600,1200,1800)}字幕");

        Assert.False(Assert.Single(layer.Tracks).IsOrdered);
        Assert.Equal(0, OpacityAt(layer, new(1, 10)), 12);
        Assert.Equal(0.4, OpacityAt(layer, new(2, 5)), 12);
        Assert.Equal(0.8, OpacityAt(layer, new(1)), 12);
        Assert.Equal(0.5, OpacityAt(layer, new(3, 2)), 12);
        Assert.Equal(0.2, OpacityAt(layer, new(19, 10)), 12);
    }

    [Theory]
    [InlineData("\\fad(200,300)\\fade(128,128,128,0,0,0,0)")]
    [InlineData("\\fad(200,300)\\r\\fad(1000,1000)")]
    public void FirstFadeWinsAcrossDifferentTagsAndStyleReset(string tags)
    {
        var (parsed, _, layer) = Import("{" + tags + "}字幕");

        Assert.Equal(0.5, OpacityAt(layer, new(1, 10)), 12);
        Assert.Single(parsed.Diagnostics.Where(item => item.Code == "Ass.DuplicateFade"));
    }

    [Fact]
    public void ZeroLengthVisibleJumpsUseNativeOrderedOperations()
    {
        var (parsed, _, layer) = Import("{\\fade(255,0,255,500,500,1500,1500)}字幕");
        var track = Assert.Single(layer.Tracks);

        Assert.True(track.IsOrdered);
        Assert.All(track.Transforms, operation => Assert.Equal(operation.Start, operation.End));
        Assert.Equal(0, OpacityAt(layer, new(499, 1000)));
        Assert.Equal(1, OpacityAt(layer, new(1, 2)));
        Assert.Equal(1, OpacityAt(layer, new(1499, 1000)));
        Assert.Equal(0, OpacityAt(layer, new(3, 2)));
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.OpacityComposition");
    }

    [Fact]
    public void OverlappingFadPreservesLibassFirstRampPriorityAndItsJump()
    {
        var (_, _, layer) = Import("{\\fad(1500,1500)}字幕");
        var track = Assert.Single(layer.Tracks);

        Assert.True(track.IsOrdered);
        Assert.Contains(track.Transforms, operation => operation.Start == new MediaTime(3, 2) && operation.Start == operation.End);
        Assert.Equal(2d / 3, OpacityAt(layer, new(1)), 12);
        Assert.Equal(1499d / 1500, OpacityAt(layer, new(1499, 1000)), 12);
        Assert.Equal(1d / 3, OpacityAt(layer, new(3, 2)), 12);
        Assert.Equal(1d / 6, OpacityAt(layer, new(7, 4)), 12);
    }

    [Theory]
    [InlineData("\\fad(0,0)")]
    [InlineData("\\fade(0,0,0,0,0,0,0)")]
    public void IdentityFadeDoesNotCreateAnUnnecessaryOpacityTrack(string tag)
    {
        var (parsed, _, layer) = Import("{" + tag + "}字幕");

        Assert.Empty(layer.Tracks);
        Assert.Equal(1, layer.Opacity);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.OpacityComposition");
    }

    [Theory]
    [InlineData(255, false)]
    [InlineData(128, true)]
    public void ConstantFadeOnlyReportsCompositionDifferencesAtPartialOpacity(int alpha, bool hasCompositionDifference)
    {
        var (parsed, _, layer) = Import($"{{\\fade({alpha},{alpha},{alpha},0,0,0,0)}}字幕");

        Assert.Single(Assert.Single(layer.Tracks).Keyframes);
        Assert.Equal(1 - alpha / 255d, OpacityAt(layer, new(1)), 12);
        Assert.Equal(hasCompositionDifference, parsed.Diagnostics.Any(item => item.Code == "Ass.OpacityComposition"));
    }

    [Theory]
    [InlineData("-1000,1000,3000,4000", 0.5, 1)]
    [InlineData("-4000,-3000,-2000,-1000", 0, 0)]
    [InlineData("3000,4000,5000,6000", 0, 0)]
    public void FadeOutsideTheDialogueRetainsItsVisiblePhaseWithinEditableKeyRange(string times,
        double first, double last)
    {
        var (_, document, layer) = Import("{\\fade(255,0,255," + times + ")}字幕");
        ProjectValidator.Validate(document);
        var (minimum, maximum) = LayerAnimationTiming.GetRange(layer);

        Assert.Equal(first, OpacityAt(layer, MediaTime.Zero), 12);
        Assert.Equal(last, OpacityAt(layer, new(2)), 12);
        Assert.All(layer.Tracks.SelectMany(track => track.Keyframes), key => Assert.True(key.Time >= minimum && key.Time <= maximum));
    }

    [Fact]
    public void NegativeKaraokeMovesOpacityPositionAndMaskIntoTheSameContentClock()
    {
        var (_, document, layer) = Import("{\\fad(1000,500)\\move(10,20,110,220,0,1000)\\clip(0,0,100,100)\\t(0,1000,\\clip(100,0,200,100))\\kt-50\\k100}字");
        ProjectValidator.Validate(document);
        var value = Assert.Single(SceneEvaluator.Evaluate(document, layer.Start + new MediaTime(1, 2)));

        Assert.Equal(new MediaTime(1, 2), layer.AnimationOffset);
        Assert.Equal(0.5, value.Opacity, 12);
        Assert.Equal(new ScenePoint(50, 100), value.Transform.Position);
        Assert.Equal(new ScenePoint(50, 0), Assert.IsType<RectangleClipMask>(value.Mask).TopLeft);
        Assert.All(layer.Tracks, track => Assert.Equal(new MediaTime(1, 2), track.Keyframes[0].Time));
    }

    [Theory]
    [InlineData("\\fade(256,0,255,0,100,1000,1500)", "Ass.FadeAlpha")]
    [InlineData("\\fade(-1,0,255,0,100,1000,1500)", "Ass.FadeAlpha")]
    [InlineData("\\fad(-1,100)", "Ass.FadeTiming")]
    [InlineData("\\fad(2147483648,100)", "Ass.FadeTiming")]
    [InlineData("\\fade(255,0,255,0,100)", "Ass.FadeArguments")]
    public void UnsupportedFadeValuesRetainTextAndReportTheSkippedEffect(string tag, string code)
    {
        var (parsed, document, layer) = Import("{" + tag + "}字幕");

        Assert.Equal("字幕", Assert.Single(document.Subtitles).Text);
        Assert.Empty(layer.Tracks);
        Assert.Contains(parsed.Diagnostics, item => item.Code == code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicFadeRoundTripPreservesOpacityWithinAssPrecisionAndDoesNotReplaceChannelAlpha(bool withKaraokeAndMove)
    {
        var body = withKaraokeAndMove
            ? "{\\move(10,20,110,220,0,1500)\\fade(255,32,192,0,500,1500,2000)\\kt-50\\k100}a{\\k100}b"
            : "{\\fade(255,32,192,0,500,1500,2000)}ab";
        var (_, original, originalLayer) = Import(body);
        var written = AssSubtitleFormat.Write(original);
        var parsed = AssSubtitleFormat.Parse(written.Text, original.Width, original.Height);
        var restored = ProjectEditingOperations.ImportSubtitleLines(new() { Width = original.Width, Height = original.Height }, parsed, "ASS");
        ProjectValidator.Validate(restored);
        var restoredLayer = Assert.Single(restored.Layers);
        var before = Assert.Single(original.Subtitles);
        var after = Assert.Single(restored.Subtitles);

        Assert.Equal(before.Style.Fill.Alpha, after.Style.Fill.Alpha, 12);
        Assert.Equal(before.Style.Stroke.Alpha, after.Style.Stroke.Alpha, 12);
        Assert.Equal(before.Style.ShadowColor.Alpha, after.Style.ShadowColor.Alpha, 12);
        for (var milliseconds = 0; milliseconds < 2000; milliseconds += 137)
        {
            var elapsed = new MediaTime(milliseconds, 1000);
            Assert.InRange(Math.Abs(OpacityAt(originalLayer, elapsed) - OpacityAt(restoredLayer, elapsed)), 0, 1d / 255 + 1e-9);
        }
        if (withKaraokeAndMove)
        {
            Assert.Equal(before.Karaoke.Select(segment => KaraokeVisualStyleResolver.RangeStyleAt(before, segment.Utf16Start, KaraokeVisualState.ACTIVE)!.Fill!.Value.Alpha),
                after.Karaoke.Select(segment => KaraokeVisualStyleResolver.RangeStyleAt(after, segment.Utf16Start, KaraokeVisualState.ACTIVE)!.Fill!.Value.Alpha));
            Assert.Equal(before.Karaoke.Select(segment => KaraokeVisualStyleResolver.RangeStyleAt(before, segment.Utf16Start, KaraokeVisualState.INACTIVE)!.Fill!.Value.Alpha),
                after.Karaoke.Select(segment => KaraokeVisualStyleResolver.RangeStyleAt(after, segment.Utf16Start, KaraokeVisualState.INACTIVE)!.Fill!.Value.Alpha));
            var expected = Assert.Single(SceneEvaluator.Evaluate(original, originalLayer.Start + new MediaTime(1))).Transform.Position;
            var actual = Assert.Single(SceneEvaluator.Evaluate(restored, restoredLayer.Start + new MediaTime(1))).Transform.Position;
            Assert.Equal(expected.X, actual.X, 8);
            Assert.Equal(expected.Y, actual.Y, 8);
        }
    }

    [Fact]
    public void ProjectAssTextKeepsOpacityInItsNativeProperties()
    {
        var line = new SubtitleLine { Text = "字幕" };
        var parsed = new AssTextParser(line, new Dictionary<string, AssStyleDefinition>(), SceneColor.White,
            projectSource: true).Parse("{\\fad(100,100)}字幕");

        Assert.Empty(parsed.OpacityTracks);
        Assert.Contains(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
    }

    private static double OpacityAt(ProjectLayer layer, MediaTime elapsed)
    {
        var track = layer.Tracks.FirstOrDefault(candidate => candidate.Property == AnimationProperty.OPACITY);
        return track is null ? layer.Opacity : SceneEvaluator.EvaluateScalarTrack(track, layer.AnimationOffset + elapsed);
    }

    private static (AssImportResult Parsed, ProjectDocument Document, ProjectLayer Layer) Import(string body)
    {
        var parsed = AssSubtitleFormat.Parse(File(body), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        return (parsed, document, Assert.Single(document.Layers));
    }

    private static string File(string body) => """
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H40FFFFFF,&H800000FF,&H20000000,&HC0000000,2,2,2
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:10.00,0:00:12.00,Default,
        """ + body;
}
