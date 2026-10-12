using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssBoundaryResetTests
{
    [Theory]
    [InlineData("c")]
    [InlineData("1c")]
    public void EmptyColorTagsRestoreCurrentResetRgbAndKeepEveryChannelAlpha(string primary)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File("{\\rAlternate\\alpha&H10&" +
            "\\1c&H000000&\\2c&HFFFFFF&\\3c&H000000&\\4c&H000000&\\" + primary + "\\2c\\3c\\4c\\k100}ab"), 640, 360).Lines);
        var active = KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE)!;
        var inactive = KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.INACTIVE)!;
        var ordinary = OrdinaryAt(line, 0);

        Assert.Equal(new SceneColor(1, 0, 0, 239d / 255), active.Fill);
        Assert.Equal(new SceneColor(0, 1, 0, 239d / 255), inactive.Fill);
        Assert.Equal(new SceneColor(0, 0, 1, 239d / 255), ordinary.Stroke);
        Assert.Equal(new SceneColor(1, 1, 1, 239d / 255), ordinary.ShadowColor);
        Assert.Single(line.Karaoke);
        Assert.Equal(2, Assert.Single(line.KaraokeStyleSpans).Utf16Length);
    }

    [Theory]
    [InlineData("\\alpha")]
    [InlineData("\\1a\\2a\\3a\\4a")]
    public void EmptyAlphaTagsRestoreIndependentResetAlphaWithoutChangingRgb(string reset)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File("{\\rAlternate" +
            "\\1c&H000000&\\2c&HFFFFFF&\\3c&H0000FF&\\4c&H00FF00&\\alpha&HFF&" + reset + "\\k100}ab"), 640, 360).Lines);

        Assert.Equal(new SceneColor(0, 0, 0, 127d / 255), line.KaraokeStyleSpans[0].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(1, 1, 1, 191d / 255), line.KaraokeStyleSpans[0].InactiveStyle!.Fill);
        Assert.Equal(new SceneColor(1, 0, 0, 223d / 255), OrdinaryAt(line, 0).Stroke);
        Assert.Equal(new SceneColor(0, 1, 0, 159d / 255), OrdinaryAt(line, 0).ShadowColor);
    }

    [Fact]
    public void EmptyShadowAxisRestoresOnlyItsResetAxisAndEmptyShadowRestoresTheWholeVector()
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File("{\\rAlternate\\xshad99\\yshad55\\xshad\\k50}a" +
            "{\\yshad\\k50}b{\\xshad99\\yshad55\\shad\\k50}c"), 1280, 1080).Lines);

        Assert.Equal(new ScenePoint(6, 165), OrdinaryAt(line, 0).ShadowOffset);
        Assert.Equal(new ScenePoint(6, 9), OrdinaryAt(line, 1).ShadowOffset);
        Assert.Equal(new ScenePoint(6, 9), OrdinaryAt(line, 2).ShadowOffset);
        foreach (var segment in line.Karaoke)
        {
            var ordinary = OrdinaryAt(line, segment.Utf16Start);
            var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, null, segment,
                KaraokeVisualStyleResolver.RangeStyleAt(line, segment.Utf16Start, KaraokeVisualState.ACTIVE));
            var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment,
                KaraokeVisualStyleResolver.RangeStyleAt(line, segment.Utf16Start, KaraokeVisualState.INACTIVE));
            Assert.Equal(ordinary.ShadowOffset, active.ShadowOffset);
            Assert.Equal(ordinary.ShadowOffset, inactive.ShadowOffset);
        }
    }

    [Theory]
    [InlineData("an")]
    [InlineData("a")]
    public void EmptyAlignmentUsesTheCurrentResetStyleAndStillWinsOverLaterAlignment(string tag)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\pos(100,200)\\rAlternate\\" + tag + "\\an3}ab"), 640, 360);
        var line = Assert.Single(result.Lines);

        Assert.Equal(TextAlignment.TOP_LEFT, line.Style.Alignment);
        Assert.Equal(new ScenePoint(0, 0), line.Style.Position!.Pivot);
        Assert.Equal(new ScenePoint(100, 200), line.Style.Position.Offset);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DuplicatePlacement");
    }

    private static SubtitleStyle OrdinaryAt(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;
    }
}
