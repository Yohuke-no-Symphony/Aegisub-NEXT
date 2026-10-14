using AegiNext.Application.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;

namespace AegiNext.Desktop.Tests;

[Collection("Workspace session")]
public sealed class EffectLibraryTransferViewModelTests
{
    [Fact]
    public void SelectionRestrictsBuiltinAndMixedFormatsWithoutChangingBusinessContent()
    {
        using var model = new EffectLibraryTransferViewModel();
        model.UpdateSelection(0, 1, 0);
        Assert.Equal(3, model.ExportFormats.Count);
        model.SelectedFormat = EffectLibraryExportFormat.PACKAGE;
        model.UpdateSelection(0, 0, 1);
        Assert.Equal(EffectLibraryExportFormat.SOURCE, model.SelectedFormat);
        Assert.Single(model.ExportFormats);
        Assert.True(model.ExportSelectionCommand.CanExecute(null));
        model.UpdateSelection(1, 1, 0);
        Assert.DoesNotContain(model.ExportFormats, choice => choice.Format == EffectLibraryExportFormat.SOURCE);
        Assert.Equal(EffectLibraryExportFormat.INDEPENDENT, model.SelectedFormat);
        model.ExternalBusy = true;
        Assert.False(model.ImportCommand.CanExecute(null));
        Assert.False(model.ExportSelectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectingOneInputVersionAutomaticallySkipsTheOtherAndRefreshKeepsDecisions()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var first = new AegiNext.Core.Presets.SubtitleStylePreset(Guid.NewGuid(), "Same", new());
        var second = first with { Style = first.Style with { FontSize = 100 } };
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "second.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [first] }, firstPath);
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [second] }, secondPath);
        var plan = await new EffectLibraryTransferService(repository).AnalyzeImportAsync([firstPath, secondPath]);
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
