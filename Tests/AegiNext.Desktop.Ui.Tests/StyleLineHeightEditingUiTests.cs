using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Styles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StyleLineHeightEditingUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public void SettingsLineHeightUsesTheSharedInputOutsideAdvancedAndSavesItsDraft(string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new SettingsWindow(new() { Language = language })
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            var original = new SubtitleStylePreset(Guid.NewGuid(), "Line height", new());
            window.UpdateStyles([original]);
            var requests = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, request) => requests.Add(request.Preset);
            window.SelectPage(SettingsPage.STYLES);
            var view = UiTestActions.Find<StyleSettingsView>(window, "StylesView");
            var input = UiTestActions.Find<NumericDraftInput>(view, "LineHeightInput");
            Assert.DoesNotContain(input.GetVisualAncestors(), ancestor => ancestor is Expander);
            Assert.Equal(0.1m, input.Minimum);
            Assert.Equal(10m, input.Maximum);
            Assert.Equal(0.1m, input.Increment);
            Type(window, Assert.Single(input.GetVisualDescendants().OfType<TextBox>()), "1.8");
            Assert.Equal("1.8", window.ViewModel.Styles.LineHeightText);
            Assert.Equal(1.8m, input.Value);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Null(window.ViewModel.Styles.Error);
            var saved = Assert.Single(requests);
            Assert.Equal(original.Id, saved.Id);
            Assert.Equal(1.8, saved.Style.LineHeight);
            window.UpdateStyles([saved], saved.Id);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.8, Assert.Single(window.ViewModel.Styles.Styles).Style.LineHeight);
            Assert.False(window.ViewModel.Styles.IsDirty);
            Assert.Equal("1.8", input.RawText);
            Assert.Equal(1.8m, input.Value);
            Capture(window, input, $"settings-line-height-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "en-US", false)]
    [InlineData(true, "zh-CN", true)]
    public async Task SharedPanelLineHeightPreviewsThenCommitsOnceAndUndoRedoReloadsItsTextAndValue(bool blur, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        Localization.SetLanguage(language);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "First\nSecond");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var sink = new TextBox();
        var content = new Grid { RowDefinitions = new("*,Auto") };
        content.Children.Add(view);
        Grid.SetRow(sink, 1);
        content.Children.Add(sink);
        var host = new Window
        {
            Width = 320, Height = 1000, Content = content,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "LineHeightInput");
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Type(host, text, "1.8");
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal(1.8, context.Session.PreviewDocument.Subtitles[0].Style.LineHeight);
            if (blur)
            {
                Assert.True(sink.Focus());
            }
            else
            {
                UiTestActions.Press(host, Key.Enter);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.8, context.Session.SelectedCue!.Style.LineHeight);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal((decimal)original.Subtitles[0].Style.LineHeight, input.Value);
            Assert.Equal(context.ViewModel.Styles.Subtitle.LineHeightText, input.RawText);
            Assert.True(context.Session.Editor.Redo());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1.8", input.RawText);
            Assert.Equal(1.8m, input.Value);
            Capture(host, input, $"panel-line-height-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("LineHeightInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EscapeRestoresLineHeightAndKeepsThePendingShadowAxis()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Line height Escape");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1000, Content = view };
        try
        {
            host.Show();
            var lineHeight = UiTestActions.Find<NumericDraftInput>(host, "LineHeightInput");
            var text = Assert.Single(lineHeight.GetVisualDescendants().OfType<TextBox>());
            Type(host, text, "7e-");
            var shadow = UiTestActions.Find<NumericDraftInput>(host, "ShadowXInput");
            Type(host, Assert.Single(shadow.GetVisualDescendants().OfType<TextBox>()), "17");
            Assert.True(text.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.Session.TryCommitDrafts(false));

            UiTestActions.Press(host, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal((decimal)original.Subtitles[0].Style.LineHeight, lineHeight.Value);
            Assert.Equal("17", shadow.RawText);
            Assert.Equal(17, context.Session.PreviewDocument.Subtitles[0].Style.ShadowOffset.X);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(original.Subtitles[0].Style.LineHeight, context.Session.SelectedCue!.Style.LineHeight);
            Assert.Equal(17, context.Session.SelectedCue.Style.ShadowOffset.X);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("LineHeightInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MountedRawLineHeightRejectsOutOfRangeTextAndSelectionReloadsTheSelectedStyle()
    {
        await using var context = new MainWindowTestContext();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "First");
        var second = context.Session.Editor.AddSubtitle(new(5), new(9), "Second");
        context.Session.Editor.UpdateSubtitle(second, cue => cue with { Style = cue.Style with { LineHeight = 2.2 } });
        context.Session.SelectCue(first);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "LineHeightInput");
            context.ViewModel.Styles.Subtitle.LineHeightText = "10.01";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("10.01", input.RawText);
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.True(context.ViewModel.Styles.Subtitle.RestoreNumberField("LineHeightInput"));
            context.Session.SelectCue(second);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2.2", input.RawText);
            Assert.Equal(2.2m, input.Value);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.RestoreNumberField("LineHeightInput");
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task OrdinaryShapesHideTheSubtitleLineHeightInput()
    {
        await using var context = new MainWindowTestContext();
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 100), End = new(4)
        };
        context.Session.Editor.AddLayer(shape);
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1000, Content = view };
        try
        {
            host.Show();
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(), control => control.Name == "LineHeightInput");
        }
        finally
        {
            host.Close();
            view.Dispose();
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

    private static void Capture(Window window, Control input, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
