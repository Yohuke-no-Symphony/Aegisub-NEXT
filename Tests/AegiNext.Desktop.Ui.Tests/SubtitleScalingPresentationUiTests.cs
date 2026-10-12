using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleScalingPresentationUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public async Task ReferenceControlsFitNarrowPanelsAndSettingsWithLocalizedLabels(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Reference subtitle");
        context.Session.SelectCue(cueId);
        Localization.SetLanguage(language);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var panel = new Window
        {
            Width = 280, Height = 760, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        var settings = new SettingsWindow(new() { Language = language })
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            panel.Show();
            settings.Show();
            settings.UpdateStyles([new SubtitleStylePreset(Guid.NewGuid(), "Reference subtitle", new())]);
            settings.SelectPage(SettingsPage.STYLES);
            Dispatcher.UIThread.RunJobs();
            panel.UpdateLayout();
            settings.UpdateLayout();
            AssertControls(panel);
            AssertControls(settings);
            Capture(panel, $"subtitle-scaling-panel-{language}.png");
            Capture(settings, $"subtitle-scaling-settings-{language}.png");
        }
        finally
        {
            settings.Close();
            panel.Close();
            view.Dispose();
        }
    }

    private static void AssertControls(Window window)
    {
        var group = UiTestActions.Find<StackPanel>(window, "StyleScalingGroup");
        var width = UiTestActions.Find<NumericDraftInput>(window, "ReferenceWidthInput");
        var height = UiTestActions.Find<NumericDraftInput>(window, "ReferenceHeightInput");
        var slider = UiTestActions.Find<Slider>(window, "MatchWidthOrHeightInput");
        var widthOrigin = width.TranslatePoint(default, group)!.Value;
        var heightOrigin = height.TranslatePoint(default, group)!.Value;

        Assert.Equal("1920", width.RawText);
        Assert.Equal("1080", height.RawText);
        Assert.Equal(0.5, slider.Value);
        Assert.True(width.Bounds.Width >= 60);
        Assert.True(height.Bounds.Width >= 60);
        Assert.True(widthOrigin.X + width.Bounds.Width <= group.Bounds.Width + 0.01);
        Assert.True(heightOrigin.X + height.Bounds.Width <= group.Bounds.Width + 0.01);
        Assert.Equal(Localization.Get("Workbench.ReferenceResolution"),
            UiTestActions.Find<TextBlock>(window, "StyleScalingHeader").Text);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("UI capture directory must be absolute.");
        }
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered UI frame.");
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
