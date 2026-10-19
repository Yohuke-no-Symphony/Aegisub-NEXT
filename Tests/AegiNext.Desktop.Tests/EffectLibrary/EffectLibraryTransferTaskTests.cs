using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.EffectLibrary;

[Collection("Workspace session")]
public sealed class EffectLibraryTransferTaskTests
{
    [Fact]
    public async Task AnalysisIsReadOnlyAndConfirmedImportPublishesBothLibrariesThroughTheTaskService()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var input = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(directory.Path);
        await owner.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared style", new() { FontFamily = "Noto Sans" });
        var effect = Effect("shared-effect");
        var path = Path.Combine(input.Path, "shared.aegilibrary");
        await EffectLibraryPackageStore.SaveAsync(new()
        {
            Styles = new() { Presets = [style] },
            Effects = new() { Presets = [effect] },
            FontFamilies = [EffectLibraryTestFonts.Family]
        }, path);
        var analysisTask = new EffectLibraryImportAnalysisTask(owner, [path]);
        Assert.Contains(AegiTaskResource.DeferredStoragePath(path), analysisTask.Resources);
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.EFFECT_LIBRARY), analysisTask.Resources);

        var analysis = owner.Tasks.Submit(analysisTask);
        var plan = await analysis.Completion;

        Assert.Equal(2, plan.Rows.Length);
        Assert.Empty(owner.StyleLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectLibrary.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(owner.LibraryPaths.FontsDirectory));
        var commit = owner.Tasks.Submit(new EffectLibraryImportCommitTask(owner, plan,
            new Dictionary<int, EffectLibraryImportDecision>()));
        await commit.Completion;

        Assert.Equal(style, Assert.Single(owner.StyleLibrary.Snapshot.Presets));
        Assert.Equal(effect, Assert.Single(owner.EffectScriptLibrary.Snapshot.Presets));
        var installed = Assert.Single(owner.EffectLibrary.Snapshot.FontFamilies);
        Assert.Equal(EffectLibraryTestFonts.Family.Files[0].Sha256, Assert.Single(installed.Files).Sha256);
        Assert.True(Directory.EnumerateFiles(owner.LibraryPaths.FontsDirectory, "*", SearchOption.AllDirectories).Any());
        Assert.Equal(AegiTaskState.Succeeded, analysis.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, commit.Snapshot.State);
        Assert.Null(commit.Snapshot.ScopeId);
    }

    [Fact]
    public async Task PackageExportUsesFrozenInputsWhenTheLiveLibraryChanges()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var output = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(directory.Path);
        await owner.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { FontSize = 41, FontFamily = "Noto Sans" });
        var effect = Effect("frozen-effect");
        await owner.RunStyleOperationAsync(() => owner.StyleLibrary.UpsertAsync(style));
        await owner.RunEffectOperationAsync(() => owner.EffectScriptLibrary.UpsertAsync(effect));
        owner.Fonts.SetApplicationFonts([EffectLibraryTestFonts.Family]);
        var captured = owner.EffectLibrary.Snapshot;
        var path = Path.Combine(output.Path, "shared.aegilibrary");
        var task = new EffectLibraryExportTask(owner, captured.Styles.Presets, captured.Effects.Presets,
            path, EffectLibraryExportMode.PACKAGE);
        await owner.RunStyleOperationAsync(() => owner.StyleLibrary.UpsertAsync(style with { Name = "Changed" }));
        Assert.Empty(owner.Fonts.Catalog!.ApplicationFonts);
        Assert.Empty(owner.Fonts.Catalog.Faces);

        await owner.Tasks.Submit(task).Completion;

        var exported = await EffectLibraryPackageStore.LoadAsync(path);
        Assert.Equal(style, Assert.Single(exported.Styles.Presets));
        Assert.Equal(effect, Assert.Single(exported.Effects.Presets));
        Assert.Equal(EffectLibraryTestFonts.Family.Files[0].Sha256, Assert.Single(Assert.Single(exported.FontFamilies).Files).Sha256);
        Assert.Equal("Changed", Assert.Single(owner.StyleLibrary.Snapshot.Presets).Name);
    }

    [Fact]
    public async Task StaleImportDecisionFailsWithoutOverwritingInterveningChanges()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var input = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(directory.Path);
        await owner.Initialization;
        var path = Path.Combine(input.Path, "incoming.aegieffect");
        await EffectScriptPresetStore.WritePresetAsync(Effect("incoming-effect"), path);
        var plan = await owner.Tasks.Submit(new EffectLibraryImportAnalysisTask(owner, [path])).Completion;
        var latest = new SubtitleStylePreset(Guid.NewGuid(), "Intervening", new() { FontFamily = "Noto Sans" });
        await owner.RunStyleOperationAsync(() => owner.StyleLibrary.UpsertAsync(latest));
        var snapshot = owner.EffectLibrary.Snapshot;

        var commit = owner.Tasks.Submit(new EffectLibraryImportCommitTask(owner, plan,
            new Dictionary<int, EffectLibraryImportDecision>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commit.Completion);

        Assert.Same(snapshot, owner.EffectLibrary.Snapshot);
        Assert.Equal(latest, Assert.Single(owner.StyleLibrary.Snapshot.Presets));
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
    }

    [Fact]
    public async Task ExportTaskDeclaresAllIndependentDestinationsAndKeepsFullEffectMetadata()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var output = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(directory.Path);
        await owner.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared", new() { FontFamily = "Noto Sans" });
        var effect = Effect("named-effect") with { Name = "Custom display name" };
        var task = new EffectLibraryExportTask(owner, [style], [effect], output.Path,
            EffectLibraryExportMode.INDEPENDENT_FILES);
        var paths = owner.EffectLibraryTransfer.GetIndependentDestinations([style], [effect], output.Path);
        Assert.All(paths, path => Assert.Contains(AegiTaskResource.DeferredStoragePath(path), task.Resources));

        await owner.Tasks.Submit(task).Completion;

        var exported = await EffectScriptPresetStore.ReadPresetAsync(Assert.Single(paths, path => path.EndsWith(".aegieffect", StringComparison.Ordinal)));
        Assert.Equal(effect, exported);
        var exportedStyle = await SubtitleStylePresetStore.LoadAsync(Assert.Single(paths, path => path.EndsWith(".aegistyles", StringComparison.Ordinal)));
        Assert.Equal(EffectLibraryTestFonts.Family.Files[0].Sha256,
            Assert.Single(Assert.Single(exportedStyle.FontFamilies).Files).Sha256);
        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task RawBuiltinExportWorksWithoutAnInitializedPersonalLibrary()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var output = new TemporaryWorkbenchDirectory();
        var libraryDirectory = Path.Combine(directory.Path, "effect-library");
        Directory.CreateDirectory(libraryDirectory);
        await File.WriteAllTextAsync(Path.Combine(libraryDirectory, "manifest.json"), "{broken");
        await using var owner = EffectLibraryTestFonts.CreateOwner(directory.Path);
        await owner.Initialization;
        var builtin = new EffectScriptPreset(Guid.NewGuid(), "Fade in", BuiltinEffectScripts.Get("fade-in").Source);
        var path = Path.Combine(output.Path, "fade-in.aegifx");

        await owner.Tasks.Submit(new EffectLibraryExportTask(owner, [], [builtin], path,
            EffectLibraryExportMode.SCRIPT_SOURCE)).Completion;

        Assert.Equal(builtin.Source, (await EffectScriptPresetStore.ReadScriptAsync(path)).Source);
        Assert.NotNull(owner.EffectLibraryLoadError);
    }

    private static EffectScriptPreset Effect(string id)
    {
        return new(Guid.NewGuid(), id,
            BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{id}\"", StringComparison.Ordinal));
    }
}
