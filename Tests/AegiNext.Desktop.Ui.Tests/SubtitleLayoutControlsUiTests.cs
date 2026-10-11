using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleLayoutControlsUiTests
{
    [AvaloniaFact]
    public void SharedPositionEditorPrefersDraftAndKeepsTheHostContextWithLegacyFallback()
    {
        using var environment = new UiTestEnvironment();
        var legacy = CreateDraft(0.25);
        var explicitDraft = CreateDraft(0.75);
        var editor = new LayerPositionEditor
        {
            DataContext = legacy, Draft = explicitDraft, ShowMargins = true, Margins = new(17, 39, 13),
            Alignment = 1, CanvasWidth = 640, CanvasHeight = 360
        };
        var window = new Window { Width = 460, Height = 620, Content = editor };
        var toggles = 0;
        editor.ExplicitPositionChanged += (_, _) => toggles++;
        try
        {
            Show(window);
            var diagram = editor.FindControl<LayerPositionDiagram>("PositionDiagram")!;
            Assert.Same(legacy, editor.DataContext);
            Assert.Equal(explicitDraft.DiagramPosition, diagram.Position);
            Assert.Same(explicitDraft.Geometry, diagram.Geometry);
            Assert.True(diagram.ShowMargins);
            Assert.Equal(new SubtitleMargins(17, 39, 13), diagram.Margins);
            Assert.Equal(640, diagram.CanvasWidth);
            Assert.Equal(360, diagram.CanvasHeight);
            Assert.Equal(1, diagram.Alignment);
            var hostContext = new object();
            editor.DataContext = hostContext;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(hostContext, editor.DataContext);
            Assert.Equal(explicitDraft.DiagramPosition, diagram.Position);
            editor.DataContext = legacy;
            editor.Draft = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(legacy.DiagramPosition, diagram.Position);
            Assert.Same(legacy.Geometry, diagram.Geometry);
            Assert.Equal(0, toggles);
            UiTestActions.Click(window, "AutomaticPositionMode");
            Assert.False(legacy.IsExplicit);
            Assert.True(explicitDraft.IsExplicit);
            Assert.Equal(1, toggles);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SharedLayoutControlsKeepHintsInLiveTooltipsAndNamesRemainConcise()
    {
        using var environment = new UiTestEnvironment();
        var position = CreateDraft(0.5);
        var margins = new SubtitleMarginsDraft();
        margins.Load(new(17, 39, 13));
        var marginEditor = new SubtitleMarginsEditor { Draft = margins, ShowDiagram = false, Alignment = 4 };
        var positionEditor = new LayerPositionEditor { Draft = position, ShowMargins = true, Margins = margins.DiagramMargins };
        var window = new Window
        {
            Width = 460, Height = 780, Content = new StackPanel { Children = { marginEditor, positionEditor } }
        };
        try
        {
            Show(window);
            margins.Left.RawText = "7e-";
            position.OffsetX.RawText = "7e-";
            foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
            {
                Localization.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                var vertical = marginEditor.FindControl<NumericDraftInput>("MarginVerticalInput")!;
                Assert.Equal(Localization.Get("Workbench.MarginVertical") + Environment.NewLine +
                             Localization.Get("Workbench.SubtitleMarginsMiddleHint"), ToolTip.GetTip(vertical));
                Assert.Equal(Localization.Get("Workbench.SubtitlePositionHint"),
                    ToolTip.GetTip(positionEditor.FindControl<RadioButton>("CustomPositionMode")!));
                foreach (var name in new[]
                         {
                             "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
                             "BottomLeft", "BottomCenter", "BottomRight"
                         })
                {
                    var button = positionEditor.FindControl<AnchorPresetPicker>("AnchorPresets")!
                        .GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "AnchorPreset" + name);
                    var label = Localization.Get("Workbench.AnchorPreset") + " · " + Localization.Get("Workbench." + name);
                    Assert.Equal(label + Environment.NewLine + Localization.Get("Workbench.AnchorPresetHint"), ToolTip.GetTip(button));
                    Assert.Equal(label, AutomationProperties.GetName(button));
                }
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                    text.Text == Localization.Get("Workbench.SubtitlePositionHint") ||
                    text.Text == Localization.Get("Workbench.AnchorPresetHint") ||
                    text.Text == Localization.Get("Workbench.SubtitleMarginsDiagramHint") ||
                    text.Text == Localization.Get("Workbench.SubtitleMarginsMiddleHint") ||
                    text.Text == Localization.Get("Workbench.SubtitleMarginsExplicitHint"));
                Assert.Equal("7e-", margins.Left.RawText);
                Assert.Equal("7e-", position.OffsetX.RawText);
            }
            marginEditor.IsExplicit = true;
            var verticalInput = marginEditor.FindControl<NumericDraftInput>("MarginVerticalInput")!;
            Assert.False(verticalInput.IsEffectivelyEnabled);
            Assert.Equal("13", margins.Vertical.RawText);
            Assert.Contains(Localization.Get("Workbench.SubtitleMarginsExplicitHint"), Assert.IsType<string>(ToolTip.GetTip(verticalInput)));
            marginEditor.IsExplicit = false;
            marginEditor.Alignment = 7;
            Assert.True(verticalInput.IsEffectivelyEnabled);
            Assert.Contains(Localization.Get("Workbench.SubtitleMarginsDiagramHint"), Assert.IsType<string>(ToolTip.GetTip(verticalInput)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReattachingThePositionEditorRefreshesTheCurrentDraftSelection()
    {
        using var environment = new UiTestEnvironment();
        var original = CreateDraft(0.25);
        var replacement = CreateDraft(0.75);
        var editor = new LayerPositionEditor { Draft = original };
        var window = new Window { Width = 460, Height = 620, Content = editor };
        try
        {
            Show(window);
            window.Content = null;
            editor.Draft = replacement;
            original.AnchorX.RawText = "0";
            replacement.AnchorX.RawText = "1";
            window.Content = editor;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(replacement.DiagramPosition, editor.FindControl<LayerPositionDiagram>("PositionDiagram")!.Position);
            Assert.Equal(1, replacement.AnchorSelection!.Value.X);
            Assert.Equal("1", editor.FindControl<VectorDraftInput>("AnchorInput")!.XText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReplacingDraftsAcrossModesNeverEditsEitherDraft(bool originalExplicit, bool replacementExplicit)
    {
        using var environment = new UiTestEnvironment();
        var original = CreateDraft(0.25, originalExplicit);
        var replacement = CreateDraft(0.75, replacementExplicit);
        var host = new object();
        var editor = new LayerPositionEditor { DataContext = host, Draft = original };
        var window = new Window { Width = 320, Height = 620, Content = editor };
        var originalChanges = 0;
        var replacementChanges = 0;
        var semanticChanges = 0;
        original.Changed += (_, _) => originalChanges++;
        replacement.Changed += (_, _) => replacementChanges++;
        editor.ExplicitPositionChanged += (_, _) => semanticChanges++;
        try
        {
            Show(window);
            editor.Draft = replacement;
            Flush(window);
            AssertMode(editor, replacementExplicit);
            Assert.Same(host, editor.DataContext);
            Assert.Equal(originalExplicit, original.IsExplicit);
            Assert.Equal(replacementExplicit, replacement.IsExplicit);
            editor.Draft = null;
            Flush(window);
            Assert.False(editor.FindControl<RadioButton>("AutomaticPositionMode")!.IsChecked);
            Assert.False(editor.FindControl<RadioButton>("CustomPositionMode")!.IsChecked);
            editor.DataContext = original;
            Flush(window);
            AssertMode(editor, originalExplicit);
            window.Content = null;
            editor.Draft = replacement;
            window.Content = editor;
            Flush(window);
            AssertMode(editor, replacementExplicit);
            Assert.Equal(originalExplicit, original.IsExplicit);
            Assert.Equal(replacementExplicit, replacement.IsExplicit);
            Assert.Equal(0, originalChanges);
            Assert.Equal(0, replacementChanges);
            Assert.Equal(0, semanticChanges);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DraftLoadsRefreshModesAndAvailabilityWithoutSemanticEvents()
    {
        using var environment = new UiTestEnvironment();
        var draft = CreateDraft(0.5);
        var editor = new LayerPositionEditor { Draft = draft };
        var window = new Window { Width = 320, Height = 620, Content = editor };
        var changes = 0;
        editor.ExplicitPositionChanged += (_, _) => changes++;
        try
        {
            Show(window);
            var geometry = draft.Geometry;
            foreach (var explicitPosition in new[] { false, true, false, true })
            {
                draft.Load(new() { Position = explicitPosition ? new() : null }, geometry: geometry);
                Flush(window);
                AssertMode(editor, explicitPosition);
                Assert.Equal(explicitPosition, editor.FindControl<StackPanel>("PositionFields")!.IsVisible);
                draft.UpdateGeometry(geometry);
                Flush(window);
                Assert.Equal(0, changes);
            }
            draft.Load(new() { Position = new() }, canCustomize: false, geometry: geometry);
            Flush(window);
            Assert.True(editor.FindControl<RadioButton>("AutomaticPositionMode")!.IsEffectivelyEnabled);
            Assert.False(editor.FindControl<RadioButton>("CustomPositionMode")!.IsEffectivelyEnabled);
            draft.Load(new(), geometry: geometry);
            Flush(window);
            AssertMode(editor, false);
            Assert.True(editor.FindControl<RadioButton>("CustomPositionMode")!.IsEffectivelyEnabled);
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ValidationFocusNeverRevealsAutomaticFieldsOrChangesThePositionMode()
    {
        using var environment = new UiTestEnvironment();
        var draft = CreateDraft(0.5, false);
        var editor = new LayerPositionEditor { Draft = draft };
        var window = new Window { Width = 320, Height = 620, Content = editor };
        var changes = 0;
        var draftChanges = 0;
        editor.ExplicitPositionChanged += (_, _) => changes++;
        draft.Changed += (_, _) => draftChanges++;
        try
        {
            Show(window);
            Assert.False(editor.FocusInvalidField("OffsetXInput"));
            AssertMode(editor, false);
            Assert.False(editor.FindControl<StackPanel>("PositionFields")!.IsVisible);
            draft.Load(new() { Position = new() }, canCustomize: false, geometry: draft.Geometry);
            Flush(window);
            Assert.Equal("CustomPositionMode", draft.Validate());
            Assert.True(editor.FocusInvalidField("CustomPositionMode"));
            Assert.True(editor.FindControl<RadioButton>("AutomaticPositionMode")!.IsFocused);
            AssertMode(editor, true);
            Assert.Equal(0, changes);
            Assert.Equal(0, draftChanges);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PointerChangesModeOnceAndKeepsPresetsDiagramAndPositionGeometryInPlace()
    {
        using var environment = new UiTestEnvironment();
        var draft = CreateDraft(0.5, false);
        var editor = new LayerPositionEditor { Draft = draft, ShowMargins = true, Margins = new(17, 39, 13) };
        var window = new Window { Width = 320, Height = 620, Content = editor };
        var changes = 0;
        var resets = 0;
        var draftChanges = 0;
        editor.ExplicitPositionChanged += (_, _) => changes++;
        editor.AutomaticPositionRequested += (_, _) => resets++;
        draft.Changed += (_, _) => draftChanges++;
        try
        {
            Show(window);
            var diagram = editor.FindControl<LayerPositionDiagram>("PositionDiagram")!;
            var presets = editor.FindControl<AnchorPresetPicker>("AnchorPresets")!;
            var diagramBounds = new Rect(diagram.TranslatePoint(default, window)!.Value, diagram.Bounds.Size);
            var presetsBounds = new Rect(presets.TranslatePoint(default, window)!.Value, presets.Bounds.Size);
            var position = diagram.Position;
            var geometry = diagram.Geometry;
            AssertMode(editor, false);
            Assert.False(editor.FindControl<StackPanel>("PositionFields")!.IsVisible);
            UiTestActions.Click(window, "CustomPositionMode");
            Flush(window);
            AssertMode(editor, true);
            Assert.True(editor.FindControl<StackPanel>("PositionFields")!.IsVisible);
            Assert.Equal(1, changes);
            Assert.Equal(1, draftChanges);
            Assert.Equal(position, diagram.Position);
            Assert.Same(geometry, diagram.Geometry);
            Assert.Equal(diagramBounds, new(diagram.TranslatePoint(default, window)!.Value, diagram.Bounds.Size));
            Assert.Equal(presetsBounds, new(presets.TranslatePoint(default, window)!.Value, presets.Bounds.Size));
            UiTestActions.Click(window, "CustomPositionMode");
            Assert.Equal(1, changes);
            Assert.Equal(1, draftChanges);
            UiTestActions.Click(window, "AutomaticPositionMode");
            Flush(window);
            AssertMode(editor, false);
            Assert.Equal(2, changes);
            Assert.Equal(2, draftChanges);
            Assert.Equal(0, resets);
            UiTestActions.Click(window, "AutomaticPositionButton");
            Assert.Equal(1, resets);
            Assert.Equal(2, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RadioKeyboardAndAccessibilityKeepIndependentEditorsExclusive()
    {
        using var environment = new UiTestEnvironment();
        var firstDraft = CreateDraft(0.25, false);
        var secondDraft = CreateDraft(0.75, false);
        var first = new LayerPositionEditor { Draft = firstDraft };
        var second = new LayerPositionEditor { Draft = secondDraft };
        var window = new Window { Width = 320, Height = 960, Content = new StackPanel { Children = { first, second } } };
        var firstChanges = 0;
        var secondChanges = 0;
        first.ExplicitPositionChanged += (_, _) => firstChanges++;
        second.ExplicitPositionChanged += (_, _) => secondChanges++;
        try
        {
            Show(window);
            var automatic = first.FindControl<RadioButton>("AutomaticPositionMode")!;
            var custom = first.FindControl<RadioButton>("CustomPositionMode")!;
            Assert.Equal(AutomationControlType.RadioButton, ControlAutomationPeer.CreatePeerForElement(custom).GetAutomationControlType());
            Assert.Equal(automatic.GroupName, custom.GroupName);
            Assert.NotEqual(automatic.GroupName, second.FindControl<RadioButton>("AutomaticPositionMode")!.GroupName);
            Assert.True(automatic.Focus());
            UiTestActions.Press(window, Key.Right);
            Flush(window);
            Assert.True(custom.IsFocused);
            UiTestActions.Press(window, Key.Space);
            Flush(window);
            AssertMode(first, true);
            AssertMode(second, false);
            Assert.Equal(1, firstChanges);
            Assert.Equal(0, secondChanges);
            UiTestActions.Press(window, Key.Space);
            AssertMode(first, true);
            Assert.Equal(1, firstChanges);
            UiTestActions.Press(window, Key.Left);
            Flush(window);
            Assert.True(automatic.IsFocused);
            UiTestActions.Press(window, Key.Space);
            AssertMode(first, false);
            Assert.Equal(2, firstChanges);
            Assert.Equal(0, secondChanges);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("AnchorXInput")]
    [InlineData("AnchorYInput")]
    [InlineData("PivotXInput")]
    [InlineData("PivotYInput")]
    [InlineData("OffsetXInput")]
    [InlineData("OffsetYInput")]
    public void TextEntryAndTitleDragKeepTheRawDraftAuthoritativeAndOnlyEditTheirComponent(string fieldKey)
    {
        using var environment = new UiTestEnvironment();
        var draft = CreateDraft(0.25);
        var fields = new (string Key, NumericValueDraft Draft)[]
        {
            ("AnchorXInput", draft.AnchorX), ("AnchorYInput", draft.AnchorY),
            ("PivotXInput", draft.PivotX), ("PivotYInput", draft.PivotY),
            ("OffsetXInput", draft.OffsetX), ("OffsetYInput", draft.OffsetY)
        };
        var field = fields.Single(value => value.Key == fieldKey).Draft;
        var otherFields = fields.Where(value => value.Key != fieldKey)
            .Select(value => (value.Draft, value.Draft.RawText, value.Draft.Value)).ToArray();
        var editor = new LayerPositionEditor { Draft = draft };
        var window = new Window { Width = 320, Height = 680, Content = editor };
        var changes = 0;
        editor.ExplicitPositionChanged += (_, _) => changes++;
        try
        {
            Show(window);
            var input = Assert.Single(editor.GetVisualDescendants().OfType<NumericDraftInput>(), value => value.Name == fieldKey);
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            var offset = fieldKey.StartsWith("Offset", StringComparison.Ordinal);
            var typedValue = offset ? 14m : 0.4m;
            var typedText = typedValue.ToString(System.Globalization.CultureInfo.CurrentCulture);
            window.KeyTextInput(typedText);
            Flush(window);
            Assert.Equal(typedText, field.RawText);
            Assert.Equal(typedValue, field.Value);
            var spinner = Assert.Single(input.GetVisualDescendants().OfType<ButtonSpinner>());
            var increase = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), value => value.Name == "PART_IncreaseButton");
            Assert.False(input.ShowButtonSpinner);
            Assert.False(increase.IsEffectivelyVisible);
            var title = Assert.Single(editor.GetVisualDescendants().OfType<NumericDragLabel>(), value => ReferenceEquals(value.Input, input));
            title.BringIntoView();
            Flush(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            var point = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;
            Assert.Same(title, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(8, 0));
            window.MouseUp(point + new Vector(8, 0), MouseButton.Left);
            Flush(window);
            var expectedValue = typedValue + (offset ? 8m : 0.08m);
            Assert.Equal(expectedValue.ToString(System.Globalization.CultureInfo.CurrentCulture), field.RawText);
            Assert.Equal(expectedValue, field.Value);
            Assert.Equal(expectedValue, input.Value);
            foreach (var other in otherFields)
            {
                Assert.Equal(other.RawText, other.Draft.RawText);
                Assert.Equal(other.Value, other.Draft.Value);
            }
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", false, true)]
    [InlineData("en-US", true, false)]
    [InlineData("zh-CN", false, false)]
    [InlineData("zh-CN", true, true)]
    public void ModeRowFitsTheNarrowLayoutWithLiveLabelsAndSquareReset(string language, bool dark, bool resetVisible)
    {
        using var environment = new UiTestEnvironment();
        var editor = new LayerPositionEditor { Draft = CreateDraft(0.5, false), ShowAutomaticPositionAction = resetVisible };
        var window = new Window
        {
            Width = 320, Height = 620, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = editor
        };
        try
        {
            Show(window);
            foreach (var currentLanguage in new[] { language, language == "en-US" ? "zh-CN" : "en-US", language })
            {
                Localization.SetLanguage(currentLanguage);
                Flush(window);
                var row = editor.FindControl<Grid>("PositionModeRow")!;
                Assert.Equal(32, row.Bounds.Height);
                Assert.Equal(Localization.Get("Workbench.PositionMode"), editor.FindControl<TextBlock>("PositionModeLabel")!.Text);
                foreach (var name in new[] { "AutomaticPositionMode", "CustomPositionMode" })
                {
                    var mode = editor.FindControl<SegmentedRadioButton>(name)!;
                    Assert.Equal(32, mode.Bounds.Height);
                    Assert.Equal(Localization.Get("Workbench." + name), mode.Content);
                    Assert.Equal(mode.Content, AutomationProperties.GetName(mode));
                    var presenter = Assert.Single(mode.GetVisualDescendants().OfType<ContentPresenter>());
                    var text = Assert.Single(presenter.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(text.DesiredSize.Width <= presenter.Bounds.Width, $"{currentLanguage}: {name} label is clipped.");
                    var point = mode.TranslatePoint(default, row)!.Value;
                    Assert.InRange(point.X, 0, row.Bounds.Width);
                    Assert.True(point.X + mode.Bounds.Width <= row.Bounds.Width);
                }
                var reset = editor.FindControl<Button>("AutomaticPositionButton")!;
                var automatic = editor.FindControl<SegmentedRadioButton>("AutomaticPositionMode")!;
                var custom = editor.FindControl<SegmentedRadioButton>("CustomPositionMode")!;
                Assert.Equal(automatic.TranslatePoint(default, row)!.Value.X + automatic.Bounds.Width,
                    custom.TranslatePoint(default, row)!.Value.X);
                Assert.Equal(resetVisible, reset.IsVisible);
                Assert.Equal(Localization.Get("Workbench.ResetAutomaticPositionHint"), ToolTip.GetTip(reset));
                Assert.Equal(ToolTip.GetTip(reset), AutomationProperties.GetName(reset));
                Assert.Equal(MaterialIconKind.Restore, Assert.IsType<MaterialIcon>(reset.Content).Kind);
                if (resetVisible)
                {
                    Assert.Equal(new Size(32, 32), reset.Bounds.Size);
                    Assert.True(reset.TranslatePoint(default, row)!.Value.X + reset.Bounds.Width <= row.Bounds.Width);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static SubtitlePositionDraft CreateDraft(double anchor, bool explicitPosition = true)
    {
        var draft = new SubtitlePositionDraft();
        var position = new SubtitlePosition { Anchor = new(anchor, 0.5) };
        draft.Load(new() { Position = explicitPosition ? position : null }, resolved: position,
            geometry: new(new(200, 100), new(20, 30), new(40, 20), new()));
        return draft;
    }

    private static void AssertMode(LayerPositionEditor editor, bool explicitPosition)
    {
        Assert.Equal(!explicitPosition, editor.FindControl<RadioButton>("AutomaticPositionMode")!.IsChecked);
        Assert.Equal(explicitPosition, editor.FindControl<RadioButton>("CustomPositionMode")!.IsChecked);
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
