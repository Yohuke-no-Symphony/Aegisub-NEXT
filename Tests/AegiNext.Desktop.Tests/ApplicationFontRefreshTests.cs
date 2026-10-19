using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Tests.Workspace;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

[Collection("Workspace session")]
public sealed class ApplicationFontRefreshTests
{
    [Fact]
    public void PublishingApplicationFontsRebuildsCandidatesAndReplacingTheFamilyRemovesOldCandidates()
    {
        var families = ReadFamilies();
        var service = new SubtitleFontSelectionService(SystemFontCatalog.Empty);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.SetApplicationFonts(families);

        Assert.Contains(service.Candidates, candidate => candidate.Selection.FamilyName == "Noto Sans");
        Assert.Contains(service.Faces, face => face.FamilyName == "Noto Sans" && face.Variant.Weight == 900);
        Assert.Equal(families, service.Catalog!.ApplicationFonts);
        service.SetApplicationFonts(families);
        Assert.Equal(1, changes);

        service.SetApplicationFonts([]);

        Assert.Empty(service.Faces);
        Assert.Empty(service.Candidates);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task PublishingApplicationFontsPreservesTheExistingCommandError()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        await session.Fonts.EnsureLoadedAsync();
        var error = new InvalidOperationException("A previous command failed.");
        session.ShowError(error, false);

        session.Fonts.SetApplicationFonts(ReadFamilies());

        Assert.Same(error, session.LastError);
        Assert.Equal(error.Message, session.ViewModel.Error);
    }

    [Fact]
    public async Task OverwritingSystemFontParametersDoesNotWaitForTheInitialFontCatalog()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var loaded = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fonts = new SubtitleFontSelectionService(() => loaded.Task);
        await using var application = new DesktopApplicationContext(new(directory.Path), new() { AutoCheckUpdates = false }, fonts);
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await context.InitializeAsync();
        var session = context.Session;
        var target = new SubtitleStylePreset(Guid.NewGuid(), "Target style", new());
        await application.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(target));
        var cueId = context.Editor.AddSubtitle(new(0), new(2), "Example");
        var style = new SubtitleStyle
        {
            FontFamily = "Source Han Sans SC", FontSize = 42,
            FontVariant = new() { Name = "Heavy", Weight = 900, Width = 5 }, Bold = true
        };
        context.Editor.UpdateSubtitle(cueId, line => line with { Style = style });
        session.SelectCue(cueId);
        try
        {
            await session.Styles.CaptureAsync(target.Id).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(loaded.Task.IsCompleted);
            Assert.Equal(target with { Style = style }, Assert.Single(session.StyleLibrary.Snapshot.Presets));
            Assert.Empty(application.EffectLibrary.Snapshot.FontFamilies);
            Assert.Empty(context.Editor.Snapshot.Assets);
        }
        finally
        {
            loaded.TrySetResult(SystemFontCatalog.Empty);
            await fonts.EnsureLoadedAsync();
        }
    }

    private static ImmutableArray<FontFamilyBundle> ReadFamilies()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        return [new("Noto Sans", [font])];
    }
}
