using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Shortcuts;
using AegiNext.SubtitleFormats;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AssAppearanceImportUiTests
{
    [AvaloniaTheory]
    [InlineData(1, SubtitleWrapMode.NATURAL, "en-US")]
    [InlineData(2, SubtitleWrapMode.NO_WRAP, "zh-CN")]
    public async Task StyleSpacingAndWrapImportIntoEditableNativeControls(int wrap, SubtitleWrapMode mode, string language)
    {
        await using var context = new MainWindowTestContext();
        Import(context, 2, wrap);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var original = context.Session.DocumentSnapshot;
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 340, Height = 1000, Content = view };
        try
        {
            host.Show();
            host.UpdateLayout();
            var spacing = UiTestActions.Find<NumericDraftInput>(host, "LetterSpacingInput");
            var choice = UiTestActions.Find<ComboBox>(host, "WrapModeInput");
            Assert.Equal(-1.25m, spacing.Value);
            Assert.Equal((int)mode, choice.SelectedIndex);
            Assert.True(spacing.IsEffectivelyEnabled);
            Type(host, spacing, "-2");
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(-2, context.Session.PreviewDocument.Subtitles[0].Style.LetterSpacing);
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-2, context.Session.SelectedCue!.Style.LetterSpacing);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(-1.25m, spacing.Value);
            choice.SelectedIndex = (int)SubtitleWrapMode.GRAPHEME;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SubtitleWrapMode.GRAPHEME, context.Session.SelectedCue!.Style.WrapMode);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal((int)mode, choice.SelectedIndex);
            UiTestCapture.CaptureExportPanel(host, "ass-appearance-style-" + language);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(0, "SelectionFillBlurInput")]
    [InlineData(2, "SelectionStrokeBlurInput")]
    public async Task OverrideSpacingAndBlurImportIntoSelectionControlsWithPreviewAndOneUndo(int border, string blurField)
    {
        await using var context = new MainWindowTestContext();
        Import(context, border, 2);
        var original = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 1000;
        host.Height = 1100;
        var editor = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        editor.SetSelection(0, 2);
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        var spacing = UiTestActions.Find<NumericDraftInput>(host, "SelectionLetterSpacingInput");
        foreach (var expander in spacing.GetVisualAncestors().OfType<Expander>())
        {
            expander.IsExpanded = true;
        }
        host.UpdateLayout();
        var blur = UiTestActions.Find<NumericDraftInput>(host, blurField);
        var sigma = 4 * 2 / Math.Sqrt(Math.Log(256));
        Assert.Equal(3.5m, spacing.Value);
        Assert.Equal(sigma, (double)blur.Value!.Value, 10);
        Assert.Equal(sigma, (double)UiTestActions.Find<NumericDraftInput>(host, "SelectionShadowBlurInput").Value!.Value, 10);
        foreach (var input in new[] { spacing, blur })
        {
            editor.SetSelection(0, 2);
            Dispatcher.UIThread.RunJobs();
            Assert.True(input.IsEffectivelyEnabled);
            Type(host, input, "6");
            Assert.Same(original, context.Session.DocumentSnapshot);
            var preview = StyleAt(context.Session.PreviewDocument.Subtitles[0], 0);
            Assert.Equal(6, input == spacing ? preview.LetterSpacing : border == 0 ? preview.FillBlur : preview.StrokeBlur);
            Assert.Equal(sigma, preview.ShadowBlur, 10);
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            var changed = StyleAt(context.Session.SelectedCue!, 0);
            Assert.Equal(6, input == spacing ? changed.LetterSpacing : border == 0 ? changed.FillBlur : changed.StrokeBlur);
            var outside = StyleAt(context.Session.SelectedCue!, 2);
            Assert.Equal(input == spacing ? 3.5 : sigma,
                input == spacing ? outside.LetterSpacing : border == 0 ? outside.FillBlur : outside.StrokeBlur, 10);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.Equal(input == spacing ? 3.5 : sigma, (double)input.Value!.Value, 10);
        }
        UiTestCapture.CaptureExportPanel(host, "ass-appearance-selection-" + (border == 0 ? "fill" : "stroke"));
    }

    private static void Import(MainWindowTestContext context, int border, int wrap)
    {
        context.Session.Editor.Apply("ASS appearance canvas fixture", document => document with { Width = 640, Height = 360 });
        var imported = AssSubtitleFormat.Parse(Source(border, wrap), 640, 360);
        var expectedDiagnosticCodes = border == 0
            ? new[] { "Ass.BlurAppearance" }
            : new[] { "Ass.BlurAppearance", "Ass.ShadowComposition" };
        Assert.Equal(expectedDiagnosticCodes.Order(StringComparer.Ordinal),
            imported.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal));
        context.Session.Editor.ImportSubtitleLines(imported, "ASS appearance");
        context.Session.SelectCue(imported.Lines[0].Id);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset) => line.InlineSpans.FirstOrDefault(span =>
        span.Utf16Start <= offset && span.Utf16Start + span.Utf16Length > offset)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static string Source(int border, int wrap) =>
        "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\n" +
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Spacing, Outline, Shadow, Alignment, MarginL, MarginR, MarginV\n" +
        $"Style: Default,sans-serif,28,&H00FFFFFF,&H00808080,&H00000000,&H00000000,-1.25,{border},2,5,20,20,20\n" +
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        $"Dialogue: 0,0:00:00.00,0:00:04.00,Default,,0,0,0,,{{\\fsp3.5\\blur4\\q{wrap}}}AB 中文\n";

    private static void Type(Window window, NumericDraftInput input, string value)
    {
        input.BringIntoView();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
    }
}
