using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AegiNext.Desktop.Views;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

internal static class UiTestActions
{
    internal static async Task<Guid> CreateSubtitleAsync(MainWindowTestContext context, MediaTime? duration = null, string text = "Subtitle ABC 中文 123")
    {
        var before = context.Session.DocumentSnapshot.Subtitles.Select(line => line.Id).ToHashSet();
        Click(context.Window, "AddCueButton");
        await Assert.IsType<AsyncRelayCommand>(context.Window.GetCommand(WorkbenchCommand.ADD_SUBTITLE)).ExecutionTask!;
        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => !before.Contains(line.Id));
        context.Session.Editor.SetSubtitleTiming(cue.Id, cue.Start, cue.Start + (duration ?? new MediaTime(5)), TimelineEditMode.CROP);
        context.Session.Editor.UpdateSubtitle(cue.Id, line => line with { Text = text });
        context.Session.SelectCue(cue.Id);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return cue.Id;
    }

    internal static T Find<T>(Control root, string name) where T : Control
    {
        if (root is Window host)
        {
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
        }
        var popupRoots = root is MainWindow mainWindow ? new[] { root }.Concat(mainWindow.Panels.Values) : [root];
        var popupControl = popupRoots.SelectMany(control => control.GetVisualDescendants().Concat(control.GetLogicalDescendants().OfType<Visual>())).OfType<Button>()
            .Select(button => button.Flyout).OfType<Flyout>().Where(flyout => flyout.IsOpen).Distinct()
            .Select(flyout => flyout.Content).OfType<Control>()
            .SelectMany(control => control.GetVisualDescendants().Concat(control.GetLogicalDescendants().OfType<Visual>()))
            .OfType<T>().FirstOrDefault(control => control.Name == name);
        if (popupControl is not null)
        {
            return popupControl;
        }
        var control = (root.Name == name ? root as T : null) ?? root.GetLogicalDescendants().OfType<T>().FirstOrDefault(control => control.Name == name)
            ?? root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
        if (control is not null && (root is not MainWindow || control.IsAttachedToVisualTree()))
        {
            return control;
        }
        if (root is MainWindow window)
        {
            foreach (var pair in window.Panels)
            {
                control = pair.Value.GetLogicalDescendants().OfType<T>().FirstOrDefault(candidate => candidate.Name == name)
                    ?? pair.Value.GetVisualDescendants().OfType<T>().FirstOrDefault(candidate => candidate.Name == name);
                if (control is not null)
                {
                    window.Layouts.Activate(pair.Key);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    return control;
                }
            }
        }
        throw new InvalidOperationException($"Missing control {name}.");
    }

    internal static void SelectBuiltinPreset(Window window, string scriptId)
    {
        var index = BuiltinEffectScripts.Templates.Select((template, index) => (template, index))
            .Single(value => value.template.Script.Id == scriptId).index;
        Find<ComboBox>(window, "PresetCombo").SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectLanguage(Window window, string languageID)
    {
        var selector = Find<ComboBox>(window, "LanguageCombo");
        selector.SelectedItem = selector.Items.OfType<LanguageInfo>()
            .Single(language => string.Equals(language.LanguageID, languageID, StringComparison.OrdinalIgnoreCase));
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectSettingsPage(Window window, SettingsPage page)
    {
        var navigation = Find<ListBox>(window, "Navigation");
        var item = navigation.Items.Cast<ListBoxItem>().Single(value => Equals(value.DataContext, page));
        item.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var point = item.TranslatePoint(new(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectShortcut(Window window, WorkbenchCommand command)
    {
        var list = Find<ListBox>(window, "ShortcutList");
        list.SelectedItem = list.Items.OfType<ShortcutSettingsListItem>().Single(item => item.Row?.Command == command);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectAnimationProperty(Window window, AnimationProperty property)
    {
        OpenAnimationOptions(Assert.IsType<MainWindow>(window), property);
    }

    internal static Button OpenAnimationOptions(MainWindow window, AnimationProperty property, string panelId = "effects")
    {
        var button = FindAnimationOptionsButton(window, property, panelId);
        if (button.Flyout?.IsOpen == true)
        {
            return button;
        }
        HideAnimationOptions(window);
        Click(window, button);
        Assert.True(button.Flyout?.IsOpen);
        return button;
    }

    private static Button FindAnimationOptionsButton(MainWindow window, AnimationProperty property, string panelId = "effects")
    {
        property = RotationTargetCompatibility.Canonicalize(new(property)).Property;
        window.Layouts.Activate(panelId);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var panel = window.Panels[panelId];
        if (panelId == "effects")
        {
            var category = property switch
            {
                AnimationProperty.POSITION or AnimationProperty.SCALE or AnimationProperty.ROTATION_3D or AnimationProperty.PERSPECTIVE_DISTANCE => "TransformCategory",
                AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING => "TypographyCategory",
                AnimationProperty.FILL or AnimationProperty.FILL_BLUR => "FillCategory",
                AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or AnimationProperty.STROKE_BLUR => "StrokeCategory",
                AnimationProperty.SHADOW_COLOR or AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR => "ShadowCategory",
                AnimationProperty.OPACITY or AnimationProperty.BLUR => "CompositeCategory",
                _ => "MaskCategory"
            };
            ExpandEffectsCategory(window, category);
        }
        var row = panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>().FirstOrDefault(control =>
            control.DataContext switch
            {
                AnimationPropertyRowViewModel model => model.Target.Property == property,
                MaskNumericField field => field.Row?.Target.Property == property,
                AegiNext.Desktop.Panels.Masks.MaskVectorField field => field.Row?.Target.Property == property,
                _ => false
            });
        return row is not null ? row.FindControl<Button>("AnimationOptionsButton")! :
            panel.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("animation-options") && Equals(button.Tag, property));
    }

    private static void HideAnimationOptions(MainWindow window)
    {
        foreach (var current in window.Panels.Values.SelectMany(panel => panel.GetVisualDescendants()).OfType<Button>()
            .Select(button => button.Flyout).OfType<Flyout>().Where(flyout => flyout.IsOpen).Distinct())
        {
            current.Hide();
        }
    }

    internal static NumericDraftInput PropertyInput(MainWindow window, AnimationProperty property, int component = 0)
    {
        property = RotationTargetCompatibility.Canonicalize(new(property)).Property;
        var anchor = FindAnimationOptionsButton(window, property);
        HideAnimationOptions(window);
        var row = anchor.GetVisualAncestors().OfType<AnimationPropertyRowControl>().FirstOrDefault();
        if (row is not null)
        {
            if (row.IsScalar)
            {
                return row.FindControl<NumericDraftInput>("ScalarInput")!;
            }
            var key = component == 0 ? row.XFieldKey : component == 1 ? row.YFieldKey : row.ZFieldKey;
            return row.FindControl<VectorDraftInput>("VectorInput")!.FindFieldInput(key)!;
        }
        var name = property switch
        {
            AnimationProperty.POSITION => "PositionInput",
            AnimationProperty.SCALE => "ScaleInput",
            AnimationProperty.OPACITY => "OpacityInput",
            AnimationProperty.BLUR => "BlurInput",
            _ => throw new InvalidOperationException($"No property input for {property}.")
        };
        if (property is AnimationProperty.POSITION or AnimationProperty.SCALE)
        {
            var key = (property == AnimationProperty.POSITION ? "Position" : "Scale") + (component == 0 ? "XInput" : "YInput");
            return Find<VectorDraftInput>(window, name).FindFieldInput(key)!;
        }
        return Find<NumericDraftInput>(window, name);
    }

    internal static Button PropertyKeyframeButton(MainWindow window, AnimationProperty property)
    {
        var anchor = FindAnimationOptionsButton(window, property);
        HideAnimationOptions(window);
        var actions = anchor.GetVisualParent()!;
        return actions.GetVisualDescendants().OfType<Button>().Single(button =>
            button.Command is not null && (button.CommandParameter is AnimationProperty || button.Name != "AnimationOptionsButton") &&
            button.GetVisualDescendants().OfType<AegiNext.Desktop.Controls.Common.IconText>().Any(icon => icon.IconKey == "Keyframe"));
    }

    internal static void ExpandEffectsCategory(Window window, string name)
    {
        var category = Find<Expander>(window, name);
        if (category.IsExpanded)
        {
            return;
        }
        var header = category.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "PART_HeaderSite");
        header.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        Assert.True(window.InputHitTest(point) is Visual visual && (ReferenceEquals(visual, header) || visual.GetVisualAncestors().Contains(header)),
            $"Category={name}; Bounds={header.Bounds}; Point={point}; Hit={window.InputHitTest(point)?.GetType().Name}");
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pressedFrame = window.CaptureRenderedFrame();
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.True(category.IsExpanded);
    }

    internal static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name);
        Click(window, button);
    }

    internal static void Click(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var root = TopLevel.GetTopLevel(button)!;
        root.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException($"Button {button.Name} is not attached to a root.");
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    internal static void ClickFontMenuItem(MenuItem item)
    {
        item.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
        root.UpdateLayout();
        var point = item.TranslatePoint(new(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SetText(TextBox input, string text)
    {
        input.Text = text;
        Dispatcher.UIThread.RunJobs();
    }
}
