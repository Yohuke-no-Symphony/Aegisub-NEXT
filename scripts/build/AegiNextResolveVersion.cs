using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

/// <summary>Resolves a release identity and platform versions from one Git snapshot.</summary>
public sealed class AegiNextResolveVersion : Microsoft.Build.Utilities.Task
{
    private const int MAX_COMPONENT = 65534;
    private static readonly Regex tagPattern = new("^rel/(beta|stable)/([0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+)$", RegexOptions.CultureInvariant);

    [Required]
    public string RepositoryRoot { get; set; } = "";
    public string RequestedTag { get; set; } = "";
    public string RequestedVersion { get; set; } = "";
    public bool RequireRelease { get; set; }
    public bool AllowMissingTag { get; set; }
    public string DevelopmentBaseVersion { get; set; } = "0.1.0";
    [Output]
    public string ProductVersion { get; set; } = "";
    [Output]
    public string BaseVersion { get; set; } = "";
    [Output]
    public string ReleaseTag { get; set; } = "";
    [Output]
    public string ReleaseChannel { get; set; } = "dev";
    [Output]
    public string ReleaseSequence { get; set; } = "0";
    [Output]
    public string BuildNumber { get; set; } = "0";
    [Output]
    public string AssemblyVersion { get; set; } = "";
    [Output]
    public string FileVersion { get; set; } = "";
    [Output]
    public string GitSha { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!TryNumericVersion(DevelopmentBaseVersion, false, out var development))
        {
            Log.LogError("Invalid development base version '{0}'.", DevelopmentBaseVersion);
            return false;
        }
        SetVersion(development, "dev");
        if (RequestedVersion.Length != 0 && !TryNumericVersion(RequestedVersion, true, out _))
        {
            Log.LogError("Invalid product version '{0}'. Use four numeric components between 0 and 65534.", RequestedVersion);
            return false;
        }
        if (RequestedTag.Length != 0 && !TryTag(RequestedTag, out _, out _))
        {
            Log.LogError("Invalid release tag '{0}'. Use rel/beta/x.y.z.n or rel/stable/x.y.z.n with components between 0 and 65534.", RequestedTag);
            return false;
        }
        if (AllowMissingTag && RequestedTag.Length == 0)
        {
            Log.LogError("Test packaging requires an explicit -Tag (AegiNextRequestedTag).");
            return false;
        }
        if ((!Directory.Exists(Path.Combine(RepositoryRoot, ".git")) && !File.Exists(Path.Combine(RepositoryRoot, ".git"))) ||
            !TryGit(out var sha, "rev-parse", "HEAD") ||
            !TryGit(out var shallow, "rev-parse", "--is-shallow-repository") || shallow != "false")
        {
            return DevelopmentOrError("Release version requires an available Git repository with complete history.");
        }
        GitSha = sha;
        if (!TryGit(out var countText, "rev-list", "--count", "HEAD") ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return DevelopmentOrError("Cannot determine the Git build number.");
        }
        if (count < 1 || count > MAX_COMPONENT)
        {
            Log.LogError("Git build number '{0}' is outside the supported range 1..65534.", countText);
            return false;
        }
        BuildNumber = count.ToString(CultureInfo.InvariantCulture);
        FileVersion = BaseVersion + "." + BuildNumber;
        if (!TryGit(out var tagsText, "tag", "--points-at", "HEAD"))
        {
            return DevelopmentOrError("Cannot inspect release tags on HEAD.");
        }
        var headTags = tagsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (AllowMissingTag && Array.IndexOf(headTags, RequestedTag) < 0)
        {
            if (!TryGit(out var allTagsText, "tag", "--list"))
            {
                Log.LogError("Cannot inspect existing Git tags for test packaging.");
                return false;
            }
            if (Array.IndexOf(allTagsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), RequestedTag) >= 0)
            {
                Log.LogError("Requested tag '{0}' already exists on a different commit. Test packaging only allows missing tags or tags on HEAD.", RequestedTag);
                return false;
            }
            TryTag(RequestedTag, out var requestedChannel, out var requestedNumeric);
            if (RequestedVersion.Length != 0 && requestedNumeric != Version.Parse(RequestedVersion))
            {
                Log.LogError("Requested version '{0}' does not match test tag '{1}'.", RequestedVersion, RequestedTag);
                return false;
            }
            ReleaseTag = RequestedTag;
            SetVersion(requestedNumeric, requestedChannel);
            return true;
        }
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in headTags)
        {
            if (!TryTag(tag, out var channel, out var numeric))
            {
                continue;
            }
            if (RequestedTag.Length != 0 && tag != RequestedTag)
            {
                continue;
            }
            if (RequestedVersion.Length != 0 && numeric != Version.Parse(RequestedVersion))
            {
                continue;
            }
            matches[channel + "/" + numeric.ToString(4)] = tag;
        }
        if (matches.Count == 0)
        {
            return DevelopmentOrError("HEAD has no matching release tag. Select a tag that points to the current commit.");
        }
        if (matches.Count > 1)
        {
            Log.LogError("HEAD has multiple release identities. Select one explicitly with -Tag (AegiNextRequestedTag).");
            return false;
        }
        foreach (var tag in matches.Values)
        {
            TryTag(tag, out var channel, out var numeric);
            ReleaseTag = tag;
            SetVersion(numeric, channel);
        }
        return true;
    }

    private void SetVersion(Version version, string channel)
    {
        BaseVersion = version.ToString(3);
        ReleaseChannel = channel;
        ReleaseSequence = Math.Max(0, version.Revision).ToString(CultureInfo.InvariantCulture);
        ProductVersion = channel == "beta" ? BaseVersion + "-beta." + ReleaseSequence
            : channel == "stable" ? BaseVersion : BaseVersion + "-dev";
        AssemblyVersion = BaseVersion + ".0";
        FileVersion = BaseVersion + "." + BuildNumber;
    }

    private bool DevelopmentOrError(string message)
    {
        if (RequireRelease || RequestedTag.Length != 0 || RequestedVersion.Length != 0)
        {
            Log.LogError(message);
            return false;
        }
        return true;
    }

    private static bool TryTag(string tag, out string channel, out Version version)
    {
        var match = tagPattern.Match(tag);
        channel = match.Success ? match.Groups[1].Value : "";
        version = null;
        return match.Success && TryNumericVersion(match.Groups[2].Value, true, out version);
    }

    private static bool TryNumericVersion(string text, bool fourParts, out Version version)
    {
        version = null;
        if (!Regex.IsMatch(text, fourParts ? "^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$" : "^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant) ||
            !Version.TryParse(text, out var parsed) || parsed.Major > MAX_COMPONENT || parsed.Minor > MAX_COMPONENT ||
            parsed.Build > MAX_COMPONENT || parsed.Revision > MAX_COMPONENT || parsed.ToString(fourParts ? 4 : 3) != text)
        {
            return false;
        }
        version = parsed;
        return true;
    }

    private bool TryGit(out string output, params string[] arguments)
    {
        output = "";
        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = string.Join(" ", arguments)
            };
            using var process = Process.Start(start);
            if (process == null)
            {
                return false;
            }
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill();
                return false;
            }
            output = standardOutput.GetAwaiter().GetResult().Trim();
            standardError.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is Win32Exception || error is InvalidOperationException || error is IOException)
        {
            return false;
        }
    }
}
