using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.I18n;
using System.Globalization;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ColorDraftInputUiTests
{
    [AvaloniaTheory]
    [InlineData("#204080", 1)]
    [InlineData("#20408080", 128d / 255)]
    public void ActualStyleHexUsesOpaqueSixDigitsOrSuffixAlpha(string text, double alpha)
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Alpha", new() { Fill = new(0.25, 0.5, 0.75, 0.73) })]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var input = UiTestActions.Find<ColorDraftInput>(window, "FillPicker");
            Type(window, input.FindControl<TextBox>("ColorInput")!, text);
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(alpha, window.ViewModel.Styles.Fill.Alpha);
            Assert.Equal(window.ViewModel.Styles.Fill, input.Draft!.Value);
            Assert.Equal(alpha, input.FindControl<ColorView>("Picker")!.Color.A / 255d);
            Assert.False(input.Draft.IsDirty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActualAccentHexCommitsOnceWithoutAlphaAndInvalidTextSurvivesLanguageUntilEscape()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.COLORS);
            var values = new List<SettingsColorsChangedEventArgs>();
            window.ColorsChanged += (_, args) => values.Add(args);
            var input = UiTestActions.Find<ColorDraftInput>(window, "AccentPicker");
            Assert.Same(window.ViewModel.Colors, input.DataContext);
            Assert.Same(window.ViewModel.Colors.AccentDraft, input.Draft);
            Assert.Same(input.Draft, input.FindControl<StackPanel>("DraftRoot")!.DataContext);
            Assert.False(input.Draft!.IsAlphaEnabled);
            var hex = input.FindControl<TextBox>("ColorInput")!;
            Type(window, hex, "#C5488580");
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("#C54885", Assert.Single(values).AccentColor);
            Assert.Equal("#C54885", hex.Text);
            Assert.False(input.Draft.IsDirty);
            Type(window, hex, "#GG0000");
            UiTestActions.Press(window, Key.Enter);
            Assert.NotNull(input.Draft.Error);
            Assert.Single(values);
            window.SelectPage(SettingsPage.SHORTCUTS);
            Localization.SetLanguage("zh-CN");
            window.SelectPage(SettingsPage.COLORS);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("#GG0000", hex.Text);
            Assert.NotNull(input.Draft.Error);
            Assert.True(hex.Focus());
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("#C54885", hex.Text);
            Assert.Null(input.Draft.Error);
            Assert.False(input.Draft.IsDirty);
            Assert.Single(values);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StylePickerAndModeTogglePreserveHdrUntilAnActualRgbaByteEdit()
    {
        using var environment = new UiTestEnvironment();
        var fill = new SceneColor(2.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234);
        var stroke = new SceneColor(1.25, 0.2, -0.1, 0.9);
        var shadow = new SceneColor(0.1, 0.2, 1.25, 0.4);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "HDR", new() { Fill = fill, Stroke = stroke, ShadowColor = shadow });
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([preset]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var saved = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, args) => saved.Add(args.Preset);
            var input = UiTestActions.Find<ColorDraftInput>(window, "FillPicker");
            Assert.Equal(fill, input.Draft!.Value);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal(fill, Assert.Single(saved).Style.Fill);
            Assert.Equal(stroke, saved[0].Style.Stroke);
            Assert.Equal(shadow, saved[0].Style.ShadowColor);
            Click(window, input.FindControl<Button>("ModeButton")!);
            Assert.Equal(ColorInputMode.RGBA, input.Draft.InputMode);
            Assert.Equal(fill, window.ViewModel.Styles.Fill);
            Assert.False(input.Draft.IsDirty);
            window.UpdateLayout();
            var text = input.FindControl<TextBox>("ColorInput")!;
            Type(window, text, "32,64,128,128");
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ColorRgbaCodec.TryParse("32,64,128,128", fill.Alpha, true, out var expected));
            Assert.Equal(expected, window.ViewModel.Styles.Fill);
            Assert.Equal(stroke, window.ViewModel.Styles.Stroke);
            Assert.Equal(shadow, window.ViewModel.Styles.ShadowColor);
            Assert.Equal("32,64,128,128", text.Text);
            Click(window, input.FindControl<Button>("ModeButton")!);
            Assert.Equal("#20408080", text.Text);
            Assert.False(input.Draft.IsDirty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InvalidColorBlocksStyleSaveAndActualEscapeRestoresOnlyThatField()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var saved = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, args) => saved.Add(args.Preset);
            var input = UiTestActions.Find<ColorDraftInput>(window, "FillPicker");
            var hex = input.FindControl<TextBox>("ColorInput")!;
            Type(window, hex, "#broken");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Empty(saved);
            Assert.Equal("FillPicker.Hex", window.ViewModel.Styles.InvalidFieldKey);
            Assert.Equal("#broken", hex.Text);
            Assert.True(hex.IsFocused);
            UiTestActions.Find<TextBox>(window, "StyleNameInput").Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(hex.IsFocused);
            Assert.Equal("#broken", hex.Text);
            Assert.True(hex.Focus());
            UiTestActions.Press(window, Key.Escape);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Single(saved);
            Assert.Null(input.Draft!.Error);
            Assert.Null(window.ViewModel.Styles.Error);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Type(Window window, TextBox input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, input.Text);
    }

    private static void Click(Window window, Button button)
    {
        button.BringIntoView();
        window.UpdateLayout();
        var point = button.TranslatePoint(new Avalonia.Point(16, 16), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
