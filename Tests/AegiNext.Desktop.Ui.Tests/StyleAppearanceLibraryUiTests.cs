using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Styles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StyleAppearanceLibraryUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task StyleLibraryControlsPreviewRestoreInvalidFieldsAndSaveAllAppearanceValues(string language)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new EffectLibraryWindow(new() { Language = language });
        try
        {
            window.Show();
            var preset = new SubtitleStylePreset(Guid.NewGuid(), "Appearance", new()
            {
                FontFamily = "sans-serif", FontSize = 28, FillBlur = 1
            });
            window.UpdateStyles([preset]);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.UpdateLayout();
            var model = window.ViewModel.Styles;
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            var spacing = UiTestActions.Find<NumericDraftInput>(view, "LetterSpacingInput");
            Type(window, spacing, "3");
            var fill = UiTestActions.Find<NumericDraftInput>(view, "FillBlurInput");
            Type(window, fill, "7e-");
            Assert.False(model.TryCreatePreviewPreset(out _));
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1", fill.RawText);
            Assert.Equal("3", spacing.RawText);
            var stroke = UiTestActions.Find<NumericDraftInput>(view, "StrokeBlurInput");
            Type(window, stroke, "5");
            var wrap = UiTestActions.Find<ComboBox>(view, "WrapModeInput");
            wrap.SelectedIndex = (int)SubtitleWrapMode.NATURAL;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(language, Localization.SelectedLanguageID);
            Assert.Equal(Localization.Get("Workbench.WrapNatural"), ((ComboBoxItem)wrap.Items[1]!).Content);
            Assert.True(model.TryCreatePreviewPreset(out var preview));
            Assert.Equal(3, preview!.Style.LetterSpacing);
            Assert.Equal(1, preview.Style.FillBlur);
            Assert.Equal(5, preview.Style.StrokeBlur);
            Assert.Equal(SubtitleWrapMode.NATURAL, preview.Style.WrapMode);
            await view.PreviewCompletion;
            Assert.False(model.HasPreviewError);
            SubtitleStylePreset? saved = null;
            model.UpsertRequested += (_, e) => saved = e.Preset;
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.NotNull(saved);
            Assert.Equal(preview.Style, saved.Style);
            UiTestCapture.CaptureExportPanel(window, "style-library-appearance-" + language);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Type(Window window, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
}
