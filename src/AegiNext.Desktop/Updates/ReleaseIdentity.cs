using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Desktop.Updates;

internal sealed record ReleaseIdentity : IComparable<ReleaseIdentity>
{
    private const int MAX_COMPONENT = 65534;

    internal ReleaseIdentity(Version baseVersion, ReleaseChannel channel, int sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(baseVersion);
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
        if (baseVersion.Major > MAX_COMPONENT || baseVersion.Minor > MAX_COMPONENT ||
            baseVersion.Build is < 0 or > MAX_COMPONENT || baseVersion.Revision > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseVersion));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MAX_COMPONENT);
        if (channel == ReleaseChannel.DEVELOPMENT && sequence != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        BaseVersion = new(baseVersion.Major, baseVersion.Minor, baseVersion.Build);
        Channel = channel;
        Sequence = sequence;
    }

    internal Version BaseVersion { get; }
    internal ReleaseChannel Channel { get; }
    internal int Sequence { get; }
    internal Version Version => new(BaseVersion.Major, BaseVersion.Minor, BaseVersion.Build, Sequence);
    internal string DisplayVersion => Channel switch
    {
        ReleaseChannel.BETA => $"{BaseVersion}-beta.{Sequence}",
        ReleaseChannel.DEVELOPMENT => $"{BaseVersion}-dev",
        _ => BaseVersion.ToString()
    };

    /// <summary>按基础版本、发布渠道和渠道内序号比较发布身份。</summary>
    public int CompareTo(ReleaseIdentity? other)
    {
        if (other is null)
        {
            return 1;
        }
        var comparison = BaseVersion.CompareTo(other.BaseVersion);
        if (comparison != 0)
        {
            return comparison;
        }
        comparison = Channel.CompareTo(other.Channel);
        return comparison != 0 ? comparison : Sequence.CompareTo(other.Sequence);
    }

    internal static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        var channel = ReleaseChannel.STABLE;
        var tagged = text.StartsWith("rel/", StringComparison.Ordinal);
        if (tagged)
        {
            var parts = text.Split('/');
            if (parts.Length != 3 || parts[1] is not ("beta" or "stable"))
            {
                return false;
            }
            channel = parts[1] == "beta" ? ReleaseChannel.BETA : ReleaseChannel.STABLE;
            text = parts[2];
        }
        else
        {
            if (text.StartsWith('v'))
            {
                text = text[1..];
            }
            var suffix = text.IndexOf('-', StringComparison.Ordinal);
            if (suffix >= 0)
            {
                var baseText = text[..suffix];
                if (!TryParseNumeric(baseText, out var baseVersion) || baseVersion.Revision >= 0)
                {
                    return false;
                }
                var qualifier = text[suffix..];
                if (qualifier == "-dev")
                {
                    identity = new(baseVersion, ReleaseChannel.DEVELOPMENT);
                    return true;
                }
                if (!qualifier.StartsWith("-beta.", StringComparison.Ordinal) ||
                    !TryParseNumeric(baseText + "." + qualifier[6..], out var betaVersion))
                {
                    return false;
                }
                identity = new(baseVersion, ReleaseChannel.BETA, betaVersion.Revision);
                return true;
            }
        }
        if (!TryParseNumeric(text, out var version))
        {
            return false;
        }
        identity = new(new(version.Major, version.Minor, version.Build), channel, Math.Max(0, version.Revision));
        return true;
    }

    private static bool TryParseNumeric(string text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (text.Any(character => !char.IsAsciiDigit(character) && character != '.') ||
            !Version.TryParse(text, out var parsed) || parsed.Build < 0 ||
            !string.Equals(text, parsed.ToString(), StringComparison.Ordinal) ||
            parsed.Major > MAX_COMPONENT || parsed.Minor > MAX_COMPONENT ||
            parsed.Build > MAX_COMPONENT || parsed.Revision > MAX_COMPONENT)
        {
            return false;
        }
        version = parsed;
        return true;
    }
}
