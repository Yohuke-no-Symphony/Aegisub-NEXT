using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Koumori;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriPreviewRowTests
{
    [Fact]
    public void UneditedDisplayPreservesExactFractionalMediaTail()
    {
        var duration = new MediaTime(224383667, 1000000);
        var row = new KoumoriPreviewRow(new(220), duration, 0.5f);

        Assert.True(row.TryRead(duration, out var start, out var end));
        Assert.Equal(new MediaTime(220), start);
        Assert.Equal(duration, end);
    }

    [Fact]
    public void EditedBoundaryRetainsSubMillisecondPrecisionAndStaysInMedia()
    {
        var duration = new MediaTime(224383667, 1000000);
        var row = new KoumoriPreviewRow(new(220), new(223), 0.5f)
        {
            EndText = 224.38366m.ToString(CultureInfo.CurrentCulture)
        };

        Assert.True(row.TryRead(duration, out _, out var end));
        Assert.Equal(new MediaTime(22438366, 100000), end);
        Assert.True(end <= duration);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("1e300")]
    [InlineData("-1")]
    [InlineData("225")]
    public void InvalidOrOutsideMediaEditsCannotBeApplied(string text)
    {
        var row = new KoumoriPreviewRow(new(220), new(223), 0.5f) { EndText = text };

        Assert.False(row.TryRead(new(224), out _, out _));
    }
}
