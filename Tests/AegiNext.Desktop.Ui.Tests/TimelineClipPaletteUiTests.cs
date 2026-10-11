using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipPaletteUiTests
{
    [AvaloniaTheory]
    [InlineData(false, "en-US")]
    [InlineData(true, "zh-CN")]
    public async Task EveryClipColorInputFitsTheScrolledViewportInBothThemes(bool dark, string language)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var settings = OpenColors(context);
        settings.SelectPage(SettingsPage.APPEARANCE);
        UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = dark ? 2 : 1;
        UiTestActions.SelectLanguage(settings, language);
        settings.SelectPage(SettingsPage.COLORS);
        var scroll = UiTestActions.Find<ScrollViewer>(settings, "ColorsPage");
        Flush(settings);
        scroll.Offset = new(0, scroll.Extent.Height);
        Flush(settings);
        var viewportTop = scroll.TranslatePoint(new Point(), settings)!.Value.Y;
        foreach (var name in new[]
        {
            "TimelineSelectedClipPicker", "TimelineInactiveClipPicker", "TimelineStartLinePicker",
            "TimelineEndLinePicker", "TimelineSelectedRangeFillPicker", "TimelineInactiveRangeFillPicker", "TimelineMediaRangeFillPicker"
        })
        {
            var picker = UiTestActions.Find<ColorDraftInput>(settings, name);
            var top = picker.TranslatePoint(new Point(), settings)!.Value.Y;
            Assert.InRange(top, viewportTop, viewportTop + scroll.Viewport.Height - picker.Bounds.Height);
            Assert.True(picker.Bounds.Width >= 200);
        }
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var frame = settings.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"settings-timeline-clips-{language}-{(dark ? "dark" : "light")}.png"),
                PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTheory]
    [InlineData("TimelineSelectedClipPicker", 0)]
    [InlineData("TimelineInactiveClipPicker", 1)]
    [InlineData("TimelineStartLinePicker", 2)]
    [InlineData("TimelineEndLinePicker", 3)]
    [InlineData("TimelineSelectedRangeFillPicker", 4)]
    [InlineData("TimelineInactiveRangeFillPicker", 5)]
    [InlineData("TimelineMediaRangeFillPicker", 6)]
    public async Task EachClipColorCommitsRgbaFromActualInputAndPersistsAcrossSettingsReopen(string pickerName, int field)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var document = context.Session.DocumentSnapshot;
        var original = context.Session.Preferences;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.MediaDuration = 20;
        var viewport = timeline.Viewport;
        var background = MediaRangePixel(timeline);
        var settings = OpenColors(context);
        var picker = UiTestActions.Find<ColorDraftInput>(settings, pickerName);
        var changes = new List<SettingsColorsChangedEventArgs>();
        settings.ColorsChanged += (_, value) => changes.Add(value);
        Type(settings, picker, "#11223380");
        Assert.Equal(original, context.Session.Preferences);
        Assert.Empty(changes);

        UiTestActions.Press(settings, Key.Enter);

        var expected = WithColor(original.TimelineClips, field, "#11223380") with { AdaptToTheme = false };
        Assert.Equal(expected, context.Session.Preferences.TimelineClips);
        Assert.Equal(expected, Assert.Single(changes).TimelineClips);
        Assert.Equal(128 / 255d, picker.Draft!.Value.Alpha);
        Assert.False(picker.Draft.IsDirty);
        Assert.Equal(viewport, timeline.Viewport);
        if (field == 6)
        {
            Assert.NotEqual(background, MediaRangePixel(timeline));
        }
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await WaitForPersistence(context);
        settings.Close();
        settings = OpenColors(context);
        Assert.Equal("#11223380", UiTestActions.Find<ColorDraftInput>(settings, pickerName).Draft!.HexText);
        Assert.Equal(expected, settings.ViewModel.Colors.TimelineClips);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task InvalidClipColorSurvivesThemeLanguageAndAnotherFieldCommitAndEscapeRestoresOnlyItsField()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var document = context.Session.DocumentSnapshot;
        var original = context.Session.Preferences.TimelineClips;
        var settings = OpenColors(context);
        var start = UiTestActions.Find<ColorDraftInput>(settings, "TimelineStartLinePicker");
        var end = UiTestActions.Find<ColorDraftInput>(settings, "TimelineEndLinePicker");
        Type(settings, start, "#bad");
        UiTestActions.Press(settings, Key.Enter);
        Assert.True(start.Draft!.HasError);
        Assert.Equal(original, context.Session.Preferences.TimelineClips);

        settings.SelectPage(SettingsPage.APPEARANCE);
        UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 2;
        UiTestActions.SelectLanguage(settings, "zh-CN");
        settings.SelectPage(SettingsPage.COLORS);
        Assert.Equal("#bad", start.FindControl<TextBox>("ColorInput")!.Text);
        Assert.True(start.Draft.HasError);
        Type(settings, end, "#11223380");
        UiTestActions.Press(settings, Key.Enter);
        var expected = original with { AdaptToTheme = false, EndLine = "#11223380" };
        Assert.Equal(expected, context.Session.Preferences.TimelineClips);
        Assert.Equal("#bad", start.FindControl<TextBox>("ColorInput")!.Text);
        Assert.True(start.Draft.HasError);

        var startInput = start.FindControl<TextBox>("ColorInput")!;
        startInput.BringIntoView();
        Flush(settings);
        Assert.True(startInput.Focus());
        UiTestActions.Press(settings, Key.Escape);
        Assert.False(start.Draft.HasError);
        Assert.False(start.Draft.IsDirty);
        Assert.Equal("#11223380", end.Draft!.HexText);
        Assert.Equal(expected, context.Session.Preferences.TimelineClips);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ResetColorsClearsEveryInvalidClipInputAndPersistsOneColorUpdateWithoutProjectEdits()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var original = context.Session.Preferences with
        {
            AccentColor = "#AABBCC", AudioGraph = AudioGraphPalettes.Get(2), Theme = WorkbenchTheme.DARK,
            Language = "en-US", Volume = 0.4f,
            TimelineClips = new() { AdaptToTheme = false, SelectedClip = "#11223380" }
        };
        context.Session.UpdatePreferences(original);
        var document = context.Session.DocumentSnapshot;
        var settings = OpenColors(context);
        var changes = new List<SettingsColorsChangedEventArgs>();
        settings.ColorsChanged += (_, value) => changes.Add(value);
        var pickerNames = new[]
        {
            "TimelineSelectedClipPicker", "TimelineInactiveClipPicker", "TimelineStartLinePicker",
            "TimelineEndLinePicker", "TimelineSelectedRangeFillPicker", "TimelineInactiveRangeFillPicker", "TimelineMediaRangeFillPicker"
        };
        foreach (var name in pickerNames)
        {
            var picker = UiTestActions.Find<ColorDraftInput>(settings, name);
            Type(settings, picker, "#bad");
            UiTestActions.Press(settings, Key.Enter);
            Assert.True(picker.Draft!.HasError);
        }
        Assert.Empty(changes);
        UiTestActions.Click(settings, "ResetColorsButton");
        var defaults = new WorkbenchPreferences();
        var expected = original with
        {
            AccentColor = defaults.AccentColor, AudioGraph = defaults.AudioGraph, TimelineClips = defaults.TimelineClips
        };
        Assert.Equal(expected, context.Session.Preferences);
        Assert.Equal(defaults.TimelineClips, Assert.Single(changes).TimelineClips);
        foreach (var name in pickerNames)
        {
            var draft = UiTestActions.Find<ColorDraftInput>(settings, name).Draft!;
            Assert.False(draft.HasError);
            Assert.False(draft.IsDirty);
        }
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await WaitForPersistence(context);
        settings.Close();
        settings = OpenColors(context);
        Assert.Equal(defaults.TimelineClips, settings.ViewModel.Colors.TimelineClips);
    }

    private static SettingsWindow OpenColors(MainWindowTestContext context)
    {
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        return settings;
    }

    private static void Type(Window window, ColorDraftInput picker, string text)
    {
        var input = picker.FindControl<TextBox>("ColorInput")!;
        input.BringIntoView();
        Flush(window);
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, input.Text);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task WaitForPersistence(MainWindowTestContext context)
    {
        using var store = new WorkbenchPreferencesStore(context.Session.PreferencesStore.DirectoryPath);
        var expected = context.Session.Preferences;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (store.Load() != expected)
        {
            Assert.True(DateTime.UtcNow < deadline, "Clip colors were not persisted.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static TimelineClipPalette WithColor(TimelineClipPalette palette, int field, string value)
    {
        return field switch
        {
            0 => palette with { SelectedClip = value },
            1 => palette with { InactiveClip = value },
            2 => palette with { StartLine = value },
            3 => palette with { EndLine = value },
            4 => palette with { SelectedRangeFill = value },
            5 => palette with { InactiveRangeFill = value },
            6 => palette with { MediaRangeFill = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static SKColor MediaRangePixel(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        return pixels.GetPixel((int)(timeline.HeaderWidth + timeline.Viewport.Width * 0.37), (int)timeline.Bounds.Height - 15);
    }
}
