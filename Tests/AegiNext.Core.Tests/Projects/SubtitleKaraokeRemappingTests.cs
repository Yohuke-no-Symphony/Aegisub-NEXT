using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleKaraokeRemappingTests
{
    [Fact]
    public void InsertionBetweenActiveAndInactiveGroupsLeavesNewTextUntimed()
    {
        var active = new KaraokeSegment(0, 1, MediaTime.Zero, new(1, 7), SceneColor.White);
        var inactive = new KaraokeSegment(1, 1, new(2, 7), new(3, 7), SceneColor.Black);
        var line = new SubtitleLine { Text = "ab", Karaoke = [active], InactiveKaraoke = [inactive] };
        var map = new SubtitleTextEditMap(line.Text, 1, 0, "XY");

        var result = SubtitleKaraokeRemapping.Remap(line, map);

        Assert.Same(active, Assert.Single(result.Karaoke));
        Assert.Equal(inactive with { Utf16Start = 3 }, Assert.Single(result.InactiveKaraoke));
    }

    [Fact]
    public void GraphemeReplacementPreservesSavedGroupIdentityTimingAndVisuals()
    {
        var segment = new KaraokeSegment(0, 1, new(1, 7), new(2, 7), new(3.25, 0.125, 0.25, 0.731))
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        var line = new SubtitleLine { Text = "ab", InactiveKaraoke = [segment] };
        var map = SubtitleTextEditMap.Between(line.Text, "👩‍💻b");

        var result = SubtitleKaraokeRemapping.Remap(line, map);

        Assert.Empty(result.Karaoke);
        Assert.Equal(segment with { Utf16Length = 5 }, Assert.Single(result.InactiveKaraoke));
    }

    [Fact]
    public void CombiningInsertionAcrossTimingOwnershipIsRejected()
    {
        var line = new SubtitleLine
        {
            Text = "ab", Karaoke = [new(0, 1, MediaTime.Zero, new(1), SceneColor.White)]
        };
        var map = new SubtitleTextEditMap(line.Text, 1, 0, "\u0301");

        Assert.Throws<InvalidOperationException>(() => SubtitleKaraokeRemapping.Remap(line, map));
    }
}
