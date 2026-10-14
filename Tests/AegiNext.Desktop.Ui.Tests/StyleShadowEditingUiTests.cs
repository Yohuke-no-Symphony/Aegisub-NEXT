using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Styles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StyleShadowEditingUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("zh-CN", true)]
    public void SettingsShadowFieldsStayOutsideCollapsedAdvancedAndFocusInvalidVectorAxis(string language, bool dark)
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
            window.UpdateStyles([new SubtitleStylePreset(Guid.NewGuid(), "Shadow", new())]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var offset = UiTestActions.Find<VectorDraftInput>(window, "ShadowOffsetInput");
            var shadow = UiTestActions.Find<ColorDraftInput>(window, "ShadowPicker");
            var blur = UiTestActions.Find<NumericDraftInput>(window, "ShadowBlurInput");
            Assert.DoesNotContain(offset.GetVisualAncestors(), ancestor => ancestor is Expander);
            Assert.DoesNotContain(shadow.GetVisualAncestors(), ancestor => ancestor is Expander);
            Assert.DoesNotContain(blur.GetVisualAncestors(), ancestor => ancestor is Expander);
            var stylesView = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            Assert.All(stylesView.GetVisualDescendants().OfType<Expander>(), expander => Assert.False(expander.IsExpanded));
            var invalid = UiTestActions.Find<NumericDraftInput>(offset, "ShadowXInput");
            var text = Assert.Single(invalid.GetVisualDescendants().OfType<TextBox>());
            var y = UiTestActions.Find<NumericDraftInput>(offset, "ShadowYInput");
            Assert.True(offset.FocusField("ShadowYInput"));
            Assert.True(Assert.Single(y.GetVisualDescendants().OfType<TextBox>()).IsFocused);
            Assert.True(offset.FocusField("ShadowXInput"));
            Assert.True(text.IsFocused);
            Assert.False(offset.FocusField("UnknownShadowField"));
            Type(window, text, "7e-");
            UiTestActions.Click(window, "SaveStyleButton");
            Assert.Equal("ShadowXInput", window.ViewModel.Styles.InvalidFieldKey);
            Assert.Equal("7e-", text.Text);
            Assert.True(text.IsFocused || invalid.IsFocused);
            window.ViewModel.Styles.DiscardDraft();
            CaptureShadowFields(window, stylesView, $"settings-shadow-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "en-US", false)]
    [InlineData(true, "zh-CN", true)]
    public async Task NumericShadowInputPreviewsBeforeEnterOrBlurAndCommitsOneUndo(bool blur, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        Localization.SetLanguage(language);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Shadow preview");
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
            var input = UiTestActions.Find<NumericDraftInput>(host, "ShadowXInput");
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Type(host, text, "17");
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal(17, context.Session.PreviewDocument.Subtitles[0].Style.ShadowOffset.X);
            if (blur)
            {
                Assert.True(sink.Focus());
            }
            else
            {
                UiTestActions.Press(host, Key.Enter);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(17, context.Session.SelectedCue!.Style.ShadowOffset.X);
            Assert.True(context.Session.Editor.Undo());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal((decimal)original.Subtitles[0].Style.ShadowOffset.X, input.Value);
            Assert.True(context.Session.Editor.Redo());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("17", input.RawText);
            CaptureShadowFields(host, view, $"panel-shadow-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EscapeRestoresOneShadowAxisWithoutCommittingOtherPendingAxes()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Shadow escape");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "ShadowXInput");
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Type(host, text, "7e-");
            var yInput = UiTestActions.Find<NumericDraftInput>(host, "ShadowYInput");
            Type(host, Assert.Single(yInput.GetVisualDescendants().OfType<TextBox>()), "11");
            Assert.Equal("11", context.ViewModel.Styles.Subtitle.ShadowYText);
            Assert.Equal("7e-", context.ViewModel.Styles.Subtitle.ShadowXText);
            Assert.True(text.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Equal("11", context.ViewModel.Styles.Subtitle.ShadowYText);

            UiTestActions.Press(host, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal("11", context.ViewModel.Styles.Subtitle.ShadowYText);
            Assert.Equal((decimal)original.Subtitles[0].Style.ShadowOffset.X, input.Value);
            Assert.Equal(11, context.Session.PreviewDocument.Subtitles[0].Style.ShadowOffset.Y);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(11, context.Session.SelectedCue!.Style.ShadowOffset.Y);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task MountedShadowInputsKeepProgrammaticRawDraftsAndTheirNumericProjection()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Shadow raw binding");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 1000, Content = view };
        try
        {
            host.Show();
            var y = UiTestActions.Find<NumericDraftInput>(host, "ShadowYInput");
            var x = UiTestActions.Find<NumericDraftInput>(host, "ShadowXInput");
            var blur = UiTestActions.Find<NumericDraftInput>(host, "ShadowBlurInput");
            var styles = context.ViewModel.Styles.Subtitle;
            styles.ShadowYText = "11";
            styles.ShadowXText = "17";
            styles.ShadowBlurText = "4";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("11", styles.ShadowYText);
            Assert.Equal(11m, styles.ShadowY);
            Assert.Equal("11", y.RawText);
            Assert.Equal("17", styles.ShadowXText);
            Assert.Equal(17m, styles.ShadowX);
            Assert.Equal("17", x.RawText);
            Assert.Equal("4", styles.ShadowBlurText);
            Assert.Equal(4m, styles.ShadowBlur);
            Assert.Equal("4", blur.RawText);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(new ScenePoint(17, 11), context.Session.PreviewDocument.Subtitles[0].Style.ShadowOffset);
            Assert.Equal(4, context.Session.PreviewDocument.Subtitles[0].Style.ShadowBlur);
            styles.ShadowXText = "7e-";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("7e-", x.RawText);
            Assert.Equal("11", y.RawText);
            Assert.Equal(17m, styles.ShadowX);
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.True(styles.RestoreShadowField("ShadowXInput"));
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(11, context.Session.SelectedCue!.Style.ShadowOffset.Y);
            Assert.Equal(4, context.Session.SelectedCue.Style.ShadowBlur);
            var committed = context.Session.Editor.Snapshot;
            styles.ShadowBlurText = "513";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("513", styles.ShadowBlurText);
            Assert.Equal("513", blur.RawText);
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Same(committed, context.Session.Editor.Snapshot);
            Assert.True(styles.RestoreShadowField("ShadowBlurInput"));
            styles.ShadowXText = "1000000001";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1000000001", styles.ShadowXText);
            Assert.Equal("1000000001", x.RawText);
            Assert.False(context.Session.TryCommitDrafts(false));
            Assert.Same(committed, context.Session.Editor.Snapshot);
            Assert.True(styles.RestoreShadowField("ShadowXInput"));
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Same(committed, context.Session.Editor.Snapshot);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ShadowColorTextPreviewsAndEnterCommitsOnceWithAlpha()
    {
        await using var context = new MainWindowTestContext();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Shadow color");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 480, Height = 1000, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<ColorDraftInput>(host, "ShadowPicker");
            var text = input.FindControl<TextBox>("ColorInput")!;
            Type(host, text, "#00FF0080");
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal(new SceneColor(0, 1, 0, 128d / 255), context.Session.PreviewDocument.Subtitles[0].Style.ShadowColor);

            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new SceneColor(0, 1, 0, 128d / 255), context.Session.SelectedCue!.Style.ShadowColor);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(original.Subtitles[0].Style.ShadowColor, input.Draft!.Value);
            Type(host, text, "#broken");
            UiTestActions.Press(host, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal("#broken", text.Text);
            UiTestActions.Press(host, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(original.Subtitles[0].Style.ShadowColor, input.Draft.Value);
            Assert.False(input.Draft.IsDirty);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ShadowFieldsAreHiddenForOrdinaryShapes()
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
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(),
                control => control.Name is "StyleShadowFields" or "ShadowPicker" or "ShadowOffsetInput" or "ShadowBlurInput");
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

    private static void CaptureShadowFields(Window window, Control view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        UiTestActions.Find<StackPanel>(view, "StyleShadowFields").BringIntoView();
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
