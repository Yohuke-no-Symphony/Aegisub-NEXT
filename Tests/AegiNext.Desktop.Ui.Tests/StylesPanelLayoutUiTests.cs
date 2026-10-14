using AegiNext.Desktop.EffectLibrary;
using System.Globalization;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StylesPanelLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData(320, "en-US", false)]
    [InlineData(320, "en-US", true)]
    [InlineData(320, "zh-CN", false)]
    [InlineData(320, "zh-CN", true)]
    [InlineData(480, "en-US", false)]
    [InlineData(480, "en-US", true)]
    [InlineData(480, "zh-CN", false)]
    [InlineData(480, "zh-CN", true)]
    public async Task PresetActionsShareOneVisibleRowAndFontButtonReservesSquareHitArea(double width,
        string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(),
            "A very long subtitle style preset name 中文 123 that must leave both actions visible", new());
        await context.Session.Styles.UpsertAsync(preset);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Font dropdown fixture");
        context.Session.SelectCue(cueId);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window
        {
            Width = width, Height = 900, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Classes.Add("business-surface");
        try
        {
            window.Show();
            Flush(window);
            var row = UiTestActions.Find<Grid>(view, "StylePresetActions");
            var selector = UiTestActions.Find<ComboBox>(view, "SubtitleStylePresetCombo");
            selector.SelectedItem = context.ViewModel.Styles.Subtitle.Presets.Single(value => value.Id == preset.Id);
            Flush(window);
            var apply = UiTestActions.Find<Button>(view, "ApplyStyleButton");
            var manage = UiTestActions.Find<Button>(view, "ManageStylesButton");
            var selectorBounds = BoundsIn(selector, row);
            var applyBounds = BoundsIn(apply, row);
            var manageBounds = BoundsIn(manage, row);
            Assert.InRange(row.Bounds.Width, 64, width);
            Assert.True(selectorBounds.Width >= 64, $"Preset selector must remain usable at {width} DIP: {selectorBounds}.");
            Assert.InRange(Math.Abs(selectorBounds.Center.Y - applyBounds.Center.Y), 0, 1);
            Assert.InRange(Math.Abs(selectorBounds.Center.Y - manageBounds.Center.Y), 0, 1);
            Assert.True(selectorBounds.Right <= applyBounds.Left);
            Assert.True(applyBounds.Right <= manageBounds.Left);
            Assert.InRange(manageBounds.Right, row.Bounds.Width - 1, row.Bounds.Width + 1);
            Assert.True(apply.IsEffectivelyEnabled);
            Assert.True(manage.IsEffectivelyEnabled);
            AssertButtonLabelFits(apply, "ApplyStyle");
            AssertButtonLabelFits(manage, "ManageStyles");
            AssertPointerHits(window, selector);
            AssertPointerHits(window, apply);
            AssertPointerHits(window, manage);

            var picker = UiTestActions.Find<FontFamilyPicker>(view, "FontCombo");
            AssertSquareFontButton(window, picker);
            var original = context.Session.DocumentSnapshot;
            AssertFontClickOpensWithoutCommitting(window, picker);
            Assert.Same(original, context.Session.DocumentSnapshot);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public void SettingsFontButtonKeepsSquareGeometryAndSearchDraft(string language, bool dark)
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
            var preset = new SubtitleStylePreset(Guid.NewGuid(), "Font fixture", new() { FontFamily = "sans-serif" });
            window.UpdateStyles([preset]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontInput");
            AssertSquareFontButton(window, picker);
            var original = window.ViewModel.Styles.Draft!.Style;
            AssertFontClickOpensWithoutCommitting(window, picker);
            Assert.Equal(original, window.ViewModel.Styles.Draft.Style);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertButtonLabelFits(Button button, string key)
    {
        var content = Assert.IsType<StackPanel>(Assert.IsType<AegiNext.Desktop.Controls.Common.IconText>(button.Content).Content);
        var label = Assert.Single(content.Children.OfType<TextBlock>());
        Assert.Equal(Localization.Get("Workbench." + key), label.Text);
        Assert.True(content.DesiredSize.Width + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1,
            $"The {key} label and icon must fit inside the action button: content {content.DesiredSize}, button {button.Bounds}.");
    }

    private static void AssertSquareFontButton(Window window, FontFamilyPicker picker)
    {
        picker.BringIntoView();
        Flush(window);
        var toggle = Assert.IsType<Button>(picker.InnerRightContent);
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        Assert.True(picker.TryFindResource("WorkbenchControlHeight", out var resource));
        var expectedSize = Assert.IsType<double>(resource);
        Assert.InRange(Math.Abs(toggle.Bounds.Width - expectedSize), 0, 1);
        Assert.InRange(Math.Abs(toggle.Bounds.Height - expectedSize), 0, 1);
        Assert.InRange(Math.Abs(toggle.Bounds.Width - toggle.Bounds.Height), 0, 0.5);
        var bounds = BoundsIn(toggle, input);
        Assert.InRange(input.Bounds.Width - bounds.Right, 0, input.BorderThickness.Right + 1);
        Assert.InRange(Math.Abs(bounds.Center.Y - input.Bounds.Height / 2), 0, 1);
        var icon = Assert.IsType<MaterialIcon>(toggle.Content);
        Assert.Equal(MaterialIconKind.ChevronDown, icon.Kind);
        var iconBounds = BoundsIn(icon, toggle);
        Assert.InRange(Math.Abs(iconBounds.Center.X - toggle.Bounds.Width / 2), 0, 1);
        Assert.InRange(Math.Abs(iconBounds.Center.Y - toggle.Bounds.Height / 2), 0, 1);
        AssertPointerHits(window, toggle);
    }

    private static void AssertFontClickOpensWithoutCommitting(Window window, FontFamilyPicker picker)
    {
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        var family = picker.Text;
        var commits = new List<string>();
        picker.FamilyCommitted += (_, value) => commits.Add(value.FamilyName);
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput("Unmatched font search draft");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Unmatched font search draft", picker.Text);
        picker.IsDropDownOpen = false;
        var toggle = Assert.IsType<Button>(picker.InnerRightContent);
        var point = AssertPointerHits(window, toggle);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.IsDropDownOpen);
        Assert.True(input.IsFocused);
        Assert.Equal("Unmatched font search draft", picker.Text);
        Assert.True(picker.ItemFilter!(picker.Text, "A different font family"));
        Assert.Empty(commits);
        UiTestActions.Press(window, Key.Escape);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(family, picker.Text);
        Assert.Empty(commits);
    }

    private static Point AssertPointerHits(Window window, Control control)
    {
        Flush(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control),
            $"The center of {control.Name} must be inside its visible hit area.");
        return point.Value;
    }

    private static Rect BoundsIn(Visual control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo);
        Assert.NotNull(origin);
        return new(origin.Value, control.Bounds.Size);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
