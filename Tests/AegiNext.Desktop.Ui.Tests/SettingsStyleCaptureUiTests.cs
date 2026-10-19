using AegiNext.Application;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsStyleCaptureUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", "覆盖到当前样式")]
    [InlineData("en-US", "Overwrite current style")]
    [InlineData("ja-JP", "現在のスタイルに上書き")]
    public async Task OverwriteLabelAndAvailabilityFollowTheSelectedSavedStyle(string language, string label)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        await UiTestActions.CreateSubtitleAsync(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        Localization.SetLanguage(language);
        var overwrite = UiTestActions.Find<Button>(library, "CaptureStyleButton");
        Assert.Equal(label, Assert.IsType<IconText>(overwrite.Content).Text);
        Assert.False(overwrite.IsEffectivelyEnabled);

        UiTestActions.Click(library, "AddStyleButton");
        await library.ViewModel.Styles.SelectionCompletion;
        Assert.True(library.ViewModel.Styles.HasDraft);
        Assert.False(overwrite.IsEffectivelyEnabled);
        UiTestActions.Find<TextBox>(library, "StyleNameInput").Text = "New saved target";
        UiTestActions.Click(library, "SaveStyleButton");
        await library.ViewModel.Styles.SelectionCompletion;
        Dispatcher.UIThread.RunJobs();
        Assert.True(overwrite.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task OverwriteUsesTheLibrarySelectionInsteadOfTheWorkbenchPreset()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var first = await SaveTargetAsync(context.Session);
        var second = new SubtitleStylePreset(Guid.NewGuid(), "Second target", new() { FontSize = 15 });
        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.UpsertAsync(second));
        var cueId = await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.UpdateSubtitle(cueId, line => line with { Style = line.Style with { FontSize = 64 } });
        var source = context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == cueId).Style;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        await library.ViewModel.Styles.SelectStylesAsync(second.Id, [second.Id]);

        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();

        Assert.False(library.ViewModel.HasError, library.ViewModel.Error);
        Assert.Equal(2, context.Session.StyleLibrary.Snapshot.Presets.Length);
        Assert.Equal(first, context.Session.StyleLibrary.Snapshot.Presets.Single(preset => preset.Id == first.Id));
        Assert.Equal(second with { Style = source }, context.Session.StyleLibrary.Snapshot.Presets.Single(preset => preset.Id == second.Id));
        Assert.Equal(second.Id, library.ViewModel.Styles.SelectedStyle?.Id);
        library.ViewModel.Styles.SelectStyles(first.Id, [first.Id, second.Id]);
        Assert.False(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task CurrentSubtitleOverwritesTheSelectedStyleWithoutChangingItsIdentityOrAssociations()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var previous = new SubtitleStylePreset(Guid.NewGuid(), "Existing saved style", new() { FontSize = 18 })
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 240 }
        };
        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.UpsertAsync(previous));
        var cueId = await UiTestActions.CreateSubtitleAsync(context, text: "Default style");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with { Style = line.Style with { FontSize = 42 } });
        var document = context.Session.DocumentSnapshot;
        var originalStyle = document.Subtitles.Single(line => line.Id == cueId).Style;
        Assert.Equal("sans-serif", originalStyle.FontFamily);
        Assert.Empty(context.Session.ApplicationContext.EffectLibrary.Snapshot.FontFamilies);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        Assert.Equal(previous.Id, library.ViewModel.Styles.SelectedStyle?.Id);
        Assert.True(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);

        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();

        Assert.False(library.ViewModel.HasError, library.ViewModel.Error);
        Assert.Null(context.Session.LastError);
        var overwritten = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(previous with { Style = originalStyle }, overwritten);
        Assert.Equal(previous.Id, library.ViewModel.Styles.SelectedStyle?.Id);
        Assert.False(library.ViewModel.Styles.IsDirty);
        Assert.Empty(context.Session.ApplicationContext.EffectLibrary.Snapshot.FontFamilies);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task CapturingAnUnavailableSourceHanFamilyPreservesItsParametersWithoutFontFiles()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var target = await SaveTargetAsync(context.Session);
        var cueId = await UiTestActions.CreateSubtitleAsync(context, text: "Source Han style");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with
            {
                FontFamily = "Source Han Sans SC", FontSize = 42, Bold = true,
                FontVariant = new() { Name = "Heavy", PostScriptName = "SourceHanSansSC-Heavy", Weight = 900 }
            }
        });
        var document = context.Session.DocumentSnapshot;
        var original = document.Subtitles.Single(line => line.Id == cueId).Style;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);

        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();

        Assert.False(library.ViewModel.HasError, library.ViewModel.Error);
        var captured = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(target.Id, captured.Id);
        Assert.Equal(original, captured.Style);
        Assert.Equal(captured.Id, library.ViewModel.Styles.SelectedStyle?.Id);
        Assert.Empty(context.Session.ApplicationContext.EffectLibrary.Snapshot.FontFamilies);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task CaptureFailureIsVisibleInsideTheLibraryWindowAndCanBeRetried()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var target = await SaveTargetAsync(context.Session);
        var cueId = await UiTestActions.CreateSubtitleAsync(context, text: "Failed style capture");
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "fonts/capture-test.ttf");
        context.Session.Editor.Apply("Link capture font", document => document with { Assets = document.Assets.Add(font) });
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = "Noto Sans", FontAssetId = font.Id }
        });
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        Assert.True(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);

        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(target, Assert.Single(context.Session.StyleLibrary.Snapshot.Presets));
        Assert.Empty(context.Session.ApplicationContext.EffectLibrary.Snapshot.FontFamilies);
        Assert.NotNull(context.Session.LastError);
        Assert.True(library.ViewModel.HasError);
        var error = UiTestActions.Find<TextBlock>(library, "EffectLibraryError");
        Assert.True(error.IsVisible);
        Assert.Contains("capture-test.ttf", error.Text, StringComparison.Ordinal);
        Assert.True(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);

        var path = Path.Combine(context.Session.ProjectDirectory, font.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), path);
        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();

        var overwritten = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(target.Id, overwritten.Id);
        Assert.Null(overwritten.Style.FontAssetId);
        Assert.Single(context.Session.ApplicationContext.EffectLibrary.Snapshot.FontFamilies);
        Assert.False(library.ViewModel.HasError);
        Assert.False(error.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("CancelButton", false, false)]
    [InlineData("DiscardButton", true, false)]
    [InlineData("SaveButton", true, true)]
    public async Task OverwriteRespectsTheExistingLibraryDraftAndKeepsTheSelectedStyle(string decisionButton,
        bool captureAccepted, bool draftSaved)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var previous = new SubtitleStylePreset(Guid.NewGuid(), "Saved style before capture", new());
        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.UpsertAsync(previous));
        var cueId = await UiTestActions.CreateSubtitleAsync(context, text: "Current style");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with { Style = line.Style with { FontSize = 73 } });
        var sourceStyle = context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == cueId).Style;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        UiTestActions.Find<TextBox>(library, "StyleNameInput").Text = "Unsaved library draft";
        Assert.True(library.ViewModel.Styles.IsDirty);
        try
        {
            UiTestActions.Click(library, "CaptureStyleButton");
            var confirmation = Assert.Single(library.OwnedWindows);
            UiTestActions.Click(confirmation, decisionButton);
            await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Dispatcher.UIThread.RunJobs();
            await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Dispatcher.UIThread.RunJobs();

            var saved = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
            Assert.Equal(previous.Id, saved.Id);
            Assert.Equal(draftSaved ? "Unsaved library draft" : previous.Name, saved.Name);
            Assert.Equal(captureAccepted ? sourceStyle : previous.Style, saved.Style);
            Assert.Equal(!captureAccepted, library.ViewModel.Styles.IsDirty);
            if (captureAccepted)
            {
                Assert.Equal(previous.Id, library.ViewModel.Styles.SelectedStyle?.Id);
            }
            else
            {
                Assert.Equal(previous.Id, library.ViewModel.Styles.SelectedStyle?.Id);
                Assert.Equal("Unsaved library draft", library.ViewModel.Styles.Name);
            }
        }
        finally
        {
            library.ViewModel.Styles.DiscardDraft();
        }
    }

    [AvaloniaFact]
    public async Task SuccessfulCaptureDoesNotDisplayAnEarlierUnrelatedSessionError()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        await SaveTargetAsync(context.Session);
        await UiTestActions.CreateSubtitleAsync(context, text: "Another default style");
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        var earlier = new InvalidOperationException("An unrelated earlier operation failed.");
        context.Session.ShowError(earlier, false);

        UiTestActions.Click(library, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.False(library.ViewModel.HasError);
        Assert.False(UiTestActions.Find<TextBlock>(library, "EffectLibraryError").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyLocalLibraryDoesNotBlockCapturingTheCurrentSubtitleStyle(bool manifestDirectory)
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        var originals = await SettingsStartupUiTests.WriteLegacyLibraryAsync(environment.DirectoryPath, manifestDirectory);
        await using var application = StyleExchangeUiTestFonts.CreateContext(environment.DirectoryPath);
        await application.Initialization;
        Assert.Null(application.EffectLibraryLoadError);
        await using var session = new WorkbenchSession(new StartupTestDialogService(), applicationContext: application);
        await session.Styles.Completion;
        Assert.Equal(ProjectOpenStatus.OPENED,
            (await session.CreateProjectAsync(new ProjectCreationRequest("Legacy style capture", environment.DirectoryPath))).Status);
        var cueId = Assert.Single(session.DocumentSnapshot.Subtitles).Id;
        session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Text = "Current subtitle",
            Style = line.Style with { FontFamily = "Noto Sans", FontSize = 42 }
        });
        session.SelectCue(cueId);
        await session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        var document = session.DocumentSnapshot;
        var window = new MainWindow(session);
        try
        {
            window.Show();
            await session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
            var library = Assert.Single(window.OwnedWindows.OfType<EffectLibraryWindow>());
            library.SelectPage(EffectLibraryPage.STYLES);
            Assert.False(library.ViewModel.HasError);
            Assert.False(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);
            UiTestActions.Click(library, "AddStyleButton");
            await library.ViewModel.Styles.SelectionCompletion;
            UiTestActions.Find<TextBox>(library, "StyleNameInput").Text = "Target in new library";
            UiTestActions.Click(library, "SaveStyleButton");
            await library.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            var target = Assert.Single(session.StyleLibrary.Snapshot.Presets);
            Assert.True(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);

            UiTestActions.Click(library, "CaptureStyleButton");
            await session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            var captured = Assert.Single(session.StyleLibrary.Snapshot.Presets);
            Assert.Equal(target.Id, captured.Id);
            Assert.Equal(document.Subtitles.Single(line => line.Id == cueId).Style, captured.Style);
            Assert.Empty(application.EffectLibrary.Snapshot.FontFamilies);
            Assert.Same(document, session.DocumentSnapshot);
            Assert.False(library.ViewModel.HasError);
            Assert.Null(application.EffectLibraryLoadError);
            Assert.Null(session.LastError);
            await SettingsStartupUiTests.AssertLegacyLibraryUnchangedAsync(environment.DirectoryPath, manifestDirectory, originals);
        }
        finally
        {
            window.Close();
            await window.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CapturedSubtitleStyleRemainsEditableAndCanBeSaved()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        await SaveTargetAsync(context.Session);
        var library = context.Session.ApplicationContext.EffectLibrary;
        await context.Session.ApplicationContext.RunEffectLibraryOperationAsync(() => library.UpdateAsync(library.Snapshot.Revision,
            current => current with { FontFamilies = [StyleExchangeUiTestFonts.Family] }));
        var cueId = await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.UpdateSubtitle(cueId, line => line with { Style = line.Style with { FontFamily = "Noto Sans" } });
        var document = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.STYLES);

        UiTestActions.Click(settings, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        settings.UpdateLayout();

        var captured = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(document.Subtitles.Single(cue => cue.Id == cueId).Style, captured.Style);
        Assert.Equal(StyleExchangeUiTestFonts.Family.Files[0].Data, Assert.Single(Assert.Single(library.Snapshot.FontFamilies).Files).Data);
        Assert.False(context.Session.Styles.IsBusy);
        Assert.False(settings.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<StackPanel>(settings, "StyleEditor").IsEffectivelyEnabled);
        Assert.True(UiTestActions.Find<Button>(settings, "SaveStyleButton").IsEffectivelyEnabled);
        var nameInput = UiTestActions.Find<TextBox>(settings, "StyleNameInput");
        Assert.True(nameInput.IsEffectivelyEnabled);
        nameInput.Focus();
        nameInput.SelectAll();
        settings.KeyTextInput("Edited captured style");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Edited captured style", settings.ViewModel.Styles.Name);

        UiTestActions.Click(settings, "SaveStyleButton");
        await settings.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        var saved = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(captured.Id, saved.Id);
        Assert.Equal("Edited captured style", saved.Name);
        Assert.False(settings.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<Button>(settings, "DuplicateStyleButton").IsEffectivelyEnabled);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    private static async Task<SubtitleStylePreset> SaveTargetAsync(WorkbenchSession session)
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Target style", new() { FontSize = 20 });
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(preset));
        return preset;
    }
}
