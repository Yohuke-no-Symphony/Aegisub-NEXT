using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class BusinessTypographyUiTests
{
    private static readonly string[] samples = ["TEST 123", "这是什么？123", "中文 abc 456"];
    private static readonly string[] monospaceFamilies = ["Cascadia Code", "Menlo", "Consolas", "Cascadia Code, Menlo, Consolas"];

    [AvaloniaFact]
    public async Task FloatingChromeAndRegisteredDialogsInheritBodyTypographyWithoutClippingLargeTitlesAndIcons()
    {
        await using var context = new MainWindowTestContext();
        context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        Assert.Contains("business-surface", floating.Classes);
        var panel = context.Window.Panels[WorkbenchPanelIds.PREVIEW];
        var icon = panel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("ui-icon"));
        Assert.True(double.IsNaN(icon.LineHeight) || icon.LineHeight >= icon.FontSize);
        var dialog = new UnsavedProjectDialog();
        try
        {
            context.WindowRegistry.RegisterAuxiliary(dialog);
            dialog.Show();
            dialog.UpdateLayout();
            Assert.Contains("business-surface", dialog.Classes);
            var body = dialog.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == Localization.Get("Workbench.UnsavedText"));
            Assert.Contains("PingFang SC", body.FontFamily.Name, StringComparison.Ordinal);
            Assert.Equal(20, body.LineHeight);
        }
        finally
        {
            dialog.Close();
            floating.Close();
        }
        var settings = new SettingsWindow(new());
        try
        {
            settings.Show();
            settings.UpdateLayout();
            var title = UiTestActions.Find<TextBlock>(settings, "PageTitle");
            Assert.Equal(28, title.LineHeight);
            Assert.True(title.LineHeight >= title.FontSize);
        }
        finally
        {
            settings.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkbenchInputsAndInternalNumericAndFontInputsShareCenteredMixedTextMetrics()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        UiTestActions.ExpandEffectsCategory(context.Window, "ClipCategory");
        UiTestActions.ExpandEffectsCategory(context.Window, "CompositeCategory");
        var names = new[] { "LayerStartInput", "LayerEndInput" };
        foreach (var name in names)
        {
            AssertInput(UiTestActions.Find<TextBox>(context.Window, name));
        }
        foreach (var name in new[] { "RotationInput", "OpacityInput", "BlurInput", "QualityInput", "AudioBitrateInput" })
        {
            if (name == "AudioBitrateInput")
            {
                UiTestActions.Find<ComboBox>(context.Window, "AudioModeCombo").SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                context.Window.UpdateLayout();
            }
            var numeric = UiTestActions.Find<NumericDraftInput>(context.Window, name);
            numeric.BringIntoView();
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            context.Window.UpdateLayout();
            var boxes = numeric.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.True(boxes.Length == 1, $"Field={name}; Visible={numeric.IsEffectivelyVisible}; Enabled={numeric.IsEffectivelyEnabled}; Boxes={boxes.Length}");
            AssertInput(boxes[0]);
        }
        var font = UiTestActions.Find<FontFamilyPicker>(context.Window, "FontCombo");
        AssertInput(Assert.Single(font.GetVisualDescendants().OfType<TextBox>()));
        var color = UiTestActions.Find<ColorDraftInput>(context.Window, "FillPicker");
        AssertInput(color.FindControl<TextBox>("ColorInput")!);
        UiTestActions.Click(context.Window, "ModeButton");
        context.Window.UpdateLayout();
        AssertInput(color.FindControl<TextBox>("ColorInput")!);
        context.Window.Layouts.Activate(WorkbenchPanelIds.LOG);
        AssertInput(UiTestActions.Find<TextBox>(context.Window, "LogSearch"));
        Assert.Null(context.Window.Panels[WorkbenchPanelIds.LOG].FindControl<TextBox>("LogDetails"));
    }

    [AvaloniaFact]
    public void SettingsInputsUseSharedMetricsWhileDslRetainsItsNativeMonospaceCaretLayout()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.COLORS);
            var accent = UiTestActions.Find<ColorDraftInput>(window, "AccentPicker");
            AssertInput(accent.FindControl<TextBox>("ColorInput")!);
            window.SelectPage(SettingsPage.SHORTCUTS);
            AssertInput(UiTestActions.Find<TextBox>(window, "GestureInput"));
            window.UpdateStyles([new(Guid.NewGuid(), "Typography 中文 123", new())]);
            window.SelectPage(SettingsPage.STYLES);
            AssertInput(UiTestActions.Find<TextBox>(window, "StyleNameInput"));
            var size = UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput");
            AssertInput(Assert.Single(size.GetVisualDescendants().OfType<TextBox>()));
            window.SelectPage(SettingsPage.EFFECTS);
            AssertInput(UiTestActions.Find<TextBox>(window, "EffectScriptNameInput"));
            var script = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            Assert.Equal(VerticalAlignment.Top, script.VerticalContentAlignment);
            Assert.Contains(script.FontFamily.Name, monospaceFamilies);
            Assert.DoesNotContain("PingFang", script.FontFamily.Name, StringComparison.Ordinal);
            Assert.Equal(20, script.LineHeight);
            Dispatcher.UIThread.RunJobs();
            var presenter = Assert.Single(script.GetVisualDescendants().OfType<EffectScriptTextPresenter>());
            Assert.Equal(script.FontFamily, presenter.FontFamily);
            script.CaretIndex = 6;
            Assert.Equal(6, presenter.CaretIndex);
            Assert.True(presenter.TextLayout.HitTestTextPosition(6).Width >= 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertInput(TextBox input)
    {
        Assert.Contains("PingFang SC", input.FontFamily.Name, StringComparison.Ordinal);
        Assert.Equal(VerticalAlignment.Center, input.VerticalContentAlignment);
        Assert.Equal(20, input.LineHeight);
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
        Assert.Equal(input.FontFamily, presenter.FontFamily);
        var baselines = new List<double>();
        foreach (var text in samples)
        {
            using var layout = new TextLayout(text, new Typeface(presenter.FontFamily), presenter.FontSize,
                presenter.Foreground, lineHeight: presenter.LineHeight);
            var line = Assert.Single(layout.TextLines);
            Assert.Equal(20, line.Height);
            baselines.Add(line.Baseline);
        }
        Assert.InRange(baselines.Max() - baselines.Min(), 0, 0.5);
    }
}
