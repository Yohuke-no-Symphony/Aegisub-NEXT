using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class NativeKaraokeStyleEditingTests
{
    [Fact]
    public void TextRemappingInheritsTheSameGraphemeSourcesAsOrdinaryStyles()
    {
        var line = new SubtitleLine
        {
            Text = "abcd", KaraokeStyleSpans = [new(0, 2, new() { Fill = SceneColor.Black }),
                new(2, 2, new() { StrokeWidth = 0 }, new() { Fill = SceneColor.Transparent })]
        };
        var insert = new SubtitleTextEditMap(line.Text, 1, 0, "XY");
        var inserted = line with { Text = insert.Text, KaraokeStyleSpans = SubtitleKaraokeStyleEditing.Remap(line.KaraokeStyleSpans, insert) };
        Assert.Equal(new SubtitleKaraokeStyleSpan(0, 4, new() { Fill = SceneColor.Black }), inserted.KaraokeStyleSpans[0]);
        Assert.Equal(4, inserted.KaraokeStyleSpans[1].Utf16Start);
        var delete = new SubtitleTextEditMap(line.Text, 1, 2, "");
        var deleted = line with { Text = delete.Text, KaraokeStyleSpans = SubtitleKaraokeStyleEditing.Remap(line.KaraokeStyleSpans, delete) };
        Assert.Equal("ad", deleted.Text);
        Assert.Equal(1, deleted.KaraokeStyleSpans[0].Utf16Length);
        Assert.Equal(1, deleted.KaraokeStyleSpans[1].Utf16Start);
        Assert.Equal(SceneColor.Transparent, KaraokeVisualStyleResolver.RangeStyleAt(deleted, 1, KaraokeVisualState.INACTIVE)!.Fill);
        ProjectValidator.ValidateSubtitleKaraoke(inserted);
        ProjectValidator.ValidateSubtitleKaraoke(deleted);
    }

    [Fact]
    public void SplittingAndMergingTextRangesRetainsOverridesInsideTheSameTimingGroup()
    {
        var line = new SubtitleLine
        {
            Text = "abcd", Karaoke = [new(0, 4, new(0), new(2), SceneColor.White)],
            KaraokeStyleSpans = [new(1, 2, new() { Fill = SceneColor.Transparent }, new() { StrokeWidth = 0 })]
        };
        var left = SubtitleContentSplitMerge.SplitKaraokeStyleSpans(line.KaraokeStyleSpans, 2, false);
        var right = SubtitleContentSplitMerge.SplitKaraokeStyleSpans(line.KaraokeStyleSpans, 2, true);
        Assert.Equal(new SubtitleKaraokeStyleSpan(1, 1, new() { Fill = SceneColor.Transparent }, new() { StrokeWidth = 0 }), Assert.Single(left));
        Assert.Equal(new SubtitleKaraokeStyleSpan(0, 1, new() { Fill = SceneColor.Transparent }, new() { StrokeWidth = 0 }), Assert.Single(right));
        var joined = SubtitleContentSplitMerge.MergeKaraokeStyleSpans(
            line with { Text = "ab", Karaoke = [], KaraokeStyleSpans = left },
            line with { Text = "cd", Karaoke = [], KaraokeStyleSpans = right }, 2, null);
        Assert.Equal(line.KaraokeStyleSpans.ToArray(), joined.ToArray());
    }

    [Fact]
    public void GroupMergePreservesDifferentDefaultsWithoutReplacingAuthoredRangeFill()
    {
        var authored = new SubtitleKaraokeStyleSpan(1, 1,
            new() { Fill = SceneColor.Transparent, StrokeWidth = 0 }, new() { Fill = SceneColor.Black });
        var result = SubtitleKaraokeStyleEditing.PreserveDefaultFill([authored], new(0, 3, new() { Fill = SceneColor.White }));
        Assert.Equal(3, result.Length);
        Assert.Equal(SceneColor.White, result[0].ActiveStyle!.Fill);
        Assert.Equal(authored, result[1]);
        Assert.Equal(SceneColor.White, result[2].ActiveStyle!.Fill);
        var unchanged = SubtitleKaraokeStyleEditing.PreserveDefaultFill(result, new(0, 3, new() { Fill = SceneColor.Black }));
        Assert.Equal(result, unchanged);
    }

    [Fact]
    public void SentenceMergeRetainsBothDefaultsAndAuthoredActiveAndInactiveOverrides()
    {
        var first = new SubtitleLine
        {
            Text = "a", KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "first",
                new() { Fill = new(4, 0, 1), FillBlur = 2, StrokeWidth = 3 }),
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)]
        };
        var second = new SubtitleLine
        {
            Text = "bc", Style = new() { FillBlur = 9 },
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "second",
                new() { Fill = SceneColor.Black, FillBlur = 4, StrokeWidth = 5 }),
            Karaoke = [new(0, 2, new(0), new(1), SceneColor.White)],
            KaraokeStyleSpans = [new(1, 1, new() { FillBlur = 0 }, new() { Fill = SceneColor.Transparent })]
        };
        var ranges = SubtitleContentSplitMerge.MergeKaraokeStyleSpans(first, second, 1, null);
        var merged = first with { Text = "abc", KaraokeStyle = null, KaraokeStyleSpans = ranges };
        Assert.Equal(first.KaraokeStyle.Fill, KaraokeVisualStyleResolver.RangeStyleAt(merged, 0, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.Equal(4, KaraokeVisualStyleResolver.RangeStyleAt(merged, 1, KaraokeVisualState.ACTIVE)!.FillBlur);
        Assert.Equal(0, KaraokeVisualStyleResolver.RangeStyleAt(merged, 2, KaraokeVisualState.ACTIVE)!.FillBlur);
        Assert.Equal(SceneColor.Transparent, KaraokeVisualStyleResolver.RangeStyleAt(merged, 2, KaraokeVisualState.INACTIVE)!.Fill);
    }
}
