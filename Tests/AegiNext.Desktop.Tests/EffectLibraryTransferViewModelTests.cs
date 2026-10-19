using AegiNext.Application.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.Tests.EffectLibrary;

namespace AegiNext.Desktop.Tests;

[Collection("Workspace session")]
public sealed class EffectLibraryTransferViewModelTests
{
    [Fact]
    public void ExportEntryDoesNotRequireSelectionAndRespectsBusyStateAndDisposal()
    {
        using var model = new EffectLibraryTransferViewModel();
        var exports = 0;
        model.ExportRequested += (_, _) => exports++;
        Assert.True(model.ExportCommand.CanExecute(null));
        model.ExportCommand.Execute(null);
        Assert.Equal(1, exports);
        model.ExternalBusy = true;
        Assert.False(model.ImportCommand.CanExecute(null));
        Assert.False(model.ExportCommand.CanExecute(null));
        model.ExternalBusy = false;
        model.SetOperationBusy(true);
        Assert.False(model.ExportCommand.CanExecute(null));
        model.SetOperationBusy(false);
        Assert.True(model.ExportCommand.CanExecute(null));
        model.Dispose();
        Assert.False(model.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectingOneInputVersionAutomaticallySkipsTheOtherAndRefreshKeepsDecisions()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var first = new AegiNext.Core.Presets.SubtitleStylePreset(Guid.NewGuid(), "Same", new() { FontFamily = "Noto Sans" });
        var second = first with { Style = first.Style with { FontSize = 100 } };
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "second.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = new() { Presets = [first] }, FontFamilies = [EffectLibraryTestFonts.Family]
        }, firstPath);
        await SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = new() { Presets = [second] }, FontFamilies = [EffectLibraryTestFonts.Family]
        }, secondPath);
        var plan = await EffectLibraryTestFonts.CreateTransfer(repository).AnalyzeImportAsync([firstPath, secondPath]);
        using var model = new EffectLibraryTransferViewModel();
        model.SetPreview(plan);
        Assert.All(model.Rows, row => Assert.Equal(EffectLibraryImportDecision.SKIP, row.Decision));
        Assert.False(model.CommitImportCommand.CanExecute(null));
        model.Rows[0].Decision = EffectLibraryImportDecision.IMPORT;
        Assert.True(model.CommitImportCommand.CanExecute(null));

        model.Rows[1].Decision = EffectLibraryImportDecision.IMPORT;
        model.RefreshLanguage();

        Assert.Equal(EffectLibraryImportDecision.SKIP, model.Rows[0].Decision);
        Assert.Equal(EffectLibraryImportDecision.IMPORT, model.Rows[1].Decision);
        Assert.Equal(EffectLibraryImportDecision.IMPORT, model.CreateDecisions()[1]);
        Assert.Same(plan, model.ImportPlan);
    }

    [Fact]
    public void ProtectedRowsOfferOnlySkipAndReportDslIdentityChanges()
    {
        var row = new EffectLibraryImportRow
        {
            Index = 0,
            Kind = EffectLibraryItemKind.EFFECT,
            Name = "Business name",
            Status = EffectLibraryImportStatus.CONFLICT,
            ConflictReason = EffectLibraryImportConflictReason.MULTIPLE_TARGETS,
            DslId = "new-script",
            PreviousDslId = "old-script",
            ConflictingNames = ["First", "Second"]
        };
        var model = new EffectLibraryImportRowViewModel(row);
        Assert.Single(model.DecisionChoices);
        Assert.False(model.IsDecisionAvailable);
        Assert.True(model.HasDslIdChange);
        Assert.Contains("old-script", model.DslIdChangeText, StringComparison.Ordinal);
        Assert.Contains("new-script", model.DslIdChangeText, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => model.Decision = EffectLibraryImportDecision.REPLACE);
    }
}
