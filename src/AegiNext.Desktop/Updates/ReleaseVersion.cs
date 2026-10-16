using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Desktop.Updates;

internal static class ReleaseVersion
{
    internal static bool TryParse(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (!ReleaseIdentity.TryParse(text, out var identity))
        {
            return false;
        }
        version = identity.Version;
        return true;
    }
}
