using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Rendering.Fonts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectLibraryExportIntegrationUiTests
{
    [AvaloniaTheory]
    [InlineData(EffectLibraryExportFormat.PACKAGE, 1)]
    [InlineData(EffectLibraryExportFormat.INDEPENDENT, 1)]
    [InlineData(EffectLibraryExportFormat.INDEPENDENT, 2)]
    public async Task MetadataOnlyExportDoesNotWaitForFontLoadingAndKeepsUnavailableFontNames(EffectLibraryExportFormat format, int styleCount)
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        var fontLoads = 0;
        var loaded = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fonts = new SubtitleFontSelectionService(() =>
        {
            fontLoads++;
            return loaded.Task;
        });
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false }, fonts);
        await context.Initialization;
        var styles = Enumerable.Range(0, styleCount).Select(index => new SubtitleStylePreset(Guid.NewGuid(), $"Style {index}",
            new()
            {
                FontFamily = "Unavailable Font Family", FontSize = 42,
                FontVariant = new() { Name = "Heavy", Weight = 900, PostScriptName = "UnavailableFont-Heavy" }
            }, new TimingPostProcessorOptions())).ToImmutableArray();
        foreach (var style in styles)
        {
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        }
        var before = context.EffectLibrary.Snapshot;
        var output = Path.Combine(environment.DirectoryPath, "output");
        var singlePath = Path.Combine(output, format == EffectLibraryExportFormat.PACKAGE ? "styles.aegilibrary" : "style.aegistyles");
        var dialogs = new StartupTestDialogService { SavePath = singlePath, FolderPath = output };
        dialogs.ExportDialogHandler = (snapshot, exportAsync, token) => exportAsync(new(
            snapshot.Styles.Presets, [], format, IncludeFonts: false), dialogs, token);
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs, () => Task.FromResult(true));

        try
        {
            await coordinator.ExportDialogAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(model.Error);
            Assert.Null(context.LastError);
            Assert.Equal(1, dialogs.ExportDialogRequests);
            Assert.Equal(1, fontLoads);
            Assert.False(loaded.Task.IsCompleted);
            Assert.Null(fonts.LoadedCatalog);
            Assert.Same(before, context.EffectLibrary.Snapshot);
            if (format == EffectLibraryExportFormat.PACKAGE)
            {
                var package = await EffectLibraryPackageStore.LoadAsync(singlePath);
                Assert.False(package.IncludesFonts);
                Assert.Empty(package.FontFamilies);
                Assert.Equal(styles.ToArray(), package.Styles.Presets.ToArray());
            }
            else
            {
                var packages = await Task.WhenAll(Directory.GetFiles(output, "*.aegistyles").Select(path => SubtitleStylePresetStore.LoadAsync(path)));
                Assert.Equal(styleCount, packages.Length);
                Assert.All(packages, package =>
                {
                    Assert.False(package.IncludesFonts);
                    Assert.Empty(package.FontFamilies);
                });
                Assert.Equal(styles.OrderBy(preset => preset.Id), packages.SelectMany(package => package.Styles.Presets).OrderBy(preset => preset.Id));
            }
            Assert.Empty(Directory.GetDirectories(output));
        }
        finally
        {
            loaded.TrySetResult(SystemFontCatalog.Empty);
            await fonts.EnsureLoadedAsync();
        }
    }

    [AvaloniaFact]
    public async Task DeferredFontLoadingKeepsTheApplicationFontsFrozenBeforeOpeningTheDialog()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        var loaded = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fonts = new SubtitleFontSelectionService(() => loaded.Task);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false }, fonts);
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Frozen style", new() { FontFamily = "Noto Sans" });
        var family = StyleExchangeUiTestFonts.Family;
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new CapturedSubtitleStyle(style, [family])));
        var before = context.EffectLibrary.Snapshot;
        var path = Path.Combine(environment.DirectoryPath, "frozen.aegistyles");
        var dialogs = new StartupTestDialogService { SavePath = path };
        dialogs.ExportDialogHandler = async (snapshot, exportAsync, token) =>
        {
            Assert.Same(before, snapshot);
            Assert.Null(fonts.LoadedCatalog);
            await context.RunEffectLibraryOperationAsync(() => context.EffectLibrary.UpdateAsync(context.EffectLibrary.Snapshot.Revision,
                current => current with
                {
                    Styles = new() { Presets = [style with { Name = "Changed after opening" }] }, FontFamilies = []
                }));
            loaded.TrySetResult(SystemFontCatalog.Empty);
            return await exportAsync(new(snapshot.Styles.Presets, [], EffectLibraryExportFormat.INDEPENDENT), dialogs, token);
        };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs, () => Task.FromResult(true));
        try
        {
            await coordinator.ExportDialogAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(model.Error);
            var exported = await SubtitleStylePresetStore.LoadAsync(path);
            Assert.True(exported.IncludesFonts);
            Assert.Equal(style, Assert.Single(exported.Styles.Presets));
            Assert.Equal(family.Files[0].Data, Assert.Single(Assert.Single(exported.FontFamilies).Files).Data);
            Assert.Empty(context.EffectLibrary.Snapshot.FontFamilies);
        }
        finally
        {
            loaded.TrySetResult(SystemFontCatalog.Empty);
            await fonts.EnsureLoadedAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(EffectLibraryPage.STYLES)]
    [InlineData(EffectLibraryPage.EFFECTS)]
    [InlineData(EffectLibraryPage.TRANSFER)]
    public async Task EveryExportEntryOpensTheSameSavedLibraryWithoutSelectionOrDraftProcessing(EffectLibraryPage page)
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new SubtitleStylePreset(Guid.NewGuid(), "Saved style", new())));
        await context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(Effect("saved-effect")));
        var before = context.EffectLibrary.Snapshot;
        var dialogs = new StartupTestDialogService();
        using var coordinator = new EffectLibraryWindowCoordinator(context, _ => dialogs);
        var owner = new Window();
        owner.Show();
        try
        {
            await coordinator.OpenAsync(owner, page: page);
            var window = coordinator.Window!;
            window.ViewModel.Styles.AddCommand.Execute(null);
            window.ViewModel.Styles.FontSizeText = "7e-";
            await window.ViewModel.Effects.AddCommand.ExecuteAsync(null);
            window.ViewModel.Effects.Source = "effect broken";
            var styleDraft = window.ViewModel.Styles.Draft;
            var effectDraft = window.ViewModel.Effects.Draft;
            Assert.Empty(window.ViewModel.Styles.SelectedIds);
            Assert.Empty(window.ViewModel.Effects.SelectedIds);

            UiTestActions.Click(window, page switch
            {
                EffectLibraryPage.STYLES => "ExportStylesButton",
                EffectLibraryPage.EFFECTS => "ExportEffectScriptButton",
                _ => "ExportEffectLibraryButton"
            });
            await coordinator.TransferCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, dialogs.ExportDialogRequests);
            Assert.Same(before, dialogs.ExportDialogSnapshot);
            Assert.Same(before, context.EffectLibrary.Snapshot);
            Assert.Equal(page, window.CurrentPage);
            Assert.Equal(styleDraft, window.ViewModel.Styles.Draft);
            Assert.Equal(effectDraft, window.ViewModel.Effects.Draft);
            Assert.Equal("7e-", window.ViewModel.Styles.FontSizeText);
            Assert.Equal("effect broken", window.ViewModel.Effects.Source);
            Assert.True(window.ViewModel.Styles.IsDirty);
            Assert.True(window.ViewModel.Effects.IsDirty);
            Assert.Equal(0, dialogs.PresetConfirmationRequests);
            Assert.Equal(0, dialogs.SaveCount);
            Assert.DoesNotContain(dialogs.ExportDialogSnapshot!.Effects.Presets,
                preset => BuiltinEffectScripts.Templates.Any(template => template.Source == preset.Source));
        }
        finally
        {
            coordinator.Dispose();
            await coordinator.TransferCompletion;
            await coordinator.DisposalCompletion;
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(EffectLibraryExportFormat.PACKAGE, 1, 2)]
    [InlineData(EffectLibraryExportFormat.INDEPENDENT, 1, 2)]
    [InlineData(EffectLibraryExportFormat.INDEPENDENT, 1, 0)]
    [InlineData(EffectLibraryExportFormat.INDEPENDENT, 0, 1)]
    [InlineData(EffectLibraryExportFormat.SOURCE, 0, 1)]
    [InlineData(EffectLibraryExportFormat.SOURCE, 0, 2)]
    public async Task DialogRequestsPublishTheSelectedFormatAndRetainCompleteMetadata(EffectLibraryExportFormat format,
        int styleCount, int effectCount)
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var fontBytes = (await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), TestContext.Current.CancellationToken)).ToImmutableArray();
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(fontBytes.AsSpan())), fontBytes);
        var family = new FontFamilyBundle("Noto Sans", [font]);
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Portable style", new() { FontFamily = "Noto Sans" },
            new TimingPostProcessorOptions());
        ImmutableArray<SubtitleStylePreset> styles = styleCount == 0 ? [] : [style];
        var effects = Enumerable.Range(0, effectCount).Select(index => Effect($"selected-{index}")).ToImmutableArray();
        await context.RunEffectLibraryOperationAsync(() => context.EffectLibrary.UpdateAsync(context.EffectLibrary.Snapshot.Revision,
            current => current with { Styles = new() { Presets = styles }, Effects = new() { Presets = effects }, FontFamilies = styleCount == 0 ? [] : [family] }));
        var before = context.EffectLibrary.Snapshot;
        var output = Path.Combine(environment.DirectoryPath, "output");
        Directory.CreateDirectory(output);
        var singleExtension = format == EffectLibraryExportFormat.PACKAGE ? ".aegilibrary" :
            format == EffectLibraryExportFormat.SOURCE ? ".aegifx" : styleCount == 1 ? ".aegistyles" : ".aegieffect";
        var singlePath = Path.Combine(output, "selected" + singleExtension);
        var dialogs = new StartupTestDialogService { SavePath = singlePath, FolderPath = output };
        dialogs.ExportDialogHandler = (snapshot, exportAsync, token) => exportAsync(new(
            snapshot.Styles.Presets, snapshot.Effects.Presets, format), dialogs, token);
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => throw new InvalidOperationException("导出不应处理草稿。"));

        await coordinator.ExportDialogAsync();

        Assert.Null(model.Error);
        Assert.Null(context.LastError);
        Assert.Same(before, context.EffectLibrary.Snapshot);
        Assert.Equal(1, dialogs.ExportDialogRequests);
        Assert.Equal(format != EffectLibraryExportFormat.PACKAGE && styleCount + effectCount > 1 ? 1 : 0, dialogs.FolderRequests);
        Assert.Equal(dialogs.FolderRequests == 0 ? 1 : 0, dialogs.SaveCount);
        if (format == EffectLibraryExportFormat.PACKAGE)
        {
            var package = await EffectLibraryPackageStore.LoadAsync(singlePath);
            AssertStyleEqual(style, Assert.Single(package.Styles.Presets));
            Assert.Equal(font.Data, Assert.Single(Assert.Single(package.FontFamilies).Files).Data);
            Assert.Equal(effects, package.Effects.Presets);
        }
        else if (format == EffectLibraryExportFormat.SOURCE)
        {
            var paths = Directory.GetFiles(output, "*.aegifx");
            Assert.Equal(effectCount, paths.Length);
            var sources = await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path)));
            Assert.Equal(effects.Select(preset => preset.Source).Order(), sources.Order());
        }
        else
        {
            var stylePaths = Directory.GetFiles(output, "*.aegistyles");
            var effectPaths = Directory.GetFiles(output, "*.aegieffect");
            Assert.Equal(styleCount, stylePaths.Length);
            Assert.Equal(effectCount, effectPaths.Length);
            if (styleCount > 0)
            {
                var package = await SubtitleStylePresetStore.LoadAsync(stylePaths.Single());
                AssertStyleEqual(style, Assert.Single(package.Styles.Presets));
                Assert.Equal(font.Data, Assert.Single(Assert.Single(package.FontFamilies).Files).Data);
            }
            var exportedEffects = await Task.WhenAll(effectPaths.Select(path => EffectScriptPresetStore.ReadPresetAsync(path)));
            Assert.Equal(effects.OrderBy(preset => preset.Id), exportedEffects.OrderBy(preset => preset.Id));
        }
        Assert.Empty(Directory.EnumerateDirectories(output));
    }

    [AvaloniaFact]
    public async Task PickerCancellationAndManagedPathFailureCanRetryInTheSameDialogWithoutGlobalErrors()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new() { FontFamily = "Noto Sans" });
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new CapturedSubtitleStyle(style,
            [StyleExchangeUiTestFonts.Family])));
        var before = context.EffectLibrary.Snapshot;
        var bytes = await File.ReadAllBytesAsync(context.EffectLibrary.ManifestPath);
        var output = Path.Combine(environment.DirectoryPath, "retry.aegistyles");
        var dialogs = new StartupTestDialogService();
        dialogs.ExportDialogHandler = async (snapshot, exportAsync, token) =>
        {
            var request = new EffectLibraryExportRequest(snapshot.Styles.Presets, [], EffectLibraryExportFormat.INDEPENDENT);
            Assert.False(await exportAsync(request, dialogs, token));
            dialogs.SavePath = context.EffectLibrary.ManifestPath;
            await Assert.ThrowsAsync<InvalidDataException>(() => exportAsync(request, dialogs, token));
            Assert.Null(context.LastError);
            Assert.Same(before, context.EffectLibrary.Snapshot);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(context.EffectLibrary.ManifestPath, token));
            dialogs.SavePath = output;
            return await exportAsync(request, dialogs, token);
        };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs, () => Task.FromResult(true));

        await coordinator.ExportDialogAsync();

        Assert.Equal(1, dialogs.ExportDialogRequests);
        Assert.Equal(3, dialogs.SaveCount);
        Assert.Equal(style, Assert.Single((await SubtitleStylePresetStore.LoadAsync(output)).Styles.Presets));
        Assert.Null(context.LastError);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task RawExportKeepsBuiltinAndSavedPersonalSourcesAndReportsFailuresOnlyInTheLibraryWindow()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var personal = Effect("saved-raw");
        await context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(personal));
        var dialogs = new StartupTestDialogService { FolderPath = context.EffectLibrary.DirectoryPath };
        using var coordinator = new EffectLibraryWindowCoordinator(context, _ => dialogs);
        var owner = new Window();
        owner.Show();
        try
        {
            await coordinator.OpenAsync(owner, page: EffectLibraryPage.EFFECTS);
            var window = coordinator.Window!;
            var model = window.ViewModel.Effects;
            model.SelectEffects(personal.Id, [personal.Id]);
            model.Source = "effect broken";
            var builtin = model.Effects.First(item => item.IsBuiltin);
            model.SelectEffects(builtin.Id, [builtin.Id, personal.Id]);

            UiTestActions.Click(window, "ExportEffectScriptSourceButton");
            await coordinator.TransferCompletion;
            Assert.True(window.ViewModel.HasError);
            Assert.Null(context.LastError);
            Assert.Equal(0, dialogs.ExportDialogRequests);
            var output = Path.Combine(environment.DirectoryPath, "raw-output");
            Directory.CreateDirectory(output);
            dialogs.FolderPath = output;
            UiTestActions.Click(window, "ExportEffectScriptSourceButton");
            await coordinator.TransferCompletion;

            var sources = await Task.WhenAll(Directory.GetFiles(output, "*.aegifx").Select(path => File.ReadAllTextAsync(path)));
            Assert.Equal(new[] { builtin.Preset.Source, personal.Source }.Order(), sources.Order());
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(EffectLibraryPage.EFFECTS, window.CurrentPage);
            Assert.Equal(0, dialogs.PresetConfirmationRequests);
            Assert.Equal(personal, Assert.Single(context.EffectScriptLibrary.Snapshot.Presets));
            model.SelectEffects(personal.Id, [personal.Id]);
            Assert.Equal("effect broken", model.Source);
            Assert.True(model.IsDirty);
        }
        finally
        {
            coordinator.Dispose();
            await coordinator.TransferCompletion;
            await coordinator.DisposalCompletion;
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task NativeExportDialogOwnsItsScopedDialogsAndClosingWaitsForExportCleanup()
    {
        using var environment = new UiTestEnvironment();
        var windows = new List<Window>();
        var owner = new Window();
        owner.Show();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new WindowWorkbenchDialogService(owner, registerWindow: windows.Add);
        var snapshot = new EffectLibrarySnapshot { Styles = new() { Presets = [new(Guid.NewGuid(), "Saved", new())] } };
        var operation = service.ShowEffectLibraryExportAsync(snapshot, async (_, scopedDialogs, token) =>
        {
            Assert.NotSame(service, scopedDialogs);
            await scopedDialogs.ConfirmSettingsRestartAsync(token);
            enteredCleanup.TrySetResult();
            return await cleanup.Task;
        }, cancellation.Token);
        try
        {
            var dialog = Assert.IsType<EffectLibraryExportDialog>(Assert.Single(windows));
            Assert.Same(owner, dialog.Owner);
            var model = Assert.IsType<EffectLibraryExportDialogViewModel>(dialog.DataContext);
            Assert.Equal(0, model.SelectedCount);
            model.SelectAllCommand.Execute(null);
            var export = model.ExportSelectedCommand.ExecuteAsync(null);
            Assert.Equal(2, windows.Count);
            Assert.Same(dialog, windows[1].Owner);
            windows[1].Close(false);
            await enteredCleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));

            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            Assert.False(operation.IsCompleted);
            cleanup.TrySetResult(true);

            Assert.False(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            await export;
            Assert.True(model.Completion.IsCompletedSuccessfully);
            Assert.True(model.DisposalCompletion.IsCompletedSuccessfully);
            Assert.All(windows, window => Assert.False(window.IsVisible));
        }
        finally
        {
            cleanup.TrySetResult(false);
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var window in windows.Where(window => window.IsVisible))
            {
                window.Close();
            }
            owner.Close();
        }
    }

    private static EffectScriptPreset Effect(string id)
    {
        return new(Guid.NewGuid(), "Display " + id,
            BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{id}\"", StringComparison.Ordinal));
    }

    private static void AssertStyleEqual(SubtitleStylePreset expected, SubtitleStylePreset actual)
    {
        Assert.Equal(expected, actual);
    }
}
