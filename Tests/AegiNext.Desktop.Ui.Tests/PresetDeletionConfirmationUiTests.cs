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
    [InlineData(SettingsPage.STYLES, false)]
    [InlineData(SettingsPage.STYLES, true)]
    [InlineData(SettingsPage.EFFECTS, false)]
    [InlineData(SettingsPage.EFFECTS, true)]
    [InlineData(SettingsPage.EXPORT_PRESETS, false)]
    [InlineData(SettingsPage.EXPORT_PRESETS, true)]
    public async Task BatchDeletionWaitsForConfirmationAndCancellingPreservesLibraryAndSelection(SettingsPage page, bool accepted)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, ids) =>
        {
            Assert.True(await SelectAsync(window, page, ids[0], [ids[0], ids[1]]));
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            var selection = SelectedIds(window, page);

            UiTestActions.Click(window, DeleteButton(page));
            var completion = Completion(coordinator, page);
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
    [InlineData(SettingsPage.STYLES, false)]
    [InlineData(SettingsPage.STYLES, true)]
    [InlineData(SettingsPage.EFFECTS, false)]
    [InlineData(SettingsPage.EFFECTS, true)]
    [InlineData(SettingsPage.EXPORT_PRESETS, false)]
    [InlineData(SettingsPage.EXPORT_PRESETS, true)]
    public async Task DirtySavedPresetDeletionPreservesInvalidDraftOnCancelAndCannotResurrectDeletedIdentity(SettingsPage page, bool accepted)
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
            var completion = Completion(coordinator, page);
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
    [InlineData(SettingsPage.STYLES, false)]
    [InlineData(SettingsPage.STYLES, true)]
    [InlineData(SettingsPage.EFFECTS, false)]
    [InlineData(SettingsPage.EFFECTS, true)]
    [InlineData(SettingsPage.EXPORT_PRESETS, false)]
    [InlineData(SettingsPage.EXPORT_PRESETS, true)]
    public async Task UnsavedDraftDiscardRequiresConfirmationAndNeverChangesPersonalLibrary(SettingsPage page, bool accepted)
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
            var completion = Completion(coordinator, page);
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
    [InlineData(SettingsPage.STYLES)]
    [InlineData(SettingsPage.EFFECTS)]
    [InlineData(SettingsPage.EXPORT_PRESETS)]
    public async Task ClosingPendingConfirmationRejectsLateAcceptanceAndDoesNotChangeReopenedWindow(SettingsPage page)
    {
        await WithSettingsAsync(page, async (context, coordinator, window, dialogs, directory, ids) =>
        {
            Assert.True(await SelectAsync(window, page, ids[0], [ids[0], ids[1]]));
            var snapshot = Snapshot(context, page);
            var bytes = await File.ReadAllBytesAsync(LibraryPath(directory, page));
            UiTestActions.Click(window, DeleteButton(page));
            var completion = Completion(coordinator, page);
            await dialogs.DeletionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var owner = Assert.IsType<Window>(window.Owner);

            window.Close();
            await window.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(coordinator.Window);
            Assert.False(window.IsVisible);
            Assert.True(dialogs.DeletionToken.IsCancellationRequested);
            Assert.False(dialogs.PendingDeletion.Task.IsCompleted);
            await coordinator.OpenAsync(owner, page: page);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
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

    private static async Task WithSettingsAsync(SettingsPage page,
        Func<DesktopApplicationContext, SettingsWindowCoordinator, SettingsWindow, PresetDeletionDialogStub, string, Guid[], Task> test)
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
            if (page == SettingsPage.STYLES)
            {
                await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new(id, name, new())));
            }
            else if (page == SettingsPage.EFFECTS)
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
        using var coordinator = new SettingsWindowCoordinator(context, _ => dialogs);
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: page);
            await test(context, coordinator, coordinator.Window!, dialogs, environment.DirectoryPath, ids);
        }
        finally
        {
            var completion = Task.WhenAll(coordinator.DeletionCompletion, coordinator.ExportCompletion);
            coordinator.Dispose();
            dialogs.PendingDeletion.TrySetResult(false);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    private static string DeleteButton(SettingsPage page) => page switch
    {
        SettingsPage.STYLES => "DeleteStyleButton",
        SettingsPage.EFFECTS => "DeleteEffectScriptButton",
        _ => "DeleteExportPresetButton"
    };

    private static string AddButton(SettingsPage page) => page switch
    {
        SettingsPage.STYLES => "AddStyleButton",
        SettingsPage.EFFECTS => "AddEffectScriptButton",
        _ => "AddExportPresetButton"
    };

    private static string LibraryPath(string directory, SettingsPage page) => Path.Combine(directory, page switch
    {
        SettingsPage.STYLES => "subtitle-styles.aegistyles",
        SettingsPage.EFFECTS => "effect-scripts.json",
        _ => "export-presets.aegiexports"
    });

    private static object Snapshot(DesktopApplicationContext context, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => context.StyleLibrary.Snapshot,
        SettingsPage.EFFECTS => context.EffectScriptLibrary.Snapshot,
        _ => context.ExportPresetLibrary.Snapshot
    };

    private static Guid[] LibraryIds(DesktopApplicationContext context, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => context.StyleLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray(),
        SettingsPage.EFFECTS => context.EffectScriptLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray(),
        _ => context.ExportPresetLibrary.Snapshot.Presets.Select(preset => preset.Id).ToArray()
    };

    private static async Task<Guid[]> PersistedIdsAsync(string directory, SettingsPage page)
    {
        var path = LibraryPath(directory, page);
        return page switch
        {
            SettingsPage.STYLES => (await SubtitleStylePresetStore.LoadAsync(path)).Presets.Select(preset => preset.Id).ToArray(),
            SettingsPage.EFFECTS => (await EffectScriptPresetStore.LoadAsync(path)).Presets.Select(preset => preset.Id).ToArray(),
            _ => (await VideoExportPresetStore.LoadAsync(path)).Presets.Select(preset => preset.Id).ToArray()
        };
    }

    private static Task<bool> SelectAsync(SettingsWindow window, SettingsPage page, Guid primaryId, Guid[] ids) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.SelectStylesAsync(primaryId, ids),
        SettingsPage.EFFECTS => window.ViewModel.Effects.SelectEffectsAsync(primaryId, ids),
        _ => window.ViewModel.ExportPresets.SelectPresetsAsync(primaryId, ids)
    };

    private static Task Completion(SettingsWindowCoordinator coordinator, SettingsPage page)
        => page == SettingsPage.EXPORT_PRESETS ? coordinator.ExportCompletion : coordinator.DeletionCompletion;

    private static Task SelectionCompletion(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.SelectionCompletion,
        SettingsPage.EFFECTS => window.ViewModel.Effects.SelectionCompletion,
        _ => window.ViewModel.ExportPresets.SelectionCompletion
    };

    private static ImmutableArray<Guid> SelectedIds(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.SelectedIds,
        SettingsPage.EFFECTS => window.ViewModel.Effects.SelectedIds,
        _ => window.ViewModel.ExportPresets.SelectedIds
    };

    private static Guid? DraftId(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.Draft?.Id,
        SettingsPage.EFFECTS => window.ViewModel.Effects.Draft?.Id,
        _ => window.ViewModel.ExportPresets.Draft?.Id
    };

    private static string DraftName(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.Name,
        SettingsPage.EFFECTS => window.ViewModel.Effects.Name,
        _ => window.ViewModel.ExportPresets.Name
    };

    private static string InvalidText(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.FontSizeText,
        SettingsPage.EFFECTS => window.ViewModel.Effects.Source,
        _ => window.ViewModel.ExportPresets.QualityText
    };

    private static bool IsDirty(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.IsDirty,
        SettingsPage.EFFECTS => window.ViewModel.Effects.IsDirty,
        _ => window.ViewModel.ExportPresets.IsDirty
    };

    private static bool IsBusy(SettingsWindow window, SettingsPage page) => page switch
    {
        SettingsPage.STYLES => window.ViewModel.Styles.IsBusy,
        SettingsPage.EFFECTS => window.ViewModel.Effects.IsBusy,
        _ => window.ViewModel.ExportPresets.IsBusy
    };

    private static void SetInvalidDraft(SettingsWindow window, SettingsPage page)
    {
        var nameInput = page switch
        {
            SettingsPage.STYLES => "StyleNameInput",
            SettingsPage.EFFECTS => "EffectScriptNameInput",
            _ => "ExportPresetNameInput"
        };
        UiTestActions.SetText(UiTestActions.Find<TextBox>(window, nameInput), "Unsaved name");
        if (page == SettingsPage.STYLES)
        {
            UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput").RawText = "7e-";
        }
        else if (page == SettingsPage.EFFECTS)
        {
            window.ViewModel.Effects.Source = "7e-";
            window.ViewModel.Effects.ValidateCommand.Execute(null);
        }
        else
        {
            UiTestActions.Find<NumericDraftInput>(window, "QualityInput").RawText = "7e-";
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Task RenameRemainingAsync(DesktopApplicationContext context, SettingsPage page)
    {
        if (page == SettingsPage.STYLES)
        {
            var preset = context.StyleLibrary.Snapshot.Presets[0];
            return context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset with { Name = "Changed remaining" }));
        }
        if (page == SettingsPage.EFFECTS)
        {
            var preset = context.EffectScriptLibrary.Snapshot.Presets[0];
            return context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(preset with { Name = "Changed remaining" }));
        }
        var exportPreset = context.ExportPresetLibrary.Snapshot.Presets[0];
        return context.RunExportPresetOperationAsync(() => context.ExportPresetLibrary.UpsertAsync(exportPreset with { Name = "Changed remaining" }));
    }
}
