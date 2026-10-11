using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StylesAlignmentUiTests
{
    [AvaloniaTheory]
    [InlineData(300, false, "en-US")]
    [InlineData(300, true, "zh-CN")]
    [InlineData(650, false, "zh-CN")]
    [InlineData(650, true, "en-US")]
    public async Task AxisButtonsComposeAlignmentAndKeepNativeStyleAndUndoInSync(double width, bool dark, string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT, Language = language
        });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Long line\nShort");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = width, Height = 920, Content = view };
        try
        {
            host.Show();
            var picker = UiTestActions.Find<SubtitleAlignmentPicker>(host, "AlignmentPicker");
            Assert.Equal((int)TextAlignment.BOTTOM_CENTER, picker.AlignmentIndex);
            foreach (var button in picker.GetVisualDescendants().OfType<ToolbarToggleButton>())
            {
                Assert.Equal(32, button.Bounds.Width);
                Assert.Equal(32, button.Bounds.Height);
                Assert.IsType<MaterialIcon>(button.Content);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            }
            UiTestActions.Click(host, "HorizontalLeftButton");
            Assert.Equal(TextAlignment.BOTTOM_LEFT, context.Session.SelectedCue!.Style.Alignment);
            UiTestActions.Click(host, "VerticalTopButton");
            Assert.Equal(TextAlignment.TOP_LEFT, context.Session.SelectedCue.Style.Alignment);
            UiTestActions.Click(host, "HorizontalRightButton");
            Assert.Equal(TextAlignment.TOP_RIGHT, context.Session.SelectedCue.Style.Alignment);
            Assert.Null(context.Session.SelectedCue.Style.TextAlign);
            Assert.Equal(original.Subtitles[0].Text, context.Session.SelectedCue.Text);
            Assert.Equal(original.Subtitles[0].Style.Position, context.Session.SelectedCue.Style.Position);
            Assert.True(context.Session.Editor.Undo());
            Assert.Equal((int)TextAlignment.TOP_LEFT, picker.AlignmentIndex);
            Assert.True(context.Session.Editor.Redo());
            Assert.Equal((int)TextAlignment.TOP_RIGHT, picker.AlignmentIndex);
            var changed = context.Session.Editor.Snapshot;
            UiTestActions.Click(host, "HorizontalRightButton");
            Assert.Same(changed, context.Session.Editor.Snapshot);
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = language == "en-US" ? "zh-CN" : "en-US" });
            Dispatcher.UIThread.RunJobs();
            Assert.Same(changed, context.Session.Editor.Snapshot);
            Assert.Equal((int)TextAlignment.TOP_RIGHT, picker.AlignmentIndex);
            var left = UiTestActions.Find<ToolbarToggleButton>(picker, "HorizontalLeftButton");
            Assert.Equal(Localization.Get("Workbench.AlignLeft"), ToolTip.GetTip(left));
            Capture(host, $"styles-alignment-{width}-{(dark ? "dark" : "light")}-{language}.png");
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickingFromAFocusedNumberFieldCommitsDraftAndAlignmentOnce(bool invalid)
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Long line\nShort");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.SetText(text, invalid ? "invalid" : "88");
            Assert.Same(original, context.Session.Editor.Snapshot);
            var button = UiTestActions.Find<ToolbarToggleButton>(host, "HorizontalLeftButton");
            button.BringIntoView();
            host.UpdateLayout();
            var point = button.TranslatePoint(new Point(16, 16), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            host.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (invalid)
            {
                Assert.Same(original, context.Session.Editor.Snapshot);
                Assert.Equal("invalid", text.Text);
                Assert.False(context.Session.Editor.CanUndo);
                context.ViewModel.Styles.Subtitle.FontSizeText = original.Subtitles[0].Style.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(context.Session.TryCommitDrafts());
                return;
            }
            Assert.Equal(88, context.Session.SelectedCue!.Style.FontSize);
            Assert.Equal(TextAlignment.BOTTOM_LEFT, context.Session.SelectedCue.Style.Alignment);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ExplicitSameAxisClickClearsLegacyOverrideAndNativeEditsRefreshBothAxes()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Long line\nShort");
        context.Session.Editor.UpdateSubtitle(id, line => line with { Style = line.Style with { TextAlign = SubtitleTextAlignment.LEFT } });
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = view };
        try
        {
            host.Show();
            var picker = UiTestActions.Find<SubtitleAlignmentPicker>(host, "AlignmentPicker");
            UiTestActions.Click(host, "HorizontalCenterButton");
            Assert.Equal(TextAlignment.BOTTOM_CENTER, context.Session.SelectedCue!.Style.Alignment);
            Assert.Null(context.Session.SelectedCue.Style.TextAlign);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            context.Session.Editor.UpdateSubtitle(id, line => line with
            {
                Style = line.Style with { Alignment = TextAlignment.TOP_LEFT, TextAlign = null }
            });
            Assert.Equal((int)TextAlignment.TOP_LEFT, picker.AlignmentIndex);
            Assert.Null(context.Session.SelectedCue!.Style.TextAlign);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task RichTextToolbarHasNoDuplicateAlignmentControls()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Long line\nShort");
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        var toolbar = UiTestActions.Find<StackPanel>(host, "SelectionStyleToolbar");
        Assert.DoesNotContain(toolbar.GetVisualDescendants().OfType<Control>(), control =>
            control.Name is "AlignLeftButton" or "AlignCenterButton" or "AlignRightButton" or "TextAlignmentSeparator");
        Assert.Contains(toolbar.GetVisualDescendants().OfType<ToolbarToggleButton>(), control => control.Name == "BoldSelectionButton");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemainingRichFormattingKeepsDurationPopupEditsAtomic(bool invalid)
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "AB");
        context.Session.SelectCue(id);
        context.Session.Details.GenerateAllTiming();
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        Assert.True(context.Session.Details.SelectClip(original.Subtitles[0].Karaoke[0].Id));
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        var button = UiTestActions.Find<ToolbarToggleButton>(host, "BoldSelectionButton");
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        popup.ShowAt(axis);
        var input = UiTestActions.Find<NumericDraftInput>(Assert.IsAssignableFrom<Control>(popup.Child), "KaraokeDurationInput");
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        UiTestActions.SetText(text, invalid ? "invalid" : "1.5");
        Assert.Same(original, context.Session.Editor.Snapshot);

        button.BringIntoView();
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var point = button.TranslatePoint(new Point(16, 16), host)!.Value;
        host.MouseMove(point);
        host.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, context.Session.Editor.Snapshot);
        host.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        if (invalid)
        {
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.True(popup.IsOpen);
            Assert.Equal("invalid", text.Text);
            Assert.False(context.Session.Editor.CanUndo);
            context.Session.Details.EditDuration("1.5");
            Assert.True(context.Session.TryCommitDrafts());
            popup.Hide();
            return;
        }
        var changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.True(Assert.Single(changed.InlineSpans).Style.Bold);
        Assert.Equal(new MediaTime(3, 2), changed.Karaoke[0].End - changed.Karaoke[0].Start);
        Assert.Equal(original.Subtitles[0].Style.Alignment, changed.Style.Alignment);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void Capture(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
    }
}
