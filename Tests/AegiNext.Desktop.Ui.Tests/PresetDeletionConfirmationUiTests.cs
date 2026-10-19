using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary;
using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using AegiNext.Export.Presets;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PresetDeletionConfirmationUiTests
{
    [AvaloniaTheory]
    [InlineData(PresetDeletionTestPage.STYLES, false)]
    [InlineData(PresetDeletionTestPage.STYLES, true)]
    [InlineData(PresetDeletionTestPage.EFFECTS, false)]
    [InlineData(PresetDeletionTestPage.EFFECTS, true)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, false)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, true)]
    public async Task BatchDeletionWaitsForConfirmationAndCancellingPreservesLibraryAndSelection(PresetDeletionTestPage page, bool accepted)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, ids) =>
        {
            Assert.True(await SelectAsync(window, page, ids[0], [ids[0], ids[1]]));
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            var selection = SelectedIds(window, page);

            UiTestActions.Click(window, DeleteButton(page));
            var completion = coordinator.DeletionCompletion;
            var request = await dialogs.DeletionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(request.IsDraftOnly);
            Assert.Equal(2, request.Names.Length);
            Assert.Equal("First", request.Names[0]);
            Assert.Equal("Second", request.Names[1]);
            Assert.False(completion.IsCompleted);
            Assert.Same(snapshot, Snapshot(context, page));
            Assert.Equal(ids, LibraryIds(context, page));
            Assert.True(IsBusy(window, page));
            Assert.False(UiTestActions.Find<Button>(window, DeleteButton(page)).IsEffectivelyEnabled);
            dialogs.PendingDeletion.TrySetResult(accepted);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, dialogs.DeletionRequests);
            Assert.False(IsBusy(window, page));
            if (accepted)
            {
                Assert.Equal(new[] { ids[2] }, LibraryIds(context, page));
                Assert.Equal(new[] { ids[2] }, await PersistedIdsAsync(directory, page));
                Assert.DoesNotContain(DraftId(window, page), ids.Take(2).Select(id => (Guid?)id));
                await RenameRemainingAsync(context, page);
                Assert.Equal(new[] { ids[2] }, LibraryIds(context, page));
                Assert.DoesNotContain(DraftId(window, page), ids.Take(2).Select(id => (Guid?)id));
                Assert.False(IsDirty(window, page));
            }
            else
            {
                Assert.Same(snapshot, Snapshot(context, page));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(LibraryPath(directory, page)));
                Assert.Equal(selection, SelectedIds(window, page));
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(PresetDeletionTestPage.STYLES, false)]
    [InlineData(PresetDeletionTestPage.STYLES, true)]
    [InlineData(PresetDeletionTestPage.EFFECTS, false)]
    [InlineData(PresetDeletionTestPage.EFFECTS, true)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, false)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, true)]
    public async Task DirtySavedPresetDeletionPreservesInvalidDraftOnCancelAndCannotResurrectDeletedIdentity(PresetDeletionTestPage page, bool accepted)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, ids) =>
        {
            Assert.True(await SelectAsync(window, page, ids[0], [ids[0]]));
            SetInvalidDraft(window, page);
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            var draftId = DraftId(window, page);
            Assert.True(IsDirty(window, page));

            UiTestActions.Click(window, DeleteButton(page));
            var completion = coordinator.DeletionCompletion;
            var request = await dialogs.DeletionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("First", Assert.Single(request.Names));
            Assert.Same(snapshot, Snapshot(context, page));
            Assert.Equal(draftId, DraftId(window, page));
            Assert.Equal("7e-", InvalidText(window, page));
            dialogs.PendingDeletion.TrySetResult(accepted);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            if (accepted)
            {
                Assert.Equal(ids.Skip(1), LibraryIds(context, page));
                Assert.NotEqual(draftId, DraftId(window, page));
                Assert.False(IsDirty(window, page));
                await RenameRemainingAsync(context, page);
                Assert.NotEqual(draftId, DraftId(window, page));
                Assert.DoesNotContain(ids[0], LibraryIds(context, page));
                Assert.DoesNotContain(ids[0], await PersistedIdsAsync(directory, page));
                Assert.False(IsDirty(window, page));
            }
            else
            {
                Assert.Same(snapshot, Snapshot(context, page));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(LibraryPath(directory, page)));
                Assert.Equal(draftId, DraftId(window, page));
                Assert.Equal("Unsaved name", DraftName(window, page));
                Assert.Equal("7e-", InvalidText(window, page));
                Assert.True(IsDirty(window, page));
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(PresetDeletionTestPage.STYLES, false)]
    [InlineData(PresetDeletionTestPage.STYLES, true)]
    [InlineData(PresetDeletionTestPage.EFFECTS, false)]
    [InlineData(PresetDeletionTestPage.EFFECTS, true)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, false)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS, true)]
    public async Task UnsavedDraftDiscardRequiresConfirmationAndNeverChangesPersonalLibrary(PresetDeletionTestPage page, bool accepted)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, _) =>
        {
            UiTestActions.Click(window, AddButton(page));
            await SelectionCompletion(window, page).WaitAsync(TimeSpan.FromSeconds(5));
            SetInvalidDraft(window, page);
            var draftId = DraftId(window, page);
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            Assert.NotNull(draftId);
            Assert.Empty(SelectedIds(window, page));

            UiTestActions.Click(window, DeleteButton(page));
            var completion = coordinator.DeletionCompletion;
            var request = await dialogs.DeletionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(request.IsDraftOnly);
            Assert.Equal("Unsaved name", Assert.Single(request.Names));
            Assert.Equal(draftId, DraftId(window, page));
            Assert.Equal("7e-", InvalidText(window, page));
            dialogs.PendingDeletion.TrySetResult(accepted);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(snapshot, Snapshot(context, page));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(LibraryPath(directory, page)));
            if (accepted)
            {
                Assert.Null(DraftId(window, page));
                Assert.False(IsDirty(window, page));
            }
            else
            {
                Assert.Equal(draftId, DraftId(window, page));
                Assert.Equal("Unsaved name", DraftName(window, page));
                Assert.Equal("7e-", InvalidText(window, page));
                Assert.True(IsDirty(window, page));
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(PresetDeletionTestPage.STYLES)]
    [InlineData(PresetDeletionTestPage.EFFECTS)]
    [InlineData(PresetDeletionTestPage.EXPORT_PRESETS)]
    public async Task ClosingPendingConfirmationRejectsLateAcceptanceAndDoesNotChangeReopenedWindow(PresetDeletionTestPage page)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, ids) =>
        {
            Assert.True(await SelectAsync(window, page, ids[0], [ids[0], ids[1]]));
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            UiTestActions.Click(window, DeleteButton(page));
            var completion = coordinator.DeletionCompletion;
            await dialogs.DeletionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var owner = Assert.IsType<Window>(window.Owner);

            window.Close();
            await CloseCompletion(window).WaitAsync(TimeSpan.FromSeconds(5));
            await completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(coordinator.Window);
            Assert.False(window.IsVisible);
            Assert.True(dialogs.DeletionToken.IsCancellationRequested);
            Assert.False(dialogs.PendingDeletion.Task.IsCompleted);
            await coordinator.OpenAsync(owner, page: page);
            var reopened = Assert.IsAssignableFrom<Window>(coordinator.Window);
            Assert.NotSame(window, reopened);
            Assert.True(await SelectAsync(reopened, page, ids[0], [ids[0]]));
            SetInvalidDraft(reopened, page);
            dialogs.PendingDeletion.TrySetResult(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(snapshot, Snapshot(context, page));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(LibraryPath(directory, page)));
            Assert.Equal(ids, LibraryIds(context, page));
            Assert.Equal("Unsaved name", DraftName(reopened, page));
            Assert.Equal("7e-", InvalidText(reopened, page));
            Assert.False(IsBusy(reopened, page));
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DialogDefaultsToCancelAndRefreshesLocalizedTextWhileKeepingNames(bool isDraftOnly)
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var request = new PresetDeletionRequest(["字幕 ABC", "Second 123"], isDraftOnly);
        var dialog = new PresetDeletionDialog(request);
        Task<bool>? completion = null;
        try
        {
            owner.Show();
            completion = dialog.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            var cancel = UiTestActions.Find<Button>(dialog, "CancelButton");
            var delete = UiTestActions.Find<Button>(dialog, "DeleteButton");
            var message = UiTestActions.Find<TextBlock>(dialog, "PresetDeletionMessage");
            var names = UiTestActions.Find<TextBlock>(dialog, "PresetDeletionNames");
            Assert.True(cancel.IsCancel);
            Assert.True(cancel.IsDefault);
            Assert.False(delete.IsDefault);
            Assert.True(cancel.IsFocused);
            var englishMessage = message.Text;
            Assert.Equal(Localization.Get("Settings.Delete"), dialog.Title);
            Assert.Equal(Localization.Get("Workbench.Cancel"), cancel.Content);
            CaptureDialog(dialog, isDraftOnly, "en-US");

            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localization.Get("Settings.Delete"), dialog.Title);
            Assert.Equal(Localization.Get("Workbench.Cancel"), cancel.Content);
            Assert.Equal(Localization.Get("Settings.Delete"), delete.Content);
            Assert.Equal(isDraftOnly ? Localization.Get("Settings.DiscardPresetDraftConfirmation")
                : Localization.Format("Settings.DeletePresetsConfirmation", request.Names.Length), message.Text);
            Assert.NotEqual(englishMessage, message.Text);
            Assert.Equal(string.Join(Environment.NewLine, request.Names), names.Text);
            CaptureDialog(dialog, isDraftOnly, "zh-CN");
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(await completion.WaitAsync(TimeSpan.FromSeconds(5)));
            owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog.Close(false);
            if (completion is not null)
            {
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            owner.Close();
        }
    }

    private static void CaptureDialog(Window dialog, bool isDraftOnly, string languageId)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("Headless capture directory must be an absolute isolated artifacts path.");
        }

        Directory.CreateDirectory(directory);
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = dialog.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The headless Skia renderer did not produce a frame.");
        var kind = isDraftOnly ? "draft" : "batch";
        frame.Save(Path.Combine(directory, $"headless-preset-deletion-{kind}-{languageId}.png"), PngBitmapEncoderOptions.Default);
    }

    private static async Task WithSettingsAsync(PresetDeletionTestPage page,
        Func<DesktopApplicationContext, PresetDeletionTestHost, Window, PresetDeletionDialogStub, string, Guid[], Task> test)
    {
        using var environment = new UiTestEnvironment();
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath));
        await context.Initialization;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var names = new[] { "First", "Second", "Remaining" };
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            var name = names[index];
            if (page == PresetDeletionTestPage.STYLES)
            {
                await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new SubtitleStylePreset(id, name, new())));
            }
            else if (page == PresetDeletionTestPage.EFFECTS)
            {
                var source = BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"custom-{id:N}\"", StringComparison.Ordinal);
                await context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(new(id, name, source)));
            }
            else
            {
                await context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(new(id, name, new())));
            }
        }
        var dialogs = new PresetDeletionDialogStub();
        using var coordinator = new PresetDeletionTestHost(context, dialogs, page);
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: page);
            await test(context, coordinator, coordinator.Window!, dialogs, environment.DirectoryPath, ids);
        }
        finally
        {
            var completion = coordinator.DeletionCompletion;
            coordinator.Dispose();
            dialogs.PendingDeletion.TrySetResult(false);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    private static string DeleteButton(PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => "DeleteStyleButton",
        PresetDeletionTestPage.EFFECTS => "DeleteEffectScriptButton",
        _ => "DeleteExportPresetButton"
    };

    private static string AddButton(PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => "AddStyleButton",
        PresetDeletionTestPage.EFFECTS => "AddEffectScriptButton",
        _ => "AddExportPresetButton"
    };

    private static string LibraryPath(string directory, PresetDeletionTestPage page) => Path.Combine(directory, page switch
    {
        PresetDeletionTestPage.STYLES or PresetDeletionTestPage.EFFECTS => Path.Combine("effect-library", "manifest.json"),
        _ => "export-presets.aegiexports"
    });

    private static object Snapshot(DesktopApplicationContext context, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => context.StyleLibrary.Snapshot,
        PresetDeletionTestPage.EFFECTS => context.EffectScriptLibrary.Snapshot,
        _ => context.ExportPresetLibrary.Snapshot
    };

    private static Guid[] LibraryIds(DesktopApplicationContext context, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => context.StyleLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray(),
        PresetDeletionTestPage.EFFECTS => context.EffectScriptLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray(),
        _ => context.ExportPresetLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray()
    };

    private static async Task<Guid[]> PersistedIdsAsync(string directory, PresetDeletionTestPage page)
    {
        if (page is PresetDeletionTestPage.STYLES or PresetDeletionTestPage.EFFECTS)
        {
            using var repository = new EffectLibraryRepository(Path.Combine(directory, "effect-library"));
            await repository.LoadAsync();
            return page == PresetDeletionTestPage.STYLES
                ? repository.Snapshot.Styles.Presets.Select(preset => preset.Id).ToArray()
                : repository.Snapshot.Effects.Presets.Select(preset => preset.Id).ToArray();
        }
        var path = LibraryPath(directory, page);
        return (await VideoExportPresetStore.LoadAsync(path)).Presets.Select(preset => preset.Id).ToArray();
    }

    private static Task<bool> SelectAsync(Window window, PresetDeletionTestPage page, Guid primaryId, Guid[] ids) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.SelectStylesAsync(primaryId, ids),
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.SelectEffectsAsync(primaryId, ids),
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.SelectPresetsAsync(primaryId, ids)
    };

    private static Task CloseCompletion(Window window) => window switch
    {
        EffectLibraryWindow library => library.CloseCompletion,
        SettingsWindow settings => settings.CloseCompletion,
        _ => throw new ArgumentOutOfRangeException(nameof(window))
    };

    private static Task SelectionCompletion(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.SelectionCompletion,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.SelectionCompletion,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.SelectionCompletion
    };

    private static ImmutableArray<Guid> SelectedIds(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.SelectedIds,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.SelectedIds,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.SelectedIds
    };

    private static Guid? DraftId(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.Draft?.Id,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.Draft?.Id,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.Draft?.Id
    };

    private static string DraftName(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.Name,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.Name,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.Name
    };

    private static string InvalidText(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.FontSizeText,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.Source,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.QualityText
    };

    private static bool IsDirty(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.IsDirty,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.IsDirty,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.IsDirty
    };

    private static bool IsBusy(Window window, PresetDeletionTestPage page) => page switch
    {
        PresetDeletionTestPage.STYLES => ((EffectLibraryWindow)window).ViewModel.Styles.IsBusy,
        PresetDeletionTestPage.EFFECTS => ((EffectLibraryWindow)window).ViewModel.Effects.IsBusy,
        _ => ((SettingsWindow)window).ViewModel.ExportPresets.IsBusy
    };

    private static void SetInvalidDraft(Window window, PresetDeletionTestPage page)
    {
        var nameInput = page switch
        {
            PresetDeletionTestPage.STYLES => "StyleNameInput",
            PresetDeletionTestPage.EFFECTS => "EffectScriptNameInput",
            _ => "ExportPresetNameInput"
        };
        UiTestActions.SetText(UiTestActions.Find<TextBox>(window, nameInput), "Unsaved name");
        if (page == PresetDeletionTestPage.STYLES)
        {
            UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput").RawText = "7e-";
        }
        else if (page == PresetDeletionTestPage.EFFECTS)
        {
            ((EffectLibraryWindow)window).ViewModel.Effects.Source = "7e-";
            ((EffectLibraryWindow)window).ViewModel.Effects.ValidateCommand.Execute(null);
        }
        else
        {
            UiTestActions.Find<NumericDraftInput>(window, "QualityInput").RawText = "7e-";
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Task RenameRemainingAsync(DesktopApplicationContext context, PresetDeletionTestPage page)
    {
        if (page == PresetDeletionTestPage.STYLES)
        {
            var preset = context.StyleLibrary.Snapshot.Presets[0];
            return context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset with { Name = "Changed remaining" }));
        }
        if (page == PresetDeletionTestPage.EFFECTS)
        {
            var preset = context.EffectScriptLibrary.Snapshot.Presets[0];
            return context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(preset with { Name = "Changed remaining" }));
        }
        var exportPreset = context.ExportPresetLibrary.Snapshot.Presets[0];
        return context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(exportPreset with { Name = "Changed remaining" }));
    }
}
