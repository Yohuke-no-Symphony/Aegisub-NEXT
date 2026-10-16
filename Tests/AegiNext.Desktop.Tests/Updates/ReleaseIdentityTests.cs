using System.Reflection;
using AegiNext.Desktop.Updates;

namespace AegiNext.Desktop.Tests.Updates;

public sealed class ReleaseIdentityTests
{
    [Theory]
    [InlineData("rel/beta/1.2.3.0", "1.2.3-beta.0", (int)ReleaseChannel.BETA, 0)]
    [InlineData("rel/stable/1.2.3.65534", "1.2.3", (int)ReleaseChannel.STABLE, 65534)]
    [InlineData("v1.2.3", "1.2.3", (int)ReleaseChannel.STABLE, 0)]
    [InlineData("1.2.3.4", "1.2.3", (int)ReleaseChannel.STABLE, 4)]
    [InlineData("0.1.0-dev", "0.1.0-dev", (int)ReleaseChannel.DEVELOPMENT, 0)]
    public void ParsingPreservesReleaseIdentity(string text, string display, int channel, int sequence)
    {
        Assert.True(ReleaseIdentity.TryParse(text, out var identity));
        Assert.Equal(new Version(text == "0.1.0-dev" ? "0.1.0" : "1.2.3"), identity.BaseVersion);
        Assert.Equal((ReleaseChannel)channel, identity.Channel);
        Assert.Equal(sequence, identity.Sequence);
        Assert.Equal(display, identity.DisplayVersion);
    }

    [Theory]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.2")]
    [InlineData("1.2.3-beta.65534", "rel/stable/1.2.3.0")]
    [InlineData("rel/stable/1.2.3.1", "rel/stable/1.2.3.2")]
    [InlineData("rel/stable/1.2.3.65534", "1.2.4-beta.0")]
    [InlineData("0.1.0-dev", "rel/beta/0.1.0.0")]
    public void OrderingUsesBaseThenChannelThenSequence(string earlier, string later)
    {
        Assert.True(ReleaseIdentity.TryParse(earlier, out var left));
        Assert.True(ReleaseIdentity.TryParse(later, out var right));

        Assert.True(left.CompareTo(right) < 0);
        Assert.True(right.CompareTo(left) > 0);
        Assert.Equal(0, left.CompareTo(left));
    }

    [Theory]
    [InlineData("rel/nightly/1.2.3.1")]
    [InlineData("rel/Beta/1.2.3.1")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3-beta.1.2")]
    [InlineData("1.2.3-dev.1")]
    [InlineData("1.2.3-beta.65535")]
    [InlineData("65535.2.3")]
    [InlineData("1.65535.3")]
    [InlineData("rel/stable/1.2.65535.1")]
    [InlineData("rel/beta/01.2.3.1")]
    [InlineData("rel/stable/1.02.3.1")]
    [InlineData("rel/beta/1.2.03.1")]
    [InlineData("rel/beta/1.2.3.01")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("01.2.3-dev")]
    [InlineData("v01.2.3")]
    [InlineData("1.2.3.00")]
    public void UnsupportedOrOutOfRangeIdentitiesAreRejected(string text)
    {
        Assert.False(ReleaseIdentity.TryParse(text, out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("beta", 2, "1.2.3-beta.2")]
    [InlineData("stable", 7, "1.2.3")]
    public void AssemblyMetadataRestoresSequenceHiddenByDisplay(string channel, int sequence, string display)
    {
        AssemblyMetadataAttribute[] metadata =
        [
            new("AegiNextReleaseChannel", channel),
            new("AegiNextBaseVersion", "1.2.3"),
            new("AegiNextReleaseSequence", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("AegiNextBuildNumber", "400")
        ];

        var identity = ApplicationVersion.ReadIdentity(metadata, display);

        Assert.Equal(sequence, identity.Sequence);
        Assert.Equal(display, identity.DisplayVersion);
        Assert.Equal(channel == "beta" ? ReleaseChannel.BETA : ReleaseChannel.STABLE, identity.Channel);
    }

    [Fact]
    public void AssemblyTagPreservesStableSequenceAndBuildNumberDoesNotAffectComparison()
    {
        var lowBuild = ApplicationVersion.ReadIdentity(
            [new("AegiNextReleaseTag", "rel/stable/1.2.3.7"), new("AegiNextBuildNumber", "1")], "1.2.3");
        var highBuild = ApplicationVersion.ReadIdentity(
            [new("AegiNextReleaseTag", "rel/stable/1.2.3.7"), new("AegiNextBuildNumber", "65534")], "1.2.3");

        Assert.Equal(7, lowBuild.Sequence);
        Assert.Equal(lowBuild, highBuild);
    }

    [Theory]
    [InlineData("0.1.0-dev", (int)ReleaseChannel.DEVELOPMENT)]
    [InlineData("1.2.3.4", (int)ReleaseChannel.STABLE)]
    [InlineData("development", (int)ReleaseChannel.DEVELOPMENT)]
    public void AssembliesWithoutReleaseMetadataHaveASafeIdentity(string display, int channel)
    {
        var identity = ApplicationVersion.ReadIdentity([], display);

        Assert.Equal((ReleaseChannel)channel, identity.Channel);
    }
}
