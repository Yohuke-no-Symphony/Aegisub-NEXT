using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectPropertyDropdownUiTests
{
    [AvaloniaTheory]
    [InlineData(320, "en-US", false)]
    [InlineData(320, "zh-CN", true)]
    [InlineData(480, "en-US", true)]
    [InlineData(480, "zh-CN", false)]
    public async Task OpeningFromNumericFocusAndRefreshingKeepsPopupItemsAndGeometryStable(double width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var layerId = context.Session.SelectedLayerId!.Value;
        context.Session.Editor.SetClipMask(layerId, new RectangleClipMask { BottomRight = new(400, 300) });
        var target = new AnimationTrackTarget(AnimationProperty.OPACITY);
        context.Session.Editor.SetKeyframe(layerId, target, new(new(0), 0.5));
        Assert.True(context.Session.SelectKeyframe(new(layerId, target, new(0), new(0))));
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        using var view = new EffectsPanelView(context.ViewModel.Effects, context.Session);
        var window = new Window { Width = width, Height = 900, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Flush(window);
            UiTestActions.ExpandEffectsCategory(window, "CompositeCategory");
            var input = UiTestActions.Find<NumericDraftInput>(view, "OpacityInput");
            input.BringIntoView();
            Flush(window);
            Assert.True(input.GetVisualDescendants().OfType<TextBox>().Single().Focus());
            var button = view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("animation-options") && Equals(button.Tag, AnimationProperty.OPACITY));
            UiTestActions.Click(window, button);
            Assert.True(button.Flyout!.IsOpen);
            var options = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(button.Flyout).Content);
            var choice = UiTestActions.Find<ComboBox>(options, "InterpolationCombo");
            Flush(window);
            var items = choice.ItemsSource;
            var selection = choice.SelectedItem;
            var root = TopLevel.GetTopLevel(choice)!;
            var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), root)!.Value;
            root.MouseDown(point, MouseButton.Left);
            root.MouseUp(point, MouseButton.Left);
            Flush(window);
            Assert.True(choice.IsDropDownOpen);
            Assert.Same(items, choice.ItemsSource);
            Assert.Same(selection, choice.SelectedItem);
            var popup = UiTestActions.Find<Popup>(choice, "PART_Popup");
            var presenter = Assert.IsAssignableFrom<Control>(popup.Child);
            var container = choice.ContainerFromIndex(choice.SelectedIndex);
            Assert.NotNull(container);
            var bounds = presenter.Bounds;
            var detached = 0;
            presenter.DetachedFromVisualTree += (_, _) => detached++;
            var sourceChanges = 0;
            choice.PropertyChanged += (_, e) => sourceChanges += e.Property == ItemsControl.ItemsSourceProperty ? 1 : 0;
            for (var index = 0; index < 10; index++)
            {
                context.Session.MaskEditing.Refresh();
                context.Session.Tick();
                Flush(window);
                Assert.True(choice.IsDropDownOpen);
                Assert.Same(items, choice.ItemsSource);
                Assert.Same(selection, choice.SelectedItem);
                Assert.Same(container, choice.ContainerFromIndex(choice.SelectedIndex));
                Assert.Equal(bounds, presenter.Bounds);
            }
            Assert.Equal(0, sourceChanges);
            Assert.Equal(0, detached);
            Assert.True(choice.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(window);
            Assert.False(choice.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RealMaskChangesRefreshRowsAndPreserveCompleteNodeTargets()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var effects = context.ViewModel.Effects;
        var id = session.SelectedLayerId!.Value;
        Assert.False(effects.HasEditableMask);
        Assert.Empty(effects.MaskRows);
        session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(400, 300) });
        Dispatcher.UIThread.RunJobs();
        Assert.True(effects.HasEditableMask);
        Assert.Contains(effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var first = new MaskNode { Position = new(10, 10) };
        var second = new MaskNode { Position = new(100, 100) };
        session.Editor.SetClipMask(id, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        session.MaskEditing.SelectNode(first.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var firstRow = Assert.Single(effects.MaskRows, row => row.Target == target);
        var rows = effects.MaskRows;
        var firstButton = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        Assert.Equal(target, effects.Target);
        session.MaskEditing.Refresh();
        Assert.Same(rows, effects.MaskRows);
        session.MaskEditing.SelectNode(second.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.False(firstButton.Flyout!.IsOpen);
        Assert.Equal(new(AnimationProperty.MASK_NODE_POSITION, second.Id), effects.Target);
        var secondRow = Assert.Single(effects.MaskRows, row => row.Target == effects.Target);
        Assert.NotEqual(firstRow.FieldKey, secondRow.FieldKey);
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            session.UpdatePreferences(session.Preferences with { Language = language });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.MASK_NODE_POSITION"), secondRow.Name);
            rows = effects.MaskRows;
            session.MaskEditing.Refresh();
            Assert.Same(rows, effects.MaskRows);
        }
        session.MaskEditing.Clear();
        Dispatcher.UIThread.RunJobs();
        Assert.False(effects.HasEditableMask);
        Assert.Empty(effects.MaskRows);
        Assert.Equal(AnimationProperty.OPACITY, effects.Target.Property);
        Assert.True(session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.True(effects.HasEditableMask);
        Assert.Contains(effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_POSITION);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
