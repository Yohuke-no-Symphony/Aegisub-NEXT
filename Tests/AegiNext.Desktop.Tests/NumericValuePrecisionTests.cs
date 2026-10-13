using System.Globalization;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class NumericValuePrecisionTests
{
    [Theory]
    [InlineData(10.123456789012345)]
    [InlineData(1e-100)]
    [InlineData(1e100)]
    public void DoublePrecisionLoadRetainsTheRoundTripModelValue(double value)
    {
        var draft = new NumericValueDraft { PreserveDoublePrecision = true };
        draft.Load(1);

        draft.Load(value);

        Assert.Equal(value, double.Parse(draft.RawText, NumberStyles.Float, CultureInfo.CurrentCulture));
        Assert.Equal(value.ToString("R", CultureInfo.CurrentCulture), draft.RawText);
        if (value is 1e-100 or 1e100)
        {
            Assert.Equal(1m, draft.Value);
        }
    }
}
