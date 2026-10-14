using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Styles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StyleScalingEditingUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public void SettingsReferenceFieldsPrecedeTypographyAndSaveTheirReferenceUnits(string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new EffectLibraryWindow(new() { Language = language })
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            var original = new SubtitleStylePreset(Guid.NewGuid(), "Reference", new() { FontSize = 72 });
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            var group = UiTestActions.Find<StackPanel>(view, "StyleScalingGroup");
            var typography = UiTestActions.Find<StackPanel>(view, "StyleTypographyGroup");
            var width = UiTestActions.Find<NumericDraftInput>(view, "ReferenceWidthInput");
            var height = UiTestActions.Find<NumericDraftInput>(view, "ReferenceHeightInput");
            var slider = UiTestActions.Find<Slider>(view, "MatchWidthOrHeightInput");
            Assert.True(group.TranslatePoint(default, view)!.Value.Y < typography.TranslatePoint(default, view)!.Value.Y);
            Assert.Equal(1m, width.Minimum);
            Assert.Equal(32768m, width.Maximum);
            Assert.Equal(1m, width.Increment);
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(1, slider.Maximum);
            Assert.Equal(0.5, slider.Value);
            Type(window, width, "1080");
            Type(window, height, "1920");
            Assert.True(slider.Focus());
            UiTestActions.Press(window, Key.End);
            var requests = new List<SubtitleStylePreset>();
            window.UpsertStyleRequested += (_, args) => requests.Add(args.Preset);

            UiTestActions.Click(window, "SaveStyleButton");

            Assert.Null(window.ViewModel.Styles.Error);
            var saved = Assert.Single(requests);
            Assert.Equal(1080, saved.Style.Scaling.ReferenceWidth);
            Assert.Equal(1920, saved.Style.Scaling.ReferenceHeight);
            Assert.Equal(1, saved.Style.Scaling.MatchWidthOrHeight);
            Assert.Equal(72, saved.Style.FontSize);
            Assert.Equal(original.Style.Margins, saved.Style.Margins);
            Assert.True(slider.Bounds.Height >= Assert.Single(slider.GetVisualDescendants().OfType<Thumb>()).Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsLanguageSwitchAndEscapeKeepOtherRawInputsAndStoredStyle()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            var original = new SubtitleStylePreset(Guid.NewGuid(), "Reference", new());
            window.UpdateStyles([original]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            var width = UiTestActions.Find<NumericDraftInput>(view, "ReferenceWidthInput");
            var height = UiTestActions.Find<NumericDraftInput>(view, "ReferenceHeightInput");
            Type(window, width, "7e-");
            Type(window, height, "720");
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("7e-", width.RawText);
            Assert.Equal(Localization.Get("Workbench.ReferenceResolution"),
                UiTestActions.Find<TextBlock>(view, "StyleScalingHeader").Text);
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal("ReferenceWidthInput", window.ViewModel.Styles.InvalidFieldKey);
            Assert.True(Assert.Single(width.GetVisualDescendants().OfType<TextBox>()).Focus());
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("1920", width.RawText);
            Assert.Equal("720", height.RawText);
            Assert.Equal(original, Assert.Single(window.ViewModel.Styles.Styles));
            Assert.True(window.ViewModel.Styles.IsDirty);
            Assert.Null(window.ViewModel.Styles.Error);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PanelSliderPreviewsReturnToStartAndCommitsOnlyOnceAfterPointerRelease()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Reference");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var width = UiTestActions.Find<NumericDraftInput>(host, "ReferenceWidthInput");
            Type(host, width, "1280");
            var slider = UiTestActions.Find<Slider>(host, "MatchWidthOrHeightInput");
            var (start, end) = DragPoints(host, slider);
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.InRange(context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight, 0.7, 0.9);
            Assert.Equal(1280, context.Session.PreviewDocument.Subtitles[0].Style.Scaling.ReferenceWidth);
            host.MouseMove(start);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.5, context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight, 8);
            host.MouseMove(end);
            Dispatcher.UIThread.RunJobs();
            var requested = slider.Value;

            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(requested, context.Session.SelectedCue!.Style.Scaling.MatchWidthOrHeight);
            Assert.Equal(1280, context.Session.SelectedCue.Style.Scaling.ReferenceWidth);
            Assert.Equal(original.Subtitles[0].Style.FontSize, context.Session.SelectedCue.Style.FontSize);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(0.5, slider.Value);
            Assert.Equal("1920", width.RawText);
            Assert.True(context.Session.Editor.Redo());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(requested, slider.Value);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("capture")]
    [InlineData("transfer")]
    [InlineData("template")]
    [InlineData("hide")]
    [InlineData("detach")]
    [InlineData("selection")]
    [InlineData("escape")]
    public async Task CancelingPanelSliderRestoresItsSourceAndDoesNotLeaveAPendingCommit(string cancellation)
    {
        await using var context = new MainWindowTestContext();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "First");
        var second = context.Session.Editor.AddSubtitle(new(5), new(9), "Second");
        context.Session.SelectCue(first);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var slider = UiTestActions.Find<Slider>(host, "MatchWidthOrHeightInput");
            var template = slider.Template;
            IPointer? pointer = null;
            object? captureLostSource = null;
            slider.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer,
                RoutingStrategies.Tunnel, true);
            slider.AddHandler(InputElement.PointerCaptureLostEvent, (_, args) => captureLostSource = args.Source,
                RoutingStrategies.Direct, true);
            var (start, end) = DragPoints(host, slider);
            host.MouseDown(start, MouseButton.Left);
            var captured = Assert.IsAssignableFrom<Visual>(pointer!.Captured);
            Assert.Contains(slider, captured.GetSelfAndVisualAncestors());
            host.MouseMove(end);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight, 0.7, 0.9);

            switch (cancellation)
            {
                case "capture":
                    pointer.Capture(null);
                    Assert.Same(slider, captureLostSource);
                    break;
                case "transfer":
                    pointer.Capture(host);
                    Assert.Same(host, pointer.Captured);
                    Assert.Same(slider, captureLostSource);
                    break;
                case "template":
                    slider.Template = null;
                    host.UpdateLayout();
                    break;
                case "hide":
                    view.IsVisible = false;
                    break;
                case "detach":
                    host.Content = null;
                    break;
                case "selection":
                    context.Session.SelectCue(second);
                    break;
                case "escape":
                    UiTestActions.Press(host, Key.Escape);
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);
            slider.Template = template;
            view.IsVisible = true;
            host.Content = view;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(0.5, slider.Value);
            Assert.True(context.Session.TryCommitDrafts(false));
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
    public async Task ReturningPanelSliderToItsStartingValueCreatesNoUndo()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Reference");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var slider = UiTestActions.Find<Slider>(host, "MatchWidthOrHeightInput");
            var (start, end) = DragPoints(host, slider);
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Dispatcher.UIThread.RunJobs();
            host.MouseMove(start);
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(0.5, context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight, 8);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    private static (Point Start, Point End) DragPoints(Window host, Slider slider)
    {
        slider.BringIntoView();
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        var thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
        var start = thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), host)!.Value;
        var end = start + new Vector((slider.Bounds.Width - thumb.Bounds.Width) * 0.3, 0);
        return (start, end);
    }

    [AvaloniaTheory]
    [InlineData("capture")]
    [InlineData("transfer")]
    [InlineData("template")]
    [InlineData("hide")]
    [InlineData("detach")]
    [InlineData("selection")]
    [InlineData("escape")]
    public void CancelingSettingsSliderRestoresOnlyItsMatchBeforeChangingTheDraft(string cancellation)
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new());
            var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new()
            {
                Scaling = new() { MatchWidthOrHeight = 0.25 }
            });
            window.UpdateStyles([first, second], first.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            var slider = UiTestActions.Find<Slider>(view, "MatchWidthOrHeightInput");
            var template = slider.Template;
            IPointer? pointer = null;
            object? captureLostSource = null;
            slider.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer,
                RoutingStrategies.Tunnel, true);
            slider.AddHandler(InputElement.PointerCaptureLostEvent, (_, args) => captureLostSource = args.Source,
                RoutingStrategies.Direct, true);
            var (start, end) = DragPoints(window, slider);
            window.MouseDown(start, MouseButton.Left);
            var captured = Assert.IsAssignableFrom<Visual>(pointer!.Captured);
            Assert.Contains(slider, captured.GetSelfAndVisualAncestors());
            window.MouseMove(end);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(window.ViewModel.Styles.Scaling.MatchWidthOrHeight, 0.7, 0.9);
            var content = window.Content;

            switch (cancellation)
            {
                case "capture":
                    pointer.Capture(null);
                    Assert.Same(slider, captureLostSource);
                    break;
                case "transfer":
                    pointer.Capture(window);
                    Assert.Same(window, pointer.Captured);
                    Assert.Same(slider, captureLostSource);
                    break;
                case "template":
                    slider.Template = null;
                    window.UpdateLayout();
                    break;
                case "hide":
                    view.IsVisible = false;
                    break;
                case "detach":
                    window.Content = null;
                    break;
                case "selection":
                    window.ViewModel.Styles.SelectedStyle = second;
                    break;
                case "escape":
                    UiTestActions.Press(window, Key.Escape);
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(end, MouseButton.Left);
            slider.Template = template;
            window.Content = content;
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.ViewModel.Styles.IsDirty);
            Assert.Equal(cancellation == "selection" ? 0.25 : 0.5, slider.Value);
            Assert.Equal(first, window.ViewModel.Styles.Styles[0]);
            Assert.Equal(second, window.ViewModel.Styles.Styles[1]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PanelSliderKeyboardAdjustmentCommitsOnKeyReleaseAndUndoesOnce()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Reference");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1100, Content = view };
        try
        {
            host.Show();
            var slider = UiTestActions.Find<Slider>(host, "MatchWidthOrHeightInput");
            Assert.True(slider.Focus());
            host.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal(1, context.Session.PreviewDocument.Subtitles[0].Style.Scaling.MatchWidthOrHeight);

            host.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, context.Session.SelectedCue!.Style.Scaling.MatchWidthOrHeight);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(0.5, slider.Value);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    private static void Type(Window host, NumericDraftInput input, string text)
    {
        input.BringIntoView();
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
        host.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
}
