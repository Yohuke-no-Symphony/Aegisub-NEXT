using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssBorderAliasImportTests
{
    [Theory]
    [InlineData(@"\xbord6\ybord6", 6)]
    [InlineData(@"\ybord6\xbord6", 6)]
    [InlineData(@"\xbord2", 2)]
    [InlineData(@"\ybord2", 2)]
    [InlineData(@"\xbord-3\ybord-2", 0)]
    [InlineData(@"\xbord9\ybord9\bord3", 3)]
    [InlineData(@"\xbord6\ybord6\xbord\ybord", 2)]
    [InlineData(@"\rAlternate\xbord6\ybord6\xbord\ybord", 4)]
    public void EqualSourceAxesProduceOneStaticStrokeWithoutIntermediateLoss(string tags, double expected)
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File("{" + tags + "}x"), 640, 360);
        var line = Assert.Single(imported.Lines);

        Assert.Equal(expected, At(line, 0).StrokeWidth);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code is "Ass.BorderAxes" or "Ass.UnsupportedTag");
        Assert.DoesNotContain(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
    }

    [Fact]
    public void StaticAxisPairCancelsEarlierSharedAnimationAndPreservesOtherProperties()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\t(0,2000,\bord10\frz40)\xbord6\ybord6\1c&H0000FF&}x"), 640, 360);
        var clip = Assert.Single(imported.Clips);

        Assert.Equal(6, At(clip.Line, 0).StrokeWidth);
        Assert.Equal(new SceneColor(1, 0, 0, 1 - 128 / 255d),
            At(clip.Line, 0).Fill);
        Assert.DoesNotContain(clip.Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.ROTATION);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
    }

    [Fact]
    public void LaterSharedAnimationStartsFromTheEqualStaticAliasValues()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\xbord6\ybord6\t(0,2000,\bord10)}x"), 640, 360);
        var track = Assert.Single(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);

        Assert.Equal(6, SceneEvaluator.EvaluateScalarTrack(track, MediaTime.Zero));
        Assert.Equal(8, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
        Assert.Equal(10, SceneEvaluator.EvaluateScalarTrack(track, new(2)));
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
    }

    [Fact]
    public void ResetAliasesBeforeBlurAnimationSelectTheFillChannel()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\xbord0\ybord0\t(0,2000,\blur6)}x"), 640, 360);

        Assert.Contains(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.FILL_BLUR);
        Assert.DoesNotContain(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.STROKE_BLUR);
    }

    [Fact]
    public void PartialAxisResetDoesNotKeepAnIncorrectSharedAnimation()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\t(0,2000,\bord10)\xbord2}x"), 640, 360);

        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
        Assert.DoesNotContain(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Equal(2, At(Assert.Single(imported.Lines), 0).StrokeWidth);
    }

    [Fact]
    public void UnequalVisibleRunReportsLossAndLaterEqualRunRecoversTheSourceWidth()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\xbord6}a{\ybord6}b"), 640, 360);
        var line = Assert.Single(imported.Lines);

        Assert.Equal(2, At(line, 0).StrokeWidth);
        Assert.Equal(6, At(line, 1).StrokeWidth);
        Assert.Single(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
    }

    [Fact]
    public void StyleResetCancelsBothSourceAxisOverrides()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\xbord6\rAlternate}x"), 640, 360);

        Assert.Equal(4, At(Assert.Single(imported.Lines), 0).StrokeWidth);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
    }

    [Fact]
    public void IndependentDynamicAxesRemainDiagnosedEvenWhenTheirTargetsMatch()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\t(0,2000,\xbord6\ybord6)}x"), 640, 360);

        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
        Assert.DoesNotContain(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
    }

    [Fact]
    public void EqualAliasesUseTheSameResolutionConversionAsBord()
    {
        var aliases = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\xbord6\ybord6}x"), 1280, 360);
        var common = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\bord6}x"), 1280, 360);

        Assert.Equal(At(Assert.Single(common.Lines), 0).StrokeWidth,
            At(Assert.Single(aliases.Lines), 0).StrokeWidth);
        Assert.Contains(aliases.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderResampling");
        Assert.DoesNotContain(aliases.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderAxes");
    }

    private static SubtitleStyle At(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)
            ?.Style.ApplyTo(line.Style) ?? line.Style;
    }
}
