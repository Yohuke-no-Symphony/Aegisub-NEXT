using AegiNext.Desktop.Diagnostics;

namespace AegiNext.Desktop.Tests;

public sealed class DebugLogFileTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetValueDisablesTheSink(string? value) => Assert.Null(DebugLogFile.Resolve(value));

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    public void FlagValuesUseTheDefaultLocation(string value)
    {
        var path = DebugLogFile.Resolve(value);

        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("AegiNext", "logs", "debug.log"), path);
    }

    [Fact]
    public void ExplicitPathIsUsedAsGiven()
    {
        var path = Path.Combine(Path.GetTempPath(), "aegi-debug.log");

        Assert.Equal(path, DebugLogFile.Resolve(path));
    }
}
