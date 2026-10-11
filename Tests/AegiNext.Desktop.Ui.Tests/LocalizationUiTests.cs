using System.Globalization;
using System.Runtime.CompilerServices;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>Exercises dynamic localization in actual compiled markup and open windows.</summary>
public sealed class LocalizationUiTests
{
    /// <summary>Loc refreshes every supported text property while business bindings keep their owner and draft.</summary>
    [AvaloniaFact]
    public void CompiledLocBindingsRefreshPropertiesTemplatesMenusAndFlyoutsWithoutChangingBusinessState()
    {
        using var environment = new UiTestEnvironment();
        var model = new LocalizationFixtureViewModel();
        var view = new LocalizationFixtureView { DataContext = model };
        var window = new Window { Width = 600, Height = 900, Content = view };
        try
        {
            window.Show();
            AssertFixtureText(view, "en-US");
            var business = UiTestActions.Find<TextBox>(view, "BusinessInput");
            UiTestActions.SetText(business, "Uncommitted business text 中文");
            var icon = UiTestActions.Find<IconText>(view, "LocalizedIcon");
            var iconGeometry = icon.GetVisualDescendants().OfType<MaterialIcon>().Single().Drawing.Geometry;

            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            AssertFixtureText(view, "zh-CN");
            Assert.Same(model, view.DataContext);
            Assert.Same(model, icon.DataContext);
            Assert.Equal("Uncommitted business text 中文", business.Text);
            Assert.Equal(business.Text, model.BusinessText);
            Assert.Equal("Cancel", icon.IconKey);
            Assert.Same(iconGeometry, icon.GetVisualDescendants().OfType<MaterialIcon>().Single().Drawing.Geometry);
            model.BusinessText = "View model still controls this input";
            Assert.Equal(model.BusinessText, business.Text);
            Localization.SetLanguage("en-US");
            AssertFixtureText(view, "en-US");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Direct language changes update the workbench, Dock, menus and settings without saving or editing business content.</summary>
    [AvaloniaFact]
    public async Task DirectSetLanguageRefreshesExistingWindowsMenusDockAndPreviewWithoutSavingPreferencesOrChangingUndo()
    {
        await using var context = new MainWindowTestContext();
        var emptySnapshot = context.Session.DocumentSnapshot;
        Localization.SetLanguage("zh-CN");
        Assert.Equal("AegiNEXT - 未命名项目", context.Window.Title);
        Localization.SetLanguage("en-US");
        Assert.Equal("AegiNEXT - Untitled project", context.Window.Title);
        Assert.Same(emptySnapshot, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await context.OpenMediaAsync();
        context.Session.Editor.AddSubtitle(new(0), new(1), "Business subtitle");
        var snapshot = context.Session.DocumentSnapshot;
        var undoLabel = context.Session.Editor.UndoLabel;
        var preferences = context.Session.Preferences;
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.SHORTCUTS);
        settings.ViewModel.Shortcuts.Gesture = "Control+";
        var draftCommand = settings.ViewModel.Shortcuts.SelectedRow!.Command;
        var appearanceEvents = 0;
        settings.AppearanceChanged += (_, _) => appearanceEvents++;
        context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("设置", settings.Title);
            Assert.Equal("视频预览", context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.PREVIEW].Title);
            Assert.EndsWith("视频预览", floating.Title, StringComparison.Ordinal);
            Assert.Equal("播放", context.ViewModel.Preview.PlayLabel);
            Assert.Equal("Control+", settings.ViewModel.Shortcuts.Gesture);
            Assert.Equal(draftCommand, settings.ViewModel.Shortcuts.SelectedRow!.Command);
            Assert.NotNull(settings.ViewModel.Shortcuts.Error);
            Assert.Equal(0, appearanceEvents);
            Assert.Same(snapshot, context.Session.DocumentSnapshot);
            Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
            Assert.Equal(preferences, context.Session.Preferences);
            foreach (var registered in new[] { context.Window, settings, floating })
            {
                var menu = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(registered));
                Assert.Contains(menu.Items.OfType<NativeMenuItem>(), item => item.Header == "文件");
            }

            context.Session.UpdatePreferences(preferences with { Theme = WorkbenchTheme.DARK, Volume = 0.375f });
            Assert.Equal("zh-CN", Localization.CurrentLanguageID);
            Assert.Equal("zh-CN", Localization.SelectedLanguageID);
            Assert.Equal(preferences.Language, context.Session.Preferences.Language);
            Assert.Equal("设置", settings.Title);
            Assert.Equal("Control+", settings.ViewModel.Shortcuts.Gesture);
            Assert.Same(snapshot, context.Session.DocumentSnapshot);
            Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Settings", settings.Title);
            Assert.Equal("Video preview", context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.PREVIEW].Title);
            Assert.Equal("Play", context.ViewModel.Preview.PlayLabel);
            Assert.Same(snapshot, context.Session.DocumentSnapshot);
            Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
        }
        finally
        {
            settings.Close();
            floating.Close();
        }
    }

    /// <summary>Repeated and invalid selections do not emit duplicate notifications or replace the active language.</summary>
    [AvaloniaFact]
    public async Task LanguageSelectionIsIdempotentAndRejectsUnknownOrBackgroundChanges()
    {
        using var environment = new UiTestEnvironment();
        var notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        Localization.LanguageChanged += handler;
        try
        {
            Localization.SetLanguage("EN-us");
            Assert.Equal(0, notifications);
            Localization.SetLanguage("zh-CN");
            Assert.Equal(1, notifications);
            Assert.Equal("zh-CN", Localization.SelectedLanguageID);
            Assert.Equal("zh-CN", Localization.CurrentLanguageID);
            Assert.Throws<ArgumentException>(() => Localization.SetLanguage("fr-FR"));
            Assert.Equal(1, notifications);
            Assert.Equal("保存", Localization.Get("Workbench.Save"));
            var backgroundException = await Task.Run(() => Record.Exception(() => Localization.SetLanguage("en-US")));
            Assert.IsType<InvalidOperationException>(backgroundException);
            Assert.Equal("zh-CN", Localization.CurrentLanguageID);
            Localization.SetLanguage("system");
            Assert.Equal("system", Localization.SelectedLanguageID);
            Assert.Equal("en-US", Localization.CurrentLanguageID);
            Assert.Equal(2, notifications);
            Localization.SetLanguage("SYSTEM");
            Assert.Equal(2, notifications);
        }
        finally
        {
            Localization.LanguageChanged -= handler;
        }
    }

    /// <summary>Background callbacks read the selected snapshot while number formatting keeps the numeric culture.</summary>
    [AvaloniaFact]
    public async Task SelectedLanguageIgnoresCallbackCultureAndDoesNotReplaceNumericCulture()
    {
        using var environment = new UiTestEnvironment();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Localization.SetLanguage("zh-CN");
            Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            var result = await Task.Run(() =>
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                return Localization.Get("Preview.Play");
            });
            Assert.Equal("播放", result);
            Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            Assert.Equal("保存", Localization.Format("Workbench.Save"));
            Assert.Contains("1234,5", Localization.Format("Workbench.TrackStyleChangeText", "Track", "Style", 1234.5), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>DSL completions translate their explanation while keeping executable keywords unchanged.</summary>
    [AvaloniaFact]
    public void CompletionHintsAndAnimationPropertyLabelsFollowServiceLanguageWithoutChangingDslKeywords()
    {
        using var environment = new UiTestEnvironment();
        const string SOURCE = "at 0 po";
        var english = Assert.Single(EffectScriptLanguage.Complete(SOURCE, SOURCE.Length));
        var englishProperties = Enum.GetValues<AnimationProperty>().Select(AnimationPropertyLocalization.Get).ToArray();
        Localization.SetLanguage("zh-CN");
        var chinese = Assert.Single(EffectScriptLanguage.Complete(SOURCE, SOURCE.Length));
        var chineseProperties = Enum.GetValues<AnimationProperty>().Select(AnimationPropertyLocalization.Get).ToArray();

        Assert.Equal("position", chinese.Insertion);
        Assert.Equal(chinese.Insertion, english.Insertion);
        Assert.Contains("向量", chinese.Hint, StringComparison.Ordinal);
        Assert.Contains("Vector", english.Hint, StringComparison.Ordinal);
        Assert.Equal(englishProperties.Length, chineseProperties.Length);
        for (var index = 0; index < englishProperties.Length; index++)
        {
            Assert.False(string.IsNullOrWhiteSpace(englishProperties[index]));
            Assert.False(string.IsNullOrWhiteSpace(chineseProperties[index]));
            Assert.NotEqual(englishProperties[index], chineseProperties[index]);
        }
    }

    /// <summary>Closing settings releases its language subscription and controls.</summary>
    [AvaloniaFact]
    public void ClosedSettingsWindowIsCollectibleAfterLanguageChanges()
    {
        using var environment = new UiTestEnvironment();
        var ownerButton = new Button { Content = "Owner focus" };
        var owner = new Window { Content = ownerButton };
        try
        {
            owner.Show();
            var weak = CreateClosedSettingsWindow(owner);
            Assert.Empty(owner.OwnedWindows);
            ShowAndCloseFocusSentinel(owner);
            owner.Activate();
            Assert.True(ownerButton.Focus());
            Assert.Same(ownerButton, owner.FocusManager!.GetFocusedElement());
            owner.Close();
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            CollectClosedWindows();

            Assert.False(weak.TryGetTarget(out _), "A closed settings window must not be retained by the process-level localization service.");
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>Disposing a programmatic localized binding stops later language notifications.</summary>
    [AvaloniaFact]
    public void DisposedObservableBindingStopsRefreshingText()
    {
        using var environment = new UiTestEnvironment();
        var text = new TextBlock();
        var binding = text.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.Save"));
        try
        {
            Assert.Equal("Save", text.Text);
            Localization.SetLanguage("zh-CN");
            Assert.Equal("保存", text.Text);
        }
        finally
        {
            binding.Dispose();
        }

        Localization.SetLanguage("en-US");
        Assert.NotEqual("Save", text.Text);
    }

    /// <summary>A discovered third language appears by native name, selects by ID, and persists with English fallback for missing text.</summary>
    [AvaloniaFact]
    public async Task DiscoveredThirdLanguageSelectsByIdentifierPersistsAndFallsBackForMissingTranslations()
    {
        using var environment = new UiTestEnvironment();
        using var store = new WorkbenchPreferencesStore(environment.DirectoryPath);
        var preferences = new WorkbenchPreferences { Language = "en-US" };
        var window = new SettingsWindow(preferences);
        var write = Task.CompletedTask;
        window.AppearanceChanged += (_, change) =>
        {
            preferences = preferences with { Language = change.Language, Theme = change.Theme, WindowMenuOnMac = change.WindowMenuOnMac };
            Localization.SetLanguage(change.Language);
            window.UpdatePreferences(preferences);
            write = store.SaveAsync(preferences, TestContext.Current.CancellationToken);
        };
        try
        {
            window.Show();
            var languages = UiTestActions.Find<ComboBox>(window, "LanguageCombo").Items.OfType<LanguageInfo>().ToArray();
            Assert.Contains(languages, language => language.LanguageID == "fr-CA" && language.LanguageName == "Français (Canada)");
            Assert.Contains(languages, language => language.LanguageID == "system");
            UiTestActions.SelectLanguage(window, "fr-CA");
            await write;

            Assert.Equal("fr-CA", Localization.SelectedLanguageID);
            Assert.Equal("fr-CA", Localization.CurrentLanguageID);
            Assert.Equal("Paramètres", window.Title);
            Assert.Equal("Enregistrer", Localization.Get("Workbench.Save"));
            Assert.Equal("Theme", Localization.Get("Settings.Theme"));
            Assert.Equal("Missing.Text", Localization.Get("Missing.Text"));
            Assert.Equal("fr-CA", store.Load().Language);
            Assert.Null(store.LoadError);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Completed and cancelled export text translates again without committing inactive input or altering the export snapshot.</summary>
    [AvaloniaTheory]
    [InlineData(false, "Encode complete", "压制完成")]
    [InlineData(true, "Cancelled", "已取消")]
    public async Task ExportFinalStatusRefreshesImmediatelyWhenLanguageChanges(bool cancel, string englishStatus, string chineseStatus)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var snapshot = context.Editor.Snapshot;
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
        {
            await context.Session.CancelExportAsync();
            await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        var model = context.Session.ViewModel.Export;
        model.VideoBitrateText = "Uncommitted inactive bitrate";
        Assert.Equal(englishStatus, model.Status);

        Localization.SetLanguage("zh-CN");

        Assert.Equal(chineseStatus, model.Status);
        Assert.Equal("Uncommitted inactive bitrate", model.VideoBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Localization.SetLanguage("en-US");
        Assert.Equal(englishStatus, model.Status);
        Assert.Equal("Uncommitted inactive bitrate", model.VideoBitrateText);
    }

    /// <summary>A dialog whose message closes over business arguments is released after closing.</summary>
    [AvaloniaFact]
    public void ClosedTrackStyleChangeDialogIsCollectibleAfterLanguageChanges()
    {
        using var environment = new UiTestEnvironment();
        var ownerButton = new Button { Content = "Owner focus" };
        var owner = new Window { Content = ownerButton };
        try
        {
            owner.Show();
            var weak = CreateClosedTrackStyleChangeDialog(owner);
            Assert.Empty(owner.OwnedWindows);
            owner.Activate();
            Assert.True(ownerButton.Focus());
            Assert.Same(ownerButton, owner.FocusManager!.GetFocusedElement());
            owner.Close();
            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();
            CollectClosedWindows();

            Assert.False(weak.TryGetTarget(out _), "A formatted localization provider must not capture and retain its dialog.");
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>Programmatic dialogs stop their localization bindings when closed even while callers still hold them.</summary>
    [AvaloniaFact]
    public void ClosedProgrammaticDialogsStopRefreshingTheirTitleAndButtonBindings()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        try
        {
            owner.Show();
            foreach (var dialog in new Window[] { new TrackStyleChangeDialog("Track", "Style", 1), new UnsavedProjectDialog() })
            {
                try
                {
                    dialog.Show(owner);
                    Localization.SetLanguage("zh-CN");
                    dialog.Close();
                    Assert.Empty(owner.OwnedWindows);
                    Assert.True(owner.IsVisible);
                    var title = dialog.Title;
                    var content = Assert.IsType<StackPanel>(dialog.Content);
                    var buttons = content.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
                    var buttonContents = buttons.Select(button => button.Content).ToArray();

                    Localization.SetLanguage("en-US");

                    Assert.Equal(title, dialog.Title);
                    Assert.Equal(buttonContents, buttons.Select(button => button.Content));
                }
                finally
                {
                    dialog.Close();
                }
            }
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>Actual workbench choice controls and invalid drafts survive language switches without creating undo or replacing the project.</summary>
    [AvaloniaFact]
    public async Task WorkbenchLanguageRoundTripPreservesInvalidNumberAndColorDraftsAndNonDefaultChoices()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        context.Session.Editor.SetSubtitleTiming(cue.Id, cue.Start, cue.Start + new MediaTime(5), TimelineEditMode.CROP);
        context.Session.Editor.UpdateSubtitle(cue.Id, line => line with { Text = "Subtitle ABC 中文 123" });
        context.Session.SelectCue(cue.Id);
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_EFFECTS);
        var cueID = cue.Id;
        var styleView = context.Window.Panels[WorkbenchPanelIds.STYLES];
        var effectsView = context.Window.Panels[WorkbenchPanelIds.EFFECTS];
        var alignment = UiTestActions.Find<SubtitleAlignmentPicker>(styleView, "AlignmentPicker");
        var blend = effectsView.FindControl<ComboBox>("BlendCombo")!;
        var interpolation = effectsView.FindControl<ComboBox>("InterpolationCombo")!;
        var number = effectsView.FindControl<NumericDraftInput>("RotationInput")!;
        var color = UiTestActions.Find<ColorDraftInput>(styleView, "FillPicker");
        context.ViewModel.Styles.Subtitle.CommitAlignment((int)TextAlignment.MIDDLE_CENTER);
        blend.SelectedIndex = (int)BlendMode.SCREEN;
        context.Session.Editor.SetKeyframe(cueID, AnimationProperty.ROTATION, new(new(1), 0));
        Assert.True(context.Session.SelectKeyframe(new(cueID, AnimationProperty.ROTATION, new(1), new(1))));
        interpolation.SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((int)TextAlignment.MIDDLE_CENTER, context.ViewModel.Styles.Subtitle.Alignment);
        Assert.Equal((int)BlendMode.SCREEN, context.ViewModel.Effects.Blend);
        Assert.Equal((int)KeyframeInterpolation.EASE_OUT, context.ViewModel.Effects.Interpolation);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, Assert.Single(context.Session.SelectedLayer!.Tracks
            .Single(track => track.Property == AnimationProperty.ROTATION).Keyframes).Interpolation);
        var originalNumberText = number.RawText;
        try
        {
            number.RawText = "7e-";
            color.Draft!.HexText = "#GG0000";
            Dispatcher.UIThread.RunJobs();
            var snapshot = context.Session.DocumentSnapshot;
            var undoLabel = context.Session.Editor.UndoLabel;
            var canUndo = context.Session.Editor.CanUndo;
            var colorValue = color.Draft.Value;
            Assert.True(color.Draft.HasError);

            foreach (var languageID in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(languageID);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal((int)TextAlignment.MIDDLE_CENTER, alignment.AlignmentIndex);
                Assert.Equal((int)TextAlignment.MIDDLE_CENTER, context.ViewModel.Styles.Subtitle.Alignment);
                Assert.Equal((int)BlendMode.SCREEN, blend.SelectedIndex);
                Assert.Equal((int)BlendMode.SCREEN, context.ViewModel.Effects.Blend);
                Assert.Equal((int)KeyframeInterpolation.EASE_OUT, interpolation.SelectedIndex);
                Assert.Equal((int)KeyframeInterpolation.EASE_OUT, context.ViewModel.Effects.Interpolation);
                Assert.Equal("7e-", number.RawText);
                Assert.Equal("7e-", context.ViewModel.Effects.RotationText);
                Assert.Equal("#GG0000", color.Draft.HexText);
                Assert.Equal("#GG0000", color.FindControl<TextBox>("ColorInput")!.Text);
                Assert.True(color.Draft.HasError);
                Assert.Equal(colorValue, color.Draft.Value);
                Assert.Same(snapshot, context.Session.DocumentSnapshot);
                Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
                Assert.Equal(canUndo, context.Session.Editor.CanUndo);
            }
        }
        finally
        {
            number.RawText = originalNumberText;
            color.Draft!.Restore("Hex");
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertFixtureText(LocalizationFixtureView view, string languageID)
    {
        var catalog = LocalizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "i18n"));
        Assert.Equal(catalog.Get(languageID, "Workbench.Codec"), UiTestActions.Find<TextBlock>(view, "StaticLabel").Text);
        Assert.Equal(catalog.Get(languageID, "Workbench.Font"), UiTestActions.Find<Label>(view, "LocalizedLabel").Content);
        var button = UiTestActions.Find<Button>(view, "LocalizedButton");
        Assert.Equal(catalog.Get(languageID, "Workbench.Save"), button.Content);
        Assert.Equal(catalog.Get(languageID, "Workbench.Save"), AutomationProperties.GetName(button));
        Assert.Equal(catalog.Get(languageID, "Workbench.AnchorPresetHint"), ToolTip.GetTip(button));
        Assert.Equal(catalog.Get(languageID, "Workbench.Cancel"), UiTestActions.Find<IconText>(view, "LocalizedIcon").Text);
        Assert.Equal(catalog.Get(languageID, "Workbench.Cancel"), AutomationProperties.GetName(UiTestActions.Find<IconText>(view, "LocalizedIcon")));
        Assert.Equal(catalog.Get(languageID, "Settings.Bold"), UiTestActions.Find<CheckBox>(view, "LocalizedCheck").Content);
        Assert.Equal(catalog.Get(languageID, "Settings.Advanced"), UiTestActions.Find<Expander>(view, "LocalizedExpander").Header);
        Assert.Equal(catalog.Get(languageID, "Workbench.File"), UiTestActions.Find<MenuItem>(view, "MenuCommand").Header);
        var contextMenu = UiTestActions.Find<TextBlock>(view, "StaticLabel").ContextMenu!;
        Assert.Equal(catalog.Get(languageID, "Workbench.Cancel"), contextMenu.Items.OfType<MenuItem>().Single().Header);
        var flyout = Assert.IsType<Flyout>(UiTestActions.Find<Button>(view, "FlyoutButton").Flyout);
        Assert.Equal(catalog.Get(languageID, "Workbench.Name"), Assert.IsType<TextBlock>(flyout.Content).Text);
        var templateLabels = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(label => label.Classes.Contains("localized-template")).ToArray();
        Assert.Equal(2, templateLabels.Length);
        Assert.All(templateLabels, label => Assert.Equal(catalog.Get(languageID, "Workbench.Open"), label.Text));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<SettingsWindow> CreateClosedSettingsWindow(Window owner)
    {
        var window = new SettingsWindow(new() { Language = "en-US" });
        try
        {
            window.Show(owner);
            window.UpdateLayout();
            return new(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowAndCloseFocusSentinel(Window owner)
    {
        var button = new Button { Content = "Focus sentinel", IsDefault = true };
        var window = new Window { Content = button };
        try
        {
            window.Show(owner);
            window.UpdateLayout();
            Assert.True(button.Focus());
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TrackStyleChangeDialog> CreateClosedTrackStyleChangeDialog(Window owner)
    {
        var window = new TrackStyleChangeDialog("Business track", "User preset", 3);
        try
        {
            window.Show(owner);
            window.UpdateLayout();
            Localization.SetLanguage("zh-CN");
            Assert.Equal("更新轨道字幕样式？", window.Title);
            var message = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("Business track", StringComparison.Ordinal) == true);
            Assert.Contains("User preset", message.Text, StringComparison.Ordinal);
            Assert.Contains("3", message.Text, StringComparison.Ordinal);
            return new(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CollectClosedWindows()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
