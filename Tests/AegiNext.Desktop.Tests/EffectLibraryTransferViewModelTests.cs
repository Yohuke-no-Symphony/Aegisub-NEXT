using AegiNext.Application.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Tests.EffectLibrary;

namespace AegiNext.Desktop.Tests;

[Collection("Workspace session")]
public sealed class EffectLibraryTransferViewModelTests
{
    private static readonly string[] assDiagnosticKeys =
    [
        "EffectLibrary.AssStyleScale", "EffectLibrary.AssStyleRotation", "EffectLibrary.AssStyleGeometry",
        "EffectLibrary.AssStyleSecondaryColor", "EffectLibrary.AssLetterSpacingRange", "EffectLibrary.AssBorderResampling",
        "EffectLibrary.AssYCbCrMatrix", "EffectLibrary.AssWrapStyle", "EffectLibrary.AssBorderLayoutResolution",
        "EffectLibrary.AssPlayRes", "EffectLibrary.AssWrapModeApproximation"
    ];

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

    [Fact]
    public void AssDiagnosticsUseLocalizedCodesAndKeepUnknownMessagesWhenRefreshingChoices()
    {
        var source = new EffectLibraryImportRow
        {
            Kind = EffectLibraryItemKind.STYLE,
            Name = "Imported style",
            SourceName = "styles.ass",
            Status = EffectLibraryImportStatus.NEW,
            CanImport = true,
            Diagnostics =
            [
                new("Ass.StyleScale", "Source scale message"),
                new("Ass.StyleRotation", "Source rotation message"),
                new("Ass.StyleGeometry", "Source geometry message"),
                new("Ass.StyleSecondaryColor", "Source secondary color message"),
                new("Ass.LetterSpacingRange", "Source spacing message"),
                new("Ass.BorderResampling", "Source border message"),
                new("Ass.YCbCrMatrix", "Source matrix message"),
                new("Ass.WrapStyle", "Source wrapping message"),
                new("Ass.BorderLayoutResolution", "Source layout message"),
                new("Ass.PlayRes", "Source resolution message"),
                new("Ass.WrapModeApproximation", "Source balanced wrapping message"),
                new("Ass.FutureConversion", "未来格式提示 keeps its original message")
            ]
        };
        var model = new EffectLibraryImportRowViewModel(source);
        model.Decision = EffectLibraryImportDecision.SKIP;
        var choice = model.SelectedDecisionChoice;
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        model.RefreshLanguage();

        Assert.True(model.HasDiagnostics);
        Assert.Equal(assDiagnosticKeys.Select(Localization.Get).Append(source.Diagnostics[^1].Message),
            model.DiagnosticsText.Split('\n'));
        Assert.Same(source, model.Row);
        Assert.Same(choice, model.SelectedDecisionChoice);
        Assert.Equal(EffectLibraryImportDecision.SKIP, model.Decision);
        Assert.Contains(nameof(model.DiagnosticsText), notifications);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("ja-JP")]
    public void AssDiagnosticTranslationsExistInEachLanguagePack(string language)
    {
        var pack = LanguagePackReader.Read(Path.Combine(AppContext.BaseDirectory, "i18n", language + ".json"));

        Assert.All(assDiagnosticKeys, key =>
        {
            Assert.True(pack.Strings.TryGetValue(key, out var value), $"Missing {language} translation: {key}");
            Assert.False(string.IsNullOrWhiteSpace(value));
        });
        Assert.Contains("ASS", pack.Strings["EffectLibrary.ImportDescription"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportRowsWithoutDiagnosticsHaveNoConversionHint(bool defaultDiagnostics)
    {
        var source = new EffectLibraryImportRow
        {
            Status = EffectLibraryImportStatus.IDENTICAL,
            Diagnostics = defaultDiagnostics ? default : []
        };
        var model = new EffectLibraryImportRowViewModel(source);

        Assert.False(model.HasDiagnostics);
        Assert.Empty(model.DiagnosticsText);
    }
}
