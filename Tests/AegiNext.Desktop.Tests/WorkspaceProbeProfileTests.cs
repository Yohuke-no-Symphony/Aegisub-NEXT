using AegiNext.Desktop.Diagnostics;

namespace AegiNext.Desktop.Tests;

public sealed class WorkspaceProbeProfileTests
{
    private static readonly string[] copiedFileNames = ["preferences.json", "subtitle-styles.aegistyles"];

    [Fact]
    public void OnlyPreferencesAndStylesAreCopiedAndProbeWritesCannotChangeTheSource()
    {
        var source = Path.Combine(Path.GetTempPath(), "AegiNext.Probe.Source.Tests", Guid.NewGuid().ToString("N"));
        var options = WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-source-profile", source]);
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "preferences.json"), "original preferences");
            File.WriteAllText(Path.Combine(source, "subtitle-styles.aegistyles"), "original styles");
            File.WriteAllText(Path.Combine(source, "layouts.json"), "original layouts");

            WorkspaceProbeProfile.Prepare(options);

            Assert.Equal(copiedFileNames,
                Directory.GetFiles(options.ProfileDirectory).Select(Path.GetFileName).Order().ToArray());
            Assert.Equal("original preferences", File.ReadAllText(Path.Combine(options.ProfileDirectory, "preferences.json")));
            Assert.Equal("original styles", File.ReadAllText(Path.Combine(options.ProfileDirectory, "subtitle-styles.aegistyles")));
            File.WriteAllText(Path.Combine(options.ProfileDirectory, "preferences.json"), "probe write");
            Directory.Delete(options.ProfileDirectory, true);

            Assert.Equal("original preferences", File.ReadAllText(Path.Combine(source, "preferences.json")));
            Assert.Equal("original styles", File.ReadAllText(Path.Combine(source, "subtitle-styles.aegistyles")));
            Assert.Equal("original layouts", File.ReadAllText(Path.Combine(source, "layouts.json")));
        }
        finally
        {
            if (Directory.Exists(options.ProfileDirectory))
            {
                Directory.Delete(options.ProfileDirectory, true);
            }
            Directory.Delete(source, true);
        }
    }

    [Fact]
    public void MissingSourceIsRejectedBeforeCreatingAProbeDirectory()
    {
        var options = WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-source-profile",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]);

        Assert.Throws<DirectoryNotFoundException>(() => WorkspaceProbeProfile.Prepare(options));
        Assert.False(Directory.Exists(options.ProfileDirectory));
    }

    [Fact]
    public void ManagedLibraryFilesAreCopiedIntoAnIndependentProbe()
    {
        using var source = new TemporaryWorkbenchDirectory();
        var library = Path.Combine(source.Path, "effect-library");
        var styles = Path.Combine(library, "styles");
        Directory.CreateDirectory(styles);
        File.WriteAllText(Path.Combine(library, "manifest.json"), "original manifest");
        File.WriteAllText(Path.Combine(styles, "preset.aegistyles"), "original preset");
        var options = WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-source-profile", source.Path]);
        try
        {
            WorkspaceProbeProfile.Prepare(options);
            var copied = Path.Combine(options.ProfileDirectory, "effect-library", "styles", "preset.aegistyles");

            Assert.Equal("original preset", File.ReadAllText(copied));
            File.WriteAllText(copied, "probe change");
            Assert.Equal("original preset", File.ReadAllText(Path.Combine(styles, "preset.aegistyles")));
        }
        finally
        {
            Directory.Delete(options.ProfileDirectory, true);
        }
    }
}
