using AegiNext.Core.Editing;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleTextEditMapTests
{
    [Fact]
    public void CombiningInsertionExpandsTheAffectedRangeToACompleteGrapheme()
    {
        var map = new SubtitleTextEditMap("eb", 1, 0, "\u0301");

        Assert.Equal("e\u0301b", map.Text);
        Assert.Equal((0, 1), (map.OldStart, map.OldEnd));
        Assert.Equal((0, 2), (map.NewStart, map.NewEnd));
        Assert.Equal(0, map.StyleSourceOffset(0));
        Assert.Equal(1, map.StyleSourceOffset(2));
        Assert.Equal(2, map.MapBoundary(1));
    }

    [Fact]
    public void BetweenRetainsCommonGraphemesAndMapsOffsetsAcrossDifferentUtf16Lengths()
    {
        var map = SubtitleTextEditMap.Between("a😀b", "a👩‍💻b");

        Assert.Equal("a👩‍💻b", map.Text);
        Assert.Equal((1, 3), (map.OriginalStart, map.OriginalEnd));
        Assert.Equal(6, map.OriginalNewEnd);
        Assert.Equal(1, map.StyleSourceOffset(1));
        Assert.Equal(3, map.StyleSourceOffset(6));
        Assert.Equal(6, map.MapBoundary(3));
    }

    [Theory]
    [InlineData(2, 0, "x")]
    [InlineData(1, 1, "x")]
    public void InvalidGraphemeRangesAreRejected(int start, int length, string replacement)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubtitleTextEditMap("a😀b", start, length, replacement));
    }

    [Theory]
    [InlineData('\uD800')]
    [InlineData('\0')]
    public void InvalidReplacementTextIsRejected(char character)
    {
        var replacement = new string(character, 1);

        Assert.Throws<ArgumentException>(() => new SubtitleTextEditMap("a😀b", 1, 2, replacement));
    }
}
