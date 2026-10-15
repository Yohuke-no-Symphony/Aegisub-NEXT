using AegiNext.Application;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsInspectorLayoutUiTests
{
    [AvaloniaFact]
    public async Task InstanceSelectorOnlyAppearsForAnInstanceAndClickingItsParentRestoresManualProperties()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = await UiTestActions.CreateSubtitleAsync(context, new(4));
        var layerId = context.Session.SelectedLayer!.Id;
        var document = ProjectEditingOperations.AddEffectInstances(context.Session.DocumentSnapshot, [layerId],
            "Fade instance", BuiltinEffectScripts.Get("fade-in").Source);
        context.Session.Editor.Reset(document);
        context.Session.SelectCue(cueId);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Flush(context.Window);
        var selector = UiTestActions.Find<ComboBox>(context.Window, "EffectInstancesCombo");
        var selectorRow = UiTestActions.Find<PropertyRowControl>(context.Window, "EffectInstancesRow");
        var manual = UiTestActions.Find<StackPanel>(context.Window, "ManualEffectProperties");
        var inspector = UiTestActions.Find<StackPanel>(context.Window, "EffectInstanceInspector");
        Assert.False(selectorRow.IsEffectivelyVisible);
        Assert.True(manual.IsEffectivelyVisible);
        var instance = Assert.Single(document.Layers.Single(layer => layer.Id == layerId).Effects);

        Assert.True(context.ViewModel.Timeline.SelectEffectClip(new(new(layerId, new(AnimationProperty.OPACITY), instance.Id))));
        Flush(context.Window);

        Assert.True(selectorRow.IsEffectivelyVisible);
        Assert.True(inspector.IsEffectivelyVisible);
        Assert.False(manual.IsEffectivelyVisible);
        Assert.Equal(instance.Id, Assert.IsType<EffectInstance>(selector.SelectedItem).Id);
        Assert.DoesNotContain(inspector.GetVisualDescendants().OfType<Button>(), button => button.Name is
            "ReturnToParentEffectsButton" or "MoveEffectInstanceUpButton" or "MoveEffectInstanceDownButton");
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var parent = timeline.GetClipRectangle(layerId)!.Value;
        var point = timeline.TranslatePoint(parent.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);

        Assert.False(selectorRow.IsEffectivelyVisible);
        Assert.False(inspector.IsEffectivelyVisible);
        Assert.True(manual.IsEffectivelyVisible);
        Assert.Null(context.ViewModel.Effects.SelectedEffectInstance);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(260, "en-US", false)]
    [InlineData(260, "zh-CN", true)]
    [InlineData(650, "en-US", true)]
    [InlineData(650, "zh-CN", false)]
    public async Task RangeActionsKeepRestorePositionOnTheRightAndShareHeightAndVerticalCenter(int width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        using var view = new EffectsPanelView(context.ViewModel.Effects, context.Session);
        var host = new Window { Width = width, Height = 800, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            host.Show();
            Flush(host);
            var actions = UiTestActions.Find<Grid>(view, "RangeActions");
            var create = UiTestActions.Find<Button>(view, "CreateAnimationRangeButton");
            var delete = UiTestActions.Find<Button>(view, "DeleteAnimationRangeButton");
            var reset = UiTestActions.Find<Button>(view, "ResetPositionButton");
            var buttons = actions.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(new[] { create, delete, reset }, buttons);
            var createBounds = BoundsIn(create, actions);
            var deleteBounds = BoundsIn(delete, actions);
            var resetBounds = BoundsIn(reset, actions);
            Assert.True(create.IsEffectivelyVisible);
            Assert.True(delete.IsEffectivelyVisible);
            Assert.True(reset.IsEffectivelyVisible);
            Assert.Equal(createBounds.Height, deleteBounds.Height, 3);
            Assert.Equal(createBounds.Height, resetBounds.Height, 3);
            Assert.Equal(createBounds.Center.Y, deleteBounds.Center.Y, 3);
            Assert.Equal(createBounds.Center.Y, resetBounds.Center.Y, 3);
            Assert.True(createBounds.Right <= deleteBounds.Left);
            Assert.True(deleteBounds.Right <= resetBounds.Left);
            Assert.Equal(actions.Bounds.Width, resetBounds.Right, 3);
            Assert.True(createBounds.Width > 0 && resetBounds.Width > 0);
            Assert.True(actions.Bounds.Width <= view.Bounds.Width);
            Assert.DoesNotContain(reset.GetVisualAncestors(), control => control is PropertySection);
        }
        finally
        {
            host.Close();
        }
    }

    private static Rect BoundsIn(Control control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
