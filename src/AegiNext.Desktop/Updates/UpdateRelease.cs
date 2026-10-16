namespace AegiNext.Desktop.Updates;

internal sealed record UpdateRelease
{
    internal UpdateRelease(Version version, string tagName, string name, string releaseNotesMarkdown, Uri pageUri,
        ReleaseIdentity? identity = null)
    {
        Identity = identity ?? (ReleaseIdentity.TryParse(tagName, out var parsed) ? parsed
            : new(new(version.Major, version.Minor, version.Build), ReleaseChannel.STABLE, Math.Max(0, version.Revision)));
        TagName = tagName;
        Name = name;
        ReleaseNotesMarkdown = releaseNotesMarkdown;
        PageUri = pageUri;
    }

    internal ReleaseIdentity Identity { get; }
    internal Version Version => Identity.Version;
    internal string TagName { get; }
    internal string Name { get; }
    internal string ReleaseNotesMarkdown { get; }
    internal Uri PageUri { get; }
}
