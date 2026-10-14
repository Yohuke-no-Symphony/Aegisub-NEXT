using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal static class UiTestCapture
{
    internal static void CaptureExportPanel(Window window, string name)
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
        Capture(window, directory, $"headless-export-{name}.png");
    }

    internal static void CaptureWorkbench(MainWindow main)
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
        main.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        main.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.OPEN_STYLE_LIBRARY).Execute(null);
        var library = Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>());
        library.UpdateStyles([new SubtitleStylePreset(Guid.NewGuid(), "Review subtitle", new() { FontSize = 48 })]);
        foreach (var (themeIndex, languageID, suffix) in new[] { (1, "en-US", "light-en-US"), (2, "zh-CN", "dark-zh-CN") })
        {
            settings.SelectPage(SettingsPage.APPEARANCE);
            UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = themeIndex;
            UiTestActions.SelectLanguage(settings, languageID);
            Capture(main, directory, $"headless-main-{suffix}.png");
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                settings.SelectPage(page);
                Capture(settings, directory, $"headless-settings-{page.ToString().ToLowerInvariant()}-{suffix}.png");
            }
            foreach (var page in Enum.GetValues<EffectLibraryPage>())
            {
                library.SelectPage(page);
                Capture(library, directory, $"headless-effect-library-{page.ToString().ToLowerInvariant()}-{suffix}.png");
            }
        }
    }

    private static void Capture(Window window, string directory, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The headless Skia renderer did not produce a frame.");
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
