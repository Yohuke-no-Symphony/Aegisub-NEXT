using AegiNext.Desktop.EffectLibrary;
using System.Globalization;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Startup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsWindowUiTests
{
    private static readonly string[] numericStyleFieldNames =
    [
        "FontSizeInput", "StrokeWidthInput", "MarginLeftInput", "MarginRightInput", "MarginVerticalInput", "LineHeightInput", "ShadowBlurInput", "ShadowXInput",
        "ShadowYInput"
    ];

    [AvaloniaFact]
    public void AppearanceEventsAndLanguageRefreshPreserveInvalidShortcutDraft()
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences { Language = "en-US" };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            var updates = new List<SettingsAppearanceChangedEventArgs>();
            window.AppearanceChanged += (_, change) =>
            {
                updates.Add(change);
                preferences = preferences with
                {
                    Theme = change.Theme, Language = change.Language,
                    WindowMenuOnMac = change.WindowMenuOnMac
                };
                Localization.SetLanguage(change.Language);
                window.UpdatePreferences(preferences);
            };
            UiTestActions.Find<ComboBox>(window, "ThemeCombo").SelectedIndex = 2;
            Assert.Equal(WorkbenchTheme.DARK, Assert.Single(updates).Theme);
            var colorUpdates = new List<SettingsColorsChangedEventArgs>();
            window.ColorsChanged += (_, change) =>
            {
                colorUpdates.Add(change);
                preferences = preferences with { AccentColor = change.AccentColor, AudioGraph = change.AudioGraph };
                window.UpdatePreferences(preferences);
            };
            window.SelectPage(SettingsPage.COLORS);
            UiTestActions.Find<ColorDraftInput>(window, "AccentPicker").FindControl<ColorView>("Picker")!.Color = Color.Parse("#C54885");
            Assert.Single(updates);
            Assert.Equal("#C54885", Assert.Single(colorUpdates).AccentColor);

            window.SelectPage(SettingsPage.SHORTCUTS);
            var input = UiTestActions.Find<ShortcutGestureDisplay>(window, "GestureInput");
            Assert.NotNull(window.ViewModel.Shortcuts.SelectedRow);
            Assert.Same(window.ViewModel.Shortcuts.SelectedItem,
                UiTestActions.Find<ListBox>(window, "ShortcutList").SelectedItem);
            Assert.True(input.Focusable);
            window.ViewModel.Shortcuts.Gesture = "Control+";
            Assert.Equal("Control+", window.ViewModel.Shortcuts.Gesture);
            Assert.NotNull(window.ViewModel.Shortcuts.Error);
            window.SelectPage(SettingsPage.APPEARANCE);
            UiTestActions.SelectLanguage(window, "zh-CN");
            Assert.Equal(2, updates.Count);
            window.SelectPage(SettingsPage.SHORTCUTS);
            Assert.Equal("Control+", input.Gesture);
            Assert.NotNull(window.ViewModel.Shortcuts.Error);
            Assert.True(UiTestActions.Find<Grid>(window, "ShortcutsPage").IsVisible);
            Assert.Null(window.FindControl<Grid>("StylesPage"));
            Assert.False(UiTestActions.Find<ScrollViewer>(window, "AppearancePage").IsVisible);
            Assert.Equal("设置", window.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CapturedKeyAndConflictValidationUseActualRecorderEvents()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var saved = new List<SettingsShortcutsChangedEventArgs>();
            window.ShortcutsChanged += (_, change) => saved.Add(change);
            var input = UiTestActions.Find<ShortcutGestureDisplay>(window, "GestureInput");
            Assert.NotNull(window.ViewModel.Shortcuts.SelectedRow);
            UiTestActions.Click(window, "RecordShortcutButton");
            Assert.True(input.Focus());
            UiTestActions.Press(window, Key.F8);
            Assert.Equal("F8", window.ViewModel.Shortcuts.Gesture);
            Assert.Empty(saved);
            Assert.Contains(Localization.Get("Settings." + nameof(WorkbenchCommand.TIMING_ENTER)), window.ViewModel.Shortcuts.Error);
            UiTestActions.Click(window, "RecordShortcutButton");
            Assert.True(window.IsShortcutCaptureActive);
            Assert.True(input.Focus());
            UiTestActions.Press(window, Key.F6);
            Assert.Equal("F6", input.Gesture);
            Assert.False(window.IsShortcutCaptureActive);
            var bindings = Assert.Single(saved).Bindings;
            Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, bindings.Length);
            Assert.Equal("F6", bindings.Single(binding => binding.Command == WorkbenchCommand.NEW_PROJECT).Gesture);
            UiTestActions.Click(window, "ResetShortcutsButton");
            Assert.Equal(2, saved.Count);
            Assert.Equal<ShortcutBinding>(ShortcutDefaults.CreateBindings(), saved[^1].Bindings);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StyleEditsRetainEmbeddedFontAndHdrColorUntilFontIsChanged()
    {
        using var environment = new UiTestEnvironment();
        var font = new EmbeddedSubtitleFont("sample.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])),
            [1, 2, 3]);
        var original =
            new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { Fill = new(2.5, 1.25, 0.5, 0.9) }, font);
        var preferences = new WorkbenchPreferences { Language = "en-US", Theme = WorkbenchTheme.LIGHT };
        var window = new EffectLibraryWindow(preferences);
        var settings = new SettingsWindow(preferences);
        try
        {
            window.Show();
            settings.Show();
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.UpdateSelectionAvailability(true);
            var saved = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, change) => saved.Add(change.Preset);
            var appearanceChanges = 0;
            settings.AppearanceChanged += (_, change) =>
            {
                appearanceChanges++;
                preferences = preferences with
                {
                    Theme = change.Theme, Language = change.Language,
                    WindowMenuOnMac = change.WindowMenuOnMac
                };
                Localization.SetLanguage(change.Language);
                window.UpdatePreferences(preferences);
                settings.UpdatePreferences(preferences);
            };
            var alignment = UiTestActions.Find<SubtitleAlignmentPicker>(window, "AlignmentPicker");
            Assert.Equal((int)ProjectTextAlignment.BOTTOM_CENTER, alignment.AlignmentIndex);
            ClickVisibleControl(window, UiTestActions.Find<RadioButton>(window, "CustomPositionMode"));
            Assert.True(window.ViewModel.Styles.Position.IsExplicit);
            var anchor = UiTestActions.Find<NumericDraftInput>(window, "AnchorXInput");
            var offset = UiTestActions.Find<NumericDraftInput>(window, "OffsetXInput");
            SetVisibleNumericText(window, anchor, "0.25");
            SetVisibleNumericText(window, offset, "24");
            var position = window.ViewModel.Styles.Draft!.Style.Position;
            Assert.NotNull(position);
            SetVisibleNumericText(window, offset, "7e-");
            Assert.Equal("7e-", window.ViewModel.Styles.Position.OffsetX.RawText);
            Assert.Equal("OffsetXInput", window.ViewModel.Styles.Position.Validate());
            UiTestActions.Find<ToolbarToggleButton>(window, "BoldCheck").IsChecked = true;
            settings.SelectPage(SettingsPage.APPEARANCE);
            UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 2;
            UiTestActions.SelectLanguage(settings, "zh-CN");
            window.SelectPage(EffectLibraryPage.STYLES);
            Assert.Equal(2, appearanceChanges);
            Assert.Equal((int)ProjectTextAlignment.BOTTOM_CENTER, alignment.AlignmentIndex);
            Assert.Equal(Localization.Get("Workbench.AlignCenter"),
                ToolTip.GetTip(UiTestActions.Find<ToolbarToggleButton>(alignment, "HorizontalCenterButton")));
            Assert.Equal("0.25", anchor.RawText);
            Assert.Equal("7e-", offset.RawText);
            Assert.True(window.ViewModel.Styles.Position.IsExplicit);
            Assert.Equal(position, window.ViewModel.Styles.Draft.Style.Position);
            ClickVisibleControl(window, UiTestActions.Find<ToolbarToggleButton>(alignment, "HorizontalRightButton"));
            ClickVisibleControl(window, UiTestActions.Find<ToolbarToggleButton>(alignment, "VerticalTopButton"));
            settings.SelectPage(SettingsPage.APPEARANCE);
            UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 1;
            UiTestActions.SelectLanguage(settings, "en-US");
            window.SelectPage(EffectLibraryPage.STYLES);
            Assert.Equal(4, appearanceChanges);
            Assert.Equal((int)ProjectTextAlignment.TOP_RIGHT, alignment.AlignmentIndex);
            Assert.Equal(Localization.Get("Workbench.AlignTop"),
                ToolTip.GetTip(UiTestActions.Find<ToolbarToggleButton>(alignment, "VerticalTopButton")));
            Assert.Equal("0.25", anchor.RawText);
            Assert.Equal("7e-", offset.RawText);
            Assert.Equal(position, window.ViewModel.Styles.Draft.Style.Position);
            SetVisibleNumericText(window, offset, "24");
            UiTestActions.Click(window, "SaveStyleButton");
            var changed = Assert.Single(saved);
            Assert.True(changed.Style.Bold);
            Assert.Equal(original.Style.Fill, changed.Style.Fill);
            Assert.Same(font, changed.Font);
            Assert.Equal(ProjectTextAlignment.TOP_RIGHT, changed.Style.Alignment);
            Assert.Equal(position, changed.Style.Position);

            UiTestActions.Click(window, "DuplicateStyleButton");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.NotEqual(original.Id, saved[^1].Id);
            Assert.Same(font, saved[^1].Font);
            SetFontText(UiTestActions.Find<FontFamilyPicker>(window, "FontInput"), "serif");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Null(saved[^1].Font);
            window.UpdateSelectionAvailability(false);
            Assert.False(UiTestActions.Find<Button>(window, "CaptureStyleButton").IsEffectivelyEnabled);
            Assert.False(window.ViewModel.Styles.CanApply);
        }
        finally
        {
            settings.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FontPickerCombinesActualCatalogSearchAndFullDropdownWithoutChangingTheStyle()
    {
        using var environment = new UiTestEnvironment();
        var original =
            new SubtitleStylePreset(Guid.NewGuid(), "Imported", new() { FontFamily = "Project Imported Face" });
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        await application.Initialization;
        await application.Fonts.EnsureLoadedAsync();
        var expected = application.Fonts.Candidates.Select(value => value.Selection.FamilyName).Distinct().ToArray();
        Assert.NotEmpty(expected);
        var window = new EffectLibraryWindow(new());
        window.ViewModel.Styles.SetFonts(application.Fonts);
        try
        {
            window.Show();
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            Dispatcher.UIThread.RunJobs();
            var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontInput");
            Assert.All(expected, family => Assert.Contains(family, picker.FontFamilies));
            Assert.Contains("Project Imported Face", picker.FontFamilies);
            var changes = new List<string>();
            picker.FamilyCommitted += (_, value) => changes.Add(value.FamilyName);
            var input = picker.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(UiTestActions.Find<Button>(window, "SaveStyleButton").Focus());
            picker.OpenFontList();
            Assert.True(input.IsFocused);
            UiTestActions.Press(window, Key.Escape);
            UiTestActions.SetText(input, "project imported");
            Assert.True(picker.IsDropDownOpen);
            Assert.True(picker.ItemFilter!(picker.Text, "Project Imported Face"));
            Assert.False(picker.ItemFilter(picker.Text, "Another Family"));
            Assert.Empty(changes);
            var toggle = Assert.IsType<Button>(picker.InnerRightContent);
            picker.IsDropDownOpen = false;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsDropDownOpen);
            Assert.Equal("project imported", picker.Text);
            Assert.True(picker.ItemFilter(picker.Text, "Another Family"));
            Assert.Empty(changes);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal(original.Style.FontFamily, picker.Text);
            Assert.Empty(changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PointerSelectionCommitsTheExactFontVariantAndSavesItOnce()
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        await application.Initialization;
        await application.Fonts.EnsureLoadedAsync();
        var selected = application.Fonts.Candidates.First(candidate => candidate.Selection.Variant is { Weight: >= 700 }).Selection;
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Fixture", new());
        var window = new EffectLibraryWindow(new());
        window.ViewModel.Styles.SetFonts(application.Fonts);
        try
        {
            window.Show();
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontInput");
            var commits = new List<FontSelection>();
            picker.FamilyCommitted += (_, value) => commits.Add(value.Selection);
            var saved = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, value) => saved.Add(value.Preset);
            picker.OpenFontList();
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsAssignableFrom<MenuBase>(Assert.Single(picker.GetVisualDescendants().OfType<Popup>()).Child);
            var family = menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, selected.FamilyName));
            UiTestActions.ClickFontMenuItem(family);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (family.HasSubMenu)
            {
                Assert.Empty(commits);
                Assert.Equal(original.Style, window.ViewModel.Styles.Draft!.Style);
                Assert.True(family.IsSubMenuOpen);
                var variant = family.Items.Cast<MenuItem>().Single(item => Equals(item.Header, selected.Variant?.Name));
                UiTestActions.ClickFontMenuItem(variant);
            }
            Assert.Equal(selected, Assert.Single(commits));
            Assert.Equal(selected.Variant, window.ViewModel.Styles.Draft!.Style.FontVariant);
            Assert.Equal(selected.DisplayName, picker.Text);
            Assert.False(picker.IsDropDownOpen);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal(selected.FamilyName, Assert.Single(saved).Style.FontFamily);
            Assert.Equal(selected.Variant, saved[0].Style.FontVariant);
            Assert.Single(commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CustomFontDraftSurvivesCatalogAndLanguageRefreshAndCommitsOnlyOnce()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Custom", new() { FontFamily = "Custom Initial Face" })]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontInput");
            var commits = new List<string>();
            picker.FamilyCommitted += (_, value) => commits.Add(value.FamilyName);
            SetFontText(picker, "Future Custom Face");
            picker.RefreshFontFamilies(["Newly Imported Face"]);
            Localization.SetLanguage("zh-CN");
            window.RefreshLanguage();
            Assert.Equal("Future Custom Face", picker.Text);
            Assert.Contains("Newly Imported Face", picker.FontFamilies);
            Assert.Empty(commits);
            var saved = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, value) => saved.Add(value.Preset);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal("Future Custom Face", Assert.Single(saved).Style.FontFamily);
            Assert.Equal("Future Custom Face", Assert.Single(commits));
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Single(commits);
            picker.SetCurrentFamily("Custom Initial Face");
            Assert.Single(commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WideButtonsCenterTheirIconAndTextWhileFormLabelsKeepReadingAlignment()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var button = UiTestActions.Find<Button>(window, "RecordShortcutButton");
            button.Width = 260;
            button.Height = 64;
            window.UpdateLayout();
            var content = Assert.IsType<StackPanel>(Assert.IsType<AegiNext.Desktop.Controls.Common.IconText>(button.Content).Content);
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            Assert.Equal(HorizontalAlignment.Center, content.HorizontalAlignment);
            var point = content.TranslatePoint(default, button);
            Assert.NotNull(point);
            Assert.InRange(Math.Abs(point.Value.X + content.Bounds.Width / 2 - button.Bounds.Width / 2), 0, 1);
            Assert.InRange(Math.Abs(point.Value.Y + content.Bounds.Height / 2 - button.Bounds.Height / 2), 0, 1);
            var title = UiTestActions.Find<TextBlock>(window, "PageTitle");
            Assert.Contains(title.TextAlignment, new[] { TextAlignment.Start, TextAlignment.Left });
        }
        finally
        {
            window.Close();
        }
    }

    private static void SetFontText(FontFamilyPicker picker, string text)
    {
        picker.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetVisibleNumericText(Window window, NumericDraftInput input, string value)
    {
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        ClickVisibleControl(window, text);
        Assert.True(text.IsFocused);
        text.SelectAll();
        window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, text.Text);
        Assert.Equal(value, input.RawText);
    }

    private static void ClickVisibleControl(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyEnabled);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        control.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The settings input must be visible in its window.");
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MenuLocationUsesBoundPageModelAndReloadRetainsSelection()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Language = "en-US" });
        try
        {
            window.Show();
            var updates = new List<SettingsAppearanceChangedEventArgs>();
            window.AppearanceChanged += (_, value) => updates.Add(value);
            var menu = UiTestActions.Find<ComboBox>(window, "MenuLocationCombo");
            menu.SelectedIndex = 1;

            Assert.True(Assert.Single(updates).WindowMenuOnMac);
            window.UpdatePreferences(new() { WindowMenuOnMac = true, Language = "en-US" });
            Assert.Single(updates);
            Assert.Equal(1, menu.SelectedIndex);
            Assert.Equal(OperatingSystem.IsMacOS(), window.ViewModel.Appearance.ShowMenuLocation);
            Assert.Same(window.ViewModel.Appearance,
                UiTestActions
                    .Find<AegiNext.Desktop.Settings.Appearance.AppearanceSettingsView>(window, "AppearanceView")
                    .DataContext);
            Assert.Same(window.ViewModel.Shortcuts,
                UiTestActions.Find<AegiNext.Desktop.Settings.Shortcuts.ShortcutSettingsView>(window, "ShortcutsView")
                    .DataContext);
            Assert.Null(window.FindControl<AegiNext.Desktop.EffectLibrary.Styles.StyleLibraryView>("StylesView"));
            Assert.Equal(window.Title, window.TitleBar.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EmptyStyleFieldRetainsDraftAndBlocksSubmitUntilCorrected()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var requests = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, value) => requests.Add(value.Preset);
            var name = UiTestActions.Find<TextBox>(window, "StyleNameInput");
            UiTestActions.SetText(name, " ");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Empty(requests);
            Assert.Equal(" ", name.Text);
            Assert.True(UiTestActions.Find<TextBlock>(window, "EffectLibraryError").IsVisible);
            Assert.True(name.IsFocused);
            window.SelectPage(EffectLibraryPage.EFFECTS);
            window.SelectPage(EffectLibraryPage.STYLES);
            Assert.Equal(" ", name.Text);
            UiTestActions.SetText(name, "Updated");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal("Updated", Assert.Single(requests).Name);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("FontSizeInput", "72.5", 72.5)]
    [InlineData("LineHeightInput", "1.5", 1.5)]
    [InlineData("MarginLeftInput", "13.5", 13.5)]
    [InlineData("MarginRightInput", "41.5", 41.5)]
    [InlineData("MarginVerticalInput", "27.5", 27.5)]
    public void UnparsedStyleInputSurvivesFocusNavigationAndLanguageAndBlocksSaveAndApply(string fieldKey,
        string validText, double expected)
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
            window.UpdateSelectionAvailability(true);
            window.SelectPage(EffectLibraryPage.STYLES);
            Assert.All(numericStyleFieldNames,
                name => Assert.False(
                    string.IsNullOrWhiteSpace(UiTestActions.Find<NumericDraftInput>(window, name).RawText)));
            Assert.Equal(64, UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput").Value);
            var input = UiTestActions.Find<NumericDraftInput>(window, fieldKey);
            var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var saved = new List<SubtitleStylePreset>();
            var applied = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, value) => saved.Add(value.Preset);
            window.ApplyStyleRequested += (_, value) => applied.Add(value.Preset);
            textBox.BringIntoView();
            window.UpdateLayout();
            Assert.True(textBox.Focus());
            UiTestActions.SetText(textBox, "7e-");

            UiTestActions.Click(window, "SaveStyleButton");
            window.ViewModel.Styles.ApplyCommand.Execute(null);

            Assert.Empty(saved);
            Assert.Empty(applied);
            Assert.Equal(fieldKey, window.ViewModel.Styles.InvalidFieldKey);
            Assert.Equal("7e-", input.RawText);
            Assert.Equal("7e-", textBox.Text);
            window.SelectPage(EffectLibraryPage.EFFECTS);
            Localization.SetLanguage("zh-CN");
            window.RefreshLanguage();
            window.SelectPage(EffectLibraryPage.STYLES);
            Assert.Equal("7e-", input.RawText);
            Assert.Equal("7e-", textBox.Text);
            Assert.True(UiTestActions.Find<TextBlock>(window, "EffectLibraryError").IsVisible);
            Assert.Equal(Localization.Get("Settings.StyleValidation"), window.ViewModel.Styles.Error);
            UiTestActions.SetText(textBox, validText);
            UiTestActions.Click(window, "SaveStyleButton");
            window.ViewModel.Styles.ApplyCommand.Execute(null);

            var preset = Assert.Single(saved);
            Assert.Equal(preset, Assert.Single(applied));
            Assert.Equal(expected, fieldKey switch
            {
                "FontSizeInput" => preset.Style.FontSize,
                "LineHeightInput" => preset.Style.LineHeight,
                "MarginLeftInput" => preset.Style.Margins.Left,
                "MarginRightInput" => preset.Style.Margins.Right,
                "MarginVerticalInput" => preset.Style.Margins.Vertical,
                _ => throw new ArgumentOutOfRangeException(nameof(fieldKey))
            });
            Assert.Null(window.ViewModel.Styles.Error);
            Assert.Null(window.ViewModel.Styles.InvalidFieldKey);
        }
        finally
        {
            window.Close();
        }
    }
}
