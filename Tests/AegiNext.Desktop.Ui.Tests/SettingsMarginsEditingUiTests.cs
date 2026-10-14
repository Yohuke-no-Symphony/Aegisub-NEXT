using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsMarginsEditingUiTests
{
    [AvaloniaFact]
    public void EscapeRestoresOnlyTheFocusedMarginAndSaveFocusesTheRemainingInvalidField()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Original", new() { Margins = new(13, 41, 27) })]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var left = UiTestActions.Find<NumericDraftInput>(window, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(window, "MarginRightInput");
            var saved = 0;
            window.UpsertStyleRequested += (_, _) => saved++;
            SetText(window, left, "7e-");
            SetText(window, right, "-");
            left.BringIntoView();
            Flush(window);
            Assert.True(Text(left).Focus());

            UiTestActions.Press(window, Key.Escape);
            UiTestActions.Click(window, "SaveStyleButton");

            Assert.Equal("13", left.RawText);
            Assert.Equal("13", Text(left).Text);
            Assert.Equal("-", right.RawText);
            Assert.Equal("-", Text(right).Text);
            Assert.Equal("MarginRightInput", window.ViewModel.Styles.InvalidFieldKey);
            Assert.True(Text(right).IsFocused);
            Assert.False(window.ViewModel.Styles.TryCreatePreviewPreset(out _));
            Assert.Equal(new SubtitleMargins(13, 41, 27), window.ViewModel.Styles.Draft!.Style.Margins);
            Assert.Equal(0, saved);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(860, 580)]
    [InlineData(980, 720)]
    public void ChineseDarkSettingsKeepThreeMarginsEditableAndUseOneMergedPositionDiagram(double width, double height)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage("zh-CN");
        var window = new EffectLibraryWindow(new() { Language = "zh-CN", Theme = WorkbenchTheme.DARK })
        {
            Width = width, Height = height
        };
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "中文字幕样式", new()
            {
                Margins = new(13, 41, 27), Alignment = AegiNext.Core.Projects.TextAlignment.BOTTOM_CENTER
            })]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var editor = UiTestActions.Find<SubtitleMarginsEditor>(window, "MarginsEditor");
            var positionEditor = UiTestActions.Find<LayerPositionEditor>(window, "PositionEditor");
            Assert.False(positionEditor.ShowAutomaticPositionAction);
            Assert.False(UiTestActions.Find<Button>(positionEditor, "AutomaticPositionButton").IsEffectivelyVisible);
            var left = UiTestActions.Find<NumericDraftInput>(editor, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(editor, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(editor, "MarginVerticalInput");
            editor.BringIntoView();
            Flush(window);

            Assert.Same(window.ViewModel.Styles.Margins, editor.Draft);
            Assert.False(editor.ShowDiagram);
            Assert.Same(window.ViewModel.Styles.Position, positionEditor.Draft);
            Assert.True(positionEditor.ShowMargins);
            Assert.Equal(window.ViewModel.Styles.Margins.CreateMargins(), positionEditor.Margins);
            Assert.Equal((int)AegiNext.Core.Projects.TextAlignment.BOTTOM_CENTER, editor.Alignment);
            Assert.False(editor.IsExplicit);
            Assert.Equal(window.ViewModel.Styles.CanvasWidth, editor.CanvasWidth);
            Assert.Equal(window.ViewModel.Styles.CanvasHeight, editor.CanvasHeight);
            var leftBounds = BoundsIn(left, editor);
            var rightBounds = BoundsIn(right, editor);
            var verticalBounds = BoundsIn(vertical, editor);
            Assert.True(leftBounds.Width >= 64, $"Left input is too narrow: {leftBounds}.");
            Assert.True(rightBounds.Width >= 64, $"Right input is too narrow: {rightBounds}.");
            Assert.True(verticalBounds.Width >= 64, $"Vertical input is too narrow: {verticalBounds}.");
            Assert.True(leftBounds.Right <= rightBounds.Left);
            Assert.True(rightBounds.Right <= verticalBounds.Left);
            Assert.InRange(verticalBounds.Right, 0, editor.Bounds.Width + 1);
            AssertVisible(window, left);
            AssertVisible(window, right);
            AssertVisible(window, vertical);
            Assert.Equal(Localization.Get("Workbench.MarginLeft"), UiTestActions.Find<TextBlock>(editor, "MarginLeftLabel").Text);
            Assert.Equal(Localization.Get("Workbench.MarginRight"), UiTestActions.Find<TextBlock>(editor, "MarginRightLabel").Text);
            Assert.Equal(Localization.Get("Workbench.MarginVertical"), UiTestActions.Find<TextBlock>(editor, "MarginVerticalLabel").Text);
            Assert.Equal("13", left.RawText);
            Assert.Equal("41", right.RawText);
            Assert.Equal("27", vertical.RawText);
            Capture(window, width == 980 ? "margin-settings-zh-dark.png" : "margin-settings-zh-dark-min.png");
            var diagram = UiTestActions.Find<LayerPositionDiagram>(positionEditor, "PositionDiagram");
            Assert.Single(window.GetVisualDescendants().OfType<LayerPositionDiagram>());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<SubtitleMarginsDiagram>(), candidate =>
                candidate.IsVisible && candidate.GetVisualAncestors().All(ancestor => ancestor.IsVisible));
            diagram.BringIntoView();
            Flush(window);
            var diagramBounds = BoundsIn(diagram, window);
            Assert.True(diagramBounds.Height >= 96);
            Assert.InRange(diagramBounds.Left, 0, window.ClientSize.Width);
            Assert.InRange(diagramBounds.Top, 0, window.ClientSize.Height);
            Assert.InRange(diagramBounds.Bottom, 0, window.ClientSize.Height + 1);
            Assert.InRange(diagramBounds.Right, 0, window.ClientSize.Width + 1);
            Capture(window, width == 980 ? "position-settings-zh-dark.png" : "position-settings-zh-dark-min.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExplicitPositionDisablesOnlyVerticalMarginWithoutChangingItsDraft()
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new());
        try
        {
            window.Show();
            window.UpdateStyles([new(Guid.NewGuid(), "Original", new() { Margins = new(13, 41, 27) })]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var left = UiTestActions.Find<NumericDraftInput>(window, "MarginLeftInput");
            var right = UiTestActions.Find<NumericDraftInput>(window, "MarginRightInput");
            var vertical = UiTestActions.Find<NumericDraftInput>(window, "MarginVerticalInput");
            var automatic = UiTestActions.Find<RadioButton>(window, "AutomaticPositionMode");
            var custom = UiTestActions.Find<RadioButton>(window, "CustomPositionMode");
            var fields = UiTestActions.Find<StackPanel>(window, "PositionFields");
            var original = Assert.Single(window.ViewModel.Styles.Styles);
            var saved = 0;
            window.UpsertStyleRequested += (_, _) => saved++;
            Assert.True(automatic.IsChecked);
            Assert.False(fields.IsEffectivelyVisible);
            custom.BringIntoView();
            Flush(window);

            AssertVisible(window, custom);
            UiTestActions.Click(window, "CustomPositionMode");

            Assert.True(window.ViewModel.Styles.Position.IsExplicit);
            Assert.True(custom.IsChecked);
            Assert.False(automatic.IsChecked);
            Assert.True(fields.IsEffectivelyVisible);
            Assert.True(left.IsEffectivelyEnabled);
            Assert.True(right.IsEffectivelyEnabled);
            Assert.False(vertical.IsEffectivelyEnabled);
            Assert.Equal("27", vertical.RawText);
            Assert.Equal(27, window.ViewModel.Styles.Draft!.Style.Margins.Vertical);
            SetText(window, left, "19");
            SetText(window, right, "47");
            Assert.Equal(new SubtitleMargins(19, 47, 27), window.ViewModel.Styles.Draft.Style.Margins);
            automatic.BringIntoView();
            Flush(window);
            AssertVisible(window, automatic);
            UiTestActions.Click(window, "AutomaticPositionMode");

            Assert.False(window.ViewModel.Styles.Position.IsExplicit);
            Assert.True(automatic.IsChecked);
            Assert.False(custom.IsChecked);
            Assert.False(fields.IsEffectivelyVisible);
            Assert.True(vertical.IsEffectivelyEnabled);
            Assert.Equal("27", vertical.RawText);
            Assert.Equal("19", left.RawText);
            Assert.Equal("47", right.RawText);
            Assert.Same(original, Assert.Single(window.ViewModel.Styles.Styles));
            Assert.Equal(new SubtitleMargins(13, 41, 27), original.Style.Margins);
            Assert.Equal(0, saved);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBox Text(NumericDraftInput input)
    {
        return Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
    }

    private static void SetText(Window window, NumericDraftInput input, string value)
    {
        input.BringIntoView();
        Flush(window);
        Assert.True(Text(input).Focus());
        UiTestActions.SetText(Text(input), value);
        Flush(window);
        Assert.Equal(value, input.RawText);
    }

    private static Rect BoundsIn(Visual control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo);
        Assert.NotNull(origin);
        return new(origin.Value, control.Bounds.Size);
    }

    private static void AssertVisible(Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var bounds = BoundsIn(control, window);
        Assert.InRange(bounds.Left, 0, window.ClientSize.Width);
        Assert.InRange(bounds.Right, 0, window.ClientSize.Width + 1);
        Assert.InRange(bounds.Top, 0, window.ClientSize.Height);
        Assert.InRange(bounds.Bottom, 0, window.ClientSize.Height + 1);
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control),
            $"Expected pointer hit on {control.Name} at {point.Value}, got {hit.GetType().Name}.");
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_MARGIN_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
