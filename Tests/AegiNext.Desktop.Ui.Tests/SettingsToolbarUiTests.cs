using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsToolbarUiTests
{
    private static readonly string[] shortcutControlNames =
    [
        "GestureInput", "RecordShortcutButton", "ClearShortcutButton", "ResetShortcutsButton"
    ];

    private static readonly string[] styleButtonNames =
    [
        "AddStyleButton", "DuplicateStyleButton", "DeleteStyleButton", "ImportStylesButton", "ExportStylesButton",
        "CaptureStyleButton", "SaveStyleButton"
    ];

    [AvaloniaTheory]
    [InlineData(860, 580, "en-US", false)]
    [InlineData(860, 580, "en-US", true)]
    [InlineData(860, 580, "zh-CN", false)]
    [InlineData(860, 580, "zh-CN", true)]
    [InlineData(1200, 820, "en-US", false)]
    [InlineData(1200, 820, "en-US", true)]
    [InlineData(1200, 820, "zh-CN", false)]
    [InlineData(1200, 820, "zh-CN", true)]
    public void ShortcutActionsShareOneVisibleRowWithoutASaveButton(double width, double height, string language,
        bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new SettingsWindow(new() { Language = language, Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT })
        {
            Width = width, Height = height
        };
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var actions = UiTestActions.Find<Grid>(window, "ShortcutActions");
            var controls = actions.Children.OfType<Control>().ToArray();
            Assert.Equal(shortcutControlNames, controls.Select(control => control.Name));
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "SaveShortcutsButton");
            var bounds = controls.Select(control => BoundsIn(control, window)).ToArray();
            foreach (var bound in bounds)
            {
                Assert.True(new Rect(window.ClientSize).Contains(bound));
                Assert.InRange(Math.Abs(bound.Y - bounds[0].Y), 0, 1);
            }

            for (var index = 1; index < bounds.Length; index++)
            {
                Assert.True(bounds[index - 1].Right < bounds[index].Left);
            }

            Capture(window, $"shortcuts-{width:0}x{height:0}-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(860, 580, "en-US", false)]
    [InlineData(860, 580, "en-US", true)]
    [InlineData(860, 580, "zh-CN", false)]
    [InlineData(860, 580, "zh-CN", true)]
    [InlineData(1200, 820, "en-US", false)]
    [InlineData(1200, 820, "en-US", true)]
    [InlineData(1200, 820, "zh-CN", false)]
    [InlineData(1200, 820, "zh-CN", true)]
    public void StyleActionsStayInOneTopRowAndTheLastAccentSaveRemainsClickable(double width, double height,
        string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new EffectLibraryWindow(new() { Language = language, Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT })
        {
            Width = width, Height = height
        };
        var saved = new List<SubtitleStylePreset>();
        window.UpsertStyleRequested += (_, args) => saved.Add(args.Preset);
        try
        {
            window.UpdateStyles([new(Guid.NewGuid(), "样式 Style 123", new())]);
            window.Show();
            window.SelectPage(EffectLibraryPage.STYLES);
            var toolbar = UiTestActions.Find<StackPanel>(window, "StyleLibraryToolbar");
            Assert.Equal(Orientation.Horizontal, toolbar.Orientation);
            var buttons = toolbar.Children.OfType<Button>().ToArray();
            Assert.Equal(styleButtonNames, buttons.Select(button => button.Name));
            Assert.Contains("accent", buttons[^1].Classes);
            var toolbarBounds = BoundsIn(toolbar, window);
            foreach (var button in buttons)
            {
                var bounds = BoundsIn(button, window);
                Assert.InRange(Math.Abs(bounds.Y - toolbarBounds.Y), 0, 1);
                Assert.True(toolbarBounds.Contains(bounds));
            }

            Assert.True(toolbarBounds.Bottom < BoundsIn(UiTestActions.Find<ListBox>(window, "StyleList"), window).Top);
            Assert.Equal(2, UiTestActions.Find<Grid>(window, "StylesPage").RowDefinitions.Count);
            Capture(window, $"styles-{width:0}x{height:0}-{language}-{(dark ? "dark" : "light")}.png");
            window.ViewModel.Styles.Name = "修改 Style 456";

            UiTestActions.Click(window, "SaveStyleButton");

            Assert.Equal("修改 Style 456", Assert.Single(saved).Name);
            Assert.True(new Rect(window.ClientSize).Contains(BoundsIn(buttons[^1], window)));
        }
        finally
        {
            window.Close();
        }
    }

    private static Rect BoundsIn(Control control, Control relativeTo)
    {
        var position = control.TranslatePoint(default, relativeTo);
        Assert.NotNull(position);
        return new(position.Value, control.Bounds.Size);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
