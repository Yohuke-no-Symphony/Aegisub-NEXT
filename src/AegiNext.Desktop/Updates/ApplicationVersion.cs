using System.Reflection;

namespace AegiNext.Desktop.Updates;

internal static class ApplicationVersion
{
    internal static string Current { get; } = typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    internal static ReleaseIdentity CurrentIdentity { get; } = ReadIdentity(
        typeof(ApplicationVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), Current);

    internal static ReleaseIdentity ReadIdentity(IEnumerable<AssemblyMetadataAttribute> metadata, string displayVersion)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in metadata)
        {
            values[attribute.Key] = attribute.Value;
        }
        if (ReleaseIdentity.TryParse(values.GetValueOrDefault("AegiNextReleaseTag"), out var taggedIdentity) &&
            taggedIdentity.Channel != ReleaseChannel.DEVELOPMENT)
        {
            return taggedIdentity;
        }
        var channel = values.GetValueOrDefault("AegiNextReleaseChannel");
        var baseVersion = values.GetValueOrDefault("AegiNextBaseVersion");
        var sequence = values.GetValueOrDefault("AegiNextReleaseSequence");
        var identityText = channel == "dev" ? baseVersion + "-dev" : $"rel/{channel}/{baseVersion}.{sequence}";
        if (ReleaseIdentity.TryParse(identityText, out var metadataIdentity))
        {
            return metadataIdentity;
        }
        return ReleaseIdentity.TryParse(displayVersion, out var identity) ? identity
            : new(new(0, 1, 0), ReleaseChannel.DEVELOPMENT);
    }
}
