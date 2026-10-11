using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Styles;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleStyleGroupingUiTests
{
    private static readonly string[] groupNames =
    [
        "StyleTypographyGroup", "StylePaintGroup", "StyleShadowFields", "StyleLayoutGroup"
    ];

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task BothEntrypointsKeepTheSameExpandedInputGroupsAndOneMeasuredLayoutDiagram(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with { Language = language });
        Assert.Equal(language, Localization.CurrentLanguageID);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Grouped style", new()
        {
            FontFamily = "sans-serif", Margins = new(17, 39, 13)
        });
        await context.Session.Styles.UpsertAsync(preset);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Grouped inputs\n中文 ABC 123");
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var panelWindow = new Window
        {
            Width = 320, Height = 900, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        var settingsWindow = new SettingsWindow(new() { Language = language })
        {
            Width = 860, Height = 580,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            settingsWindow.ViewModel.Styles.SetPositionMeasurement((style, text) =>
                context.Session.MeasureStylePosition(style, text));
            panelWindow.Show();
            settingsWindow.Show();
            settingsWindow.UpdateStyles([preset]);
            settingsWindow.SelectPage(SettingsPage.STYLES);
            var settings = UiTestActions.Find<StyleSettingsView>(settingsWindow, "StylesView");
            Flush(panelWindow);
            Flush(settingsWindow);
            var panelGroups = Groups(panel);
            var settingsGroups = Groups(settings);
            AssertGroupOrder(panel, panelGroups);
            AssertGroupOrder(settings, settingsGroups);
            AssertGroupHeaders(panelGroups);
            AssertGroupHeaders(settingsGroups);

            AssertTypography(panelGroups[0], "FontCombo", context.ViewModel.Styles.Subtitle.Bold, context.ViewModel.Styles.Subtitle.Italic);
            AssertTypography(settingsGroups[0], "FontInput", settingsWindow.ViewModel.Styles.Bold,
                settingsWindow.ViewModel.Styles.Italic);
            AssertPaint(panelGroups[1]);
            AssertPaint(settingsGroups[1]);
            AssertShadow(panelGroups[2]);
            AssertShadow(settingsGroups[2]);
            AssertLayout(panelGroups[3], true);
            AssertLayout(settingsGroups[3], false);

            Assert.NotNull(UiTestActions.Find<ComboBox>(panel, "SubtitleStylePresetCombo"));
            Assert.NotNull(UiTestActions.Find<Button>(panel, "ApplyStyleButton"));
            Assert.DoesNotContain(Controls(settings), control => control.Name is "SubtitleStylePresetCombo" or "ApplyStyleButton");
            var name = UiTestActions.Find<TextBox>(settings, "StyleNameInput");
            var preview = UiTestActions.Find<StackPanel>(settings, "StylePreviewSection");
            Assert.Equal(preset.Name, name.Text);
            Assert.True(preview.IsEffectivelyVisible);
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(settings, "StylePreviewFrame"));
            Assert.DoesNotContain(Controls(panel), control => control.Name is "StyleNameInput" or "StylePreviewSection");
            Assert.DoesNotContain(settingsGroups, group => name.GetVisualAncestors().Contains(group));
            Assert.DoesNotContain(settingsGroups, group => preview.GetVisualAncestors().Contains(group));
            Assert.Single(panel.GetVisualDescendants().OfType<SubtitlePositionDiagram>(), diagram => diagram.IsEffectivelyVisible);
            Assert.Single(settings.GetVisualDescendants().OfType<SubtitlePositionDiagram>(), diagram => diagram.IsEffectivelyVisible);
            Assert.False(UiTestActions.Find<SubtitleMarginsDiagram>(panel, "MarginsDiagram").IsEffectivelyVisible);
            Assert.False(UiTestActions.Find<SubtitleMarginsDiagram>(settings, "MarginsDiagram").IsEffectivelyVisible);
            Assert.Equal(language, Localization.CurrentLanguageID);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(settingsWindow.ViewModel.Styles.IsDirty);
            Capture(panelWindow, panelGroups[0], $"panel-style-inputs-{language}-{(dark ? "dark" : "light")}.png");
            Capture(settingsWindow, settingsGroups[0], $"settings-style-inputs-{language}-{(dark ? "dark" : "light")}.png");
            Capture(panelWindow, UiTestActions.Find<SubtitlePositionDiagram>(panel, "PositionDiagram"),
                $"panel-style-groups-{language}-{(dark ? "dark" : "light")}.png");
            Capture(settingsWindow, UiTestActions.Find<SubtitlePositionDiagram>(settings, "PositionDiagram"),
                $"settings-style-groups-{language}-{(dark ? "dark" : "light")}.png");
            Capture(panelWindow, UiTestActions.Find<Grid>(panel, "PositionModeRow"),
                $"panel-position-mode-{language}-{(dark ? "dark" : "light")}.png");
            Capture(settingsWindow, UiTestActions.Find<Grid>(settings, "PositionModeRow"),
                $"settings-position-mode-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            settingsWindow.Close();
            panelWindow.Close();
            panel.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormattingIconPointerClicksCommitOneMainPanelEditAndKeepSettingsChangesInTheDraft(bool italic)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(preferences => preferences with { Language = "en-US" });
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Formatting style", new()
        {
            FontFamily = "sans-serif", Bold = false, Italic = false
        });
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Formatting ABC 中文");
        context.Session.Editor.UpdateSubtitle(cueId, cue => cue with { Style = preset.Style });
        context.Session.SelectCue(cueId);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var panelWindow = new Window { Width = 320, Height = 900, Content = panel };
        var settingsWindow = new SettingsWindow(new() { Language = "en-US" }) { Width = 860, Height = 580 };
        try
        {
            settingsWindow.ViewModel.Styles.SetPositionMeasurement((style, text) =>
                context.Session.MeasureStylePosition(style, text));
            panelWindow.Show();
            settingsWindow.Show();
            settingsWindow.UpdateStyles([preset]);
            settingsWindow.SelectPage(SettingsPage.STYLES);
            var toggleName = italic ? "ItalicCheck" : "BoldCheck";
            var panelToggle = UiTestActions.Find<ToolbarToggleButton>(panel, toggleName);
            var settingsToggle = UiTestActions.Find<ToolbarToggleButton>(settingsWindow, toggleName);
            Assert.False(panelToggle.IsChecked);
            Assert.False(settingsToggle.IsChecked);

            ClickFormattingIcon(panelWindow, panelToggle);
            Flush(panelWindow);

            var committed = context.Session.Editor.Snapshot;
            var committedStyle = Assert.Single(committed.Subtitles).Style;
            Assert.Equal(!italic, committedStyle.Bold);
            Assert.Equal(italic, committedStyle.Italic);
            Assert.True(panelToggle.IsChecked);
            Assert.Equal(!italic, context.ViewModel.Styles.Subtitle.Bold);
            Assert.Equal(italic, context.ViewModel.Styles.Subtitle.Italic);
            Assert.True(context.Session.Editor.Undo());
            Flush(panelWindow);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(panelToggle.IsChecked);
            Assert.True(context.Session.Editor.Redo());
            Flush(panelWindow);
            Assert.Same(committed, context.Session.Editor.Snapshot);
            Assert.True(panelToggle.IsChecked);
            Assert.True(context.Session.Editor.Undo());
            Flush(panelWindow);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);

            Assert.False(settingsWindow.ViewModel.Styles.IsDirty);
            ClickFormattingIcon(settingsWindow, settingsToggle);
            Flush(settingsWindow);

            var draftStyle = settingsWindow.ViewModel.Styles.Draft!.Style;
            Assert.Equal(!italic, draftStyle.Bold);
            Assert.Equal(italic, draftStyle.Italic);
            Assert.True(settingsToggle.IsChecked);
            Assert.Equal(!italic, settingsWindow.ViewModel.Styles.Bold);
            Assert.Equal(italic, settingsWindow.ViewModel.Styles.Italic);
            Assert.True(settingsWindow.ViewModel.Styles.IsDirty);
            Assert.Same(preset, Assert.Single(settingsWindow.ViewModel.Styles.Styles));
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);

            ClickFormattingIcon(settingsWindow, settingsToggle);
            Flush(settingsWindow);
            Assert.False(settingsToggle.IsChecked);
            Assert.False(settingsWindow.ViewModel.Styles.Draft!.Style.Bold);
            Assert.False(settingsWindow.ViewModel.Styles.Draft.Style.Italic);
            Assert.Same(preset, Assert.Single(settingsWindow.ViewModel.Styles.Styles));
            Assert.Same(original, context.Session.Editor.Snapshot);
        }
        finally
        {
            settingsWindow.Close();
            panelWindow.Close();
            panel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task GroupingKeepsGenericPaintInputsUsableForShapeSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 100), End = new(4)
        };
        context.Session.Editor.AddLayer(shape);
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 320, Height = 900, Content = panel };
        try
        {
            host.Show();
            Assert.False(context.ViewModel.Styles.Subtitle.HasCue);
            var fill = UiTestActions.Find<ColorDraftInput>(panel, "FillPicker");
            var stroke = UiTestActions.Find<ColorDraftInput>(panel, "StrokePicker");
            var width = UiTestActions.Find<NumericDraftInput>(panel, "StrokeWidthInput");
            Assert.True(fill.IsEffectivelyEnabled);
            Assert.True(stroke.IsEffectivelyEnabled);
            Assert.True(width.IsEffectivelyEnabled);
            Assert.DoesNotContain(panel.GetVisualDescendants(), value => value is FontFamilyPicker);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Control>(), value => value.Name == "MarginVerticalInput");
            fill.Draft!.HexText = "#FF0000";
            Flush(host);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(new SceneColor(1, 0, 0, 1), context.Session.SelectedLayer!.Fill);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            context.ViewModel.Styles.Shape.FillDraft.Restore("HexText");
            host.Close();
            panel.Dispose();
        }
    }

    private static void ClickFormattingIcon(Window window, ToolbarToggleButton toggle)
    {
        toggle.BringIntoView();
        Flush(window);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var target = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window);
        Assert.NotNull(target);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(target.Value));
        var hitPath = string.Join(" -> ", hit.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(control => $"{control.GetType().Name}#{control.Name} {BoundsIn(control, window)}"));
        Assert.True(ReferenceEquals(hit, toggle) || hit.GetVisualAncestors().Contains(toggle),
            $"{toggle.Name} pointer target {target.Value} hit {hitPath} instead of the toggle.");
        var clicks = 0;
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> clickTrace = (_, _) => clicks++;
        toggle.Click += clickTrace;
        try
        {
            UiTestActions.Click(window, toggle.Name!);
            Assert.Equal(1, clicks);
        }
        finally
        {
            toggle.Click -= clickTrace;
        }
    }

    private static StackPanel[] Groups(Control root)
    {
        return groupNames.Select(name => UiTestActions.Find<StackPanel>(root, name)).ToArray();
    }

    private static void AssertGroupOrder(Control root, StackPanel[] groups)
    {
        var names = groups.Select(group => group.Name).ToArray();
        var actual = root.GetVisualDescendants().OfType<StackPanel>()
            .Where(group => names.Contains(group.Name)).Select(group => group.Name).ToArray();
        Assert.Equal(names, actual);
        Assert.All(groups, group =>
        {
            Assert.True(group.IsEffectivelyVisible);
            Assert.True(group.Bounds.Width > 0 && group.Bounds.Height > 0);
            Assert.DoesNotContain(group.GetVisualAncestors(), ancestor => ancestor is Expander);
        });
        for (var index = 1; index < groups.Length; index++)
        {
            Assert.True(BoundsIn(groups[index - 1], root).Bottom <= BoundsIn(groups[index], root).Top);
        }
    }

    private static void AssertGroupHeaders(StackPanel[] groups)
    {
        var headers = new[]
        {
            ("StyleTypographyHeader", "Workbench.StyleTypographyGroup"),
            ("StylePaintHeader", "Workbench.StylePaintGroup"),
            ("StyleShadowHeader", "Workbench.StyleShadowGroup"),
            ("StyleLayoutHeader", "Workbench.StyleLayoutGroup")
        };
        for (var index = 0; index < groups.Length; index++)
        {
            var header = UiTestActions.Find<TextBlock>(groups[index], headers[index].Item1);
            var expected = Localization.Get(headers[index].Item2);
            Assert.NotEqual(headers[index].Item2, expected);
            Assert.Equal(expected, header.Text);
            Assert.True(header.DesiredSize.Width <= header.Bounds.Width + 1);
            var container = Assert.IsType<Border>(groups[index].Children[0]);
            Assert.Same(header, container.Child);
            Assert.InRange(BoundsIn(container, groups[index]).Top, 0, 1);
            Assert.InRange(Math.Abs(BoundsIn(header, container).Top - container.Padding.Top), 0, 1);
        }
    }

    private static void AssertTypography(StackPanel group, string fontName, bool? boldState, bool? italicState)
    {
        AssertFieldOrder(group, [fontName, "BoldCheck", "ItalicCheck", "FontSizeInput", "LineHeightInput"]);
        var font = UiTestActions.Find<FontFamilyPicker>(group, fontName);
        var size = UiTestActions.Find<NumericDraftInput>(group, "FontSizeInput");
        var lineHeight = UiTestActions.Find<NumericDraftInput>(group, "LineHeightInput");
        var bold = UiTestActions.Find<ToolbarToggleButton>(group, "BoldCheck");
        var italic = UiTestActions.Find<ToolbarToggleButton>(group, "ItalicCheck");
        var fontBounds = BoundsIn(font, group);
        var sizeBounds = BoundsIn(size, group);
        var lineBounds = BoundsIn(lineHeight, group);
        var boldBounds = BoundsIn(bold, group);
        var italicBounds = BoundsIn(italic, group);
        Assert.True(fontBounds.Width >= 64);
        Assert.True(fontBounds.Right <= boldBounds.Left);
        Assert.True(boldBounds.Right <= italicBounds.Left);
        Assert.InRange(Math.Abs(fontBounds.Center.Y - boldBounds.Center.Y), 0, 1);
        Assert.InRange(Math.Abs(boldBounds.Center.Y - italicBounds.Center.Y), 0, 1);
        Assert.True(fontBounds.Bottom <= sizeBounds.Top);
        Assert.True(boldBounds.Bottom <= sizeBounds.Top);
        Assert.True(italicBounds.Bottom <= lineBounds.Top);
        Assert.True(sizeBounds.Right <= lineBounds.Left);
        Assert.InRange(Math.Abs(sizeBounds.Center.Y - lineBounds.Center.Y), 0, 1);
        Assert.InRange(Math.Abs(sizeBounds.Width - lineBounds.Width), 0, 1);
        Assert.True(sizeBounds.Width >= 64 && lineBounds.Width >= 64);
        Assert.InRange(italicBounds.Right, 0, group.Bounds.Width + 1);
        AssertFormattingIcon(bold, "Workbench.Bold", MaterialIconKind.FormatBold);
        AssertFormattingIcon(italic, "Workbench.Italic", MaterialIconKind.FormatItalic);
        Assert.Equal(boldState, bold.IsChecked);
        Assert.Equal(italicState, italic.IsChecked);
        Assert.False(size.ShowButtonSpinner);
        Assert.False(lineHeight.ShowButtonSpinner);
    }

    private static void AssertFormattingIcon(ToolbarToggleButton toggle, string labelKey, MaterialIconKind kind)
    {
        Assert.Equal(32, toggle.Bounds.Width);
        Assert.Equal(32, toggle.Bounds.Height);
        Assert.Equal(Localization.Get(labelKey), ToolTip.GetTip(toggle));
        Assert.Equal(Localization.Get(labelKey), AutomationProperties.GetName(toggle));
        var icon = Assert.IsType<MaterialIcon>(toggle.Content);
        Assert.Equal(kind, icon.Kind);
        Assert.DoesNotContain(toggle.GetVisualDescendants(), visual => visual is TextBlock);
    }

    private static void AssertPaint(StackPanel group)
    {
        AssertFieldOrder(group, ["FillPicker", "StrokePicker", "StrokeWidthInput"]);
        var fill = UiTestActions.Find<ColorDraftInput>(group, "FillPicker");
        var stroke = UiTestActions.Find<ColorDraftInput>(group, "StrokePicker");
        var width = UiTestActions.Find<NumericDraftInput>(group, "StrokeWidthInput");
        AssertStacked(group, fill, stroke, width);
        Assert.False(width.ShowButtonSpinner);
    }

    private static void AssertShadow(StackPanel group)
    {
        AssertFieldOrder(group, ["ShadowPicker", "ShadowOffsetInput", "ShadowBlurInput"]);
        var color = UiTestActions.Find<ColorDraftInput>(group, "ShadowPicker");
        var offset = UiTestActions.Find<VectorDraftInput>(group, "ShadowOffsetInput");
        var blur = UiTestActions.Find<NumericDraftInput>(group, "ShadowBlurInput");
        AssertStacked(group, color, offset, blur);
        Assert.False(blur.ShowButtonSpinner);
    }

    private static void AssertLayout(StackPanel group, bool showReset)
    {
        AssertFieldOrder(group, ["AlignmentPicker", "MarginsEditor", "PositionEditor"]);
        var alignment = UiTestActions.Find<SubtitleAlignmentPicker>(group, "AlignmentPicker");
        var margins = UiTestActions.Find<SubtitleMarginsEditor>(group, "MarginsEditor");
        var position = UiTestActions.Find<SubtitlePositionEditor>(group, "PositionEditor");
        AssertStacked(group, alignment, margins, position);
        Assert.False(margins.ShowDiagram);
        Assert.True(position.ShowMargins);
        Assert.Equal(showReset, position.ShowAutomaticPositionAction);
        var modeRow = UiTestActions.Find<Grid>(position, "PositionModeRow");
        var modeLabel = UiTestActions.Find<TextBlock>(modeRow, "PositionModeLabel");
        Assert.Equal(Localization.Get("Workbench.PositionMode"), modeLabel.Text);
        Assert.True(modeLabel.DesiredSize.Width <= modeLabel.Bounds.Width + 1);
        var automatic = UiTestActions.Find<RadioButton>(modeRow, "AutomaticPositionMode");
        var custom = UiTestActions.Find<RadioButton>(modeRow, "CustomPositionMode");
        var reset = UiTestActions.Find<Button>(modeRow, "AutomaticPositionButton");
        Assert.True(automatic.IsEffectivelyVisible);
        Assert.True(custom.IsEffectivelyVisible);
        Assert.True(automatic.IsChecked);
        Assert.False(custom.IsChecked);
        Assert.False(UiTestActions.Find<StackPanel>(position, "PositionFields").IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<AnchorPresetPicker>(position, "AnchorPresets").IsEffectivelyVisible);
        var automaticBounds = BoundsIn(automatic, modeRow);
        var customBounds = BoundsIn(custom, modeRow);
        Assert.True(automaticBounds.Right <= customBounds.Left);
        Assert.InRange(Math.Abs(automaticBounds.Center.Y - customBounds.Center.Y), 0, 1);
        Assert.InRange(customBounds.Right, 0, modeRow.Bounds.Width + 1);
        AssertModeLabel(automatic, "Workbench.AutomaticPositionMode", "Workbench.AutomaticPositionMode");
        AssertModeLabel(custom, "Workbench.CustomPositionMode", "Workbench.SubtitlePositionHint");
        Assert.Equal(showReset, reset.IsEffectivelyVisible);
        if (showReset)
        {
            var resetBounds = BoundsIn(reset, modeRow);
            Assert.Equal(32, resetBounds.Width);
            Assert.Equal(32, resetBounds.Height);
            Assert.True(customBounds.Right <= resetBounds.Left);
            Assert.InRange(Math.Abs(customBounds.Center.Y - resetBounds.Center.Y), 0, 1);
            Assert.InRange(resetBounds.Right, 0, modeRow.Bounds.Width + 1);
            var icon = Assert.IsType<MaterialIcon>(reset.Content);
            Assert.Equal(MaterialIconKind.Restore, icon.Kind);
            Assert.Equal(Localization.Get("Workbench.ResetAutomaticPositionHint"), ToolTip.GetTip(reset));
            Assert.Equal(Localization.Get("Workbench.ResetAutomaticPositionHint"), AutomationProperties.GetName(reset));
        }
        var diagram = UiTestActions.Find<SubtitlePositionDiagram>(position, "PositionDiagram");
        Assert.True(diagram.IsEffectivelyVisible);
        Assert.NotNull(diagram.Geometry);
        Assert.NotNull(diagram.Position);
        Assert.Equal(margins.Draft!.CreateMargins(), diagram.Margins);
        Assert.InRange(BoundsIn(diagram, group).Right, 0, group.Bounds.Width + 1);
        Assert.True(diagram.Bounds.Height >= 96);
    }

    private static void AssertModeLabel(RadioButton mode, string key, string tooltipKey)
    {
        var expected = Localization.Get(key);
        Assert.Equal(expected, mode.Content);
        Assert.Equal(Localization.Get(tooltipKey), ToolTip.GetTip(mode));
        Assert.Equal(expected, AutomationProperties.GetName(mode));
        var label = Assert.Single(mode.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == expected);
        Assert.True(label.DesiredSize.Width <= label.Bounds.Width + 1);
        var bounds = BoundsIn(label, mode);
        Assert.InRange(bounds.Left, -1, mode.Bounds.Width);
        Assert.InRange(bounds.Right, 0, mode.Bounds.Width + 1);
        Assert.InRange(bounds.Top, -1, mode.Bounds.Height);
        Assert.InRange(bounds.Bottom, 0, mode.Bounds.Height + 1);
    }

    private static void AssertFieldOrder(Control group, string[] names)
    {
        var actual = group.GetVisualDescendants().OfType<Control>()
            .Where(control => names.Contains(control.Name)).Select(control => control.Name).ToArray();
        Assert.Equal(names, actual);
    }

    private static void AssertStacked(Visual group, params Control[] fields)
    {
        for (var index = 1; index < fields.Length; index++)
        {
            Assert.True(BoundsIn(fields[index - 1], group).Bottom <= BoundsIn(fields[index], group).Top);
        }
    }

    private static IEnumerable<Control> Controls(Control root)
    {
        return root.GetLogicalDescendants().OfType<Control>()
            .Concat(root.GetVisualDescendants().OfType<Control>()).Distinct();
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

    private static void Capture(Window window, Control focus, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_MARGIN_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        focus.BringIntoView();
        Flush(window);
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
