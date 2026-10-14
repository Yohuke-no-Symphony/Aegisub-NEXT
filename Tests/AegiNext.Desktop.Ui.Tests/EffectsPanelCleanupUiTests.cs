using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsPanelCleanupUiTests
{
    [AvaloniaFact]
    public async Task TimelineTimeInputsHaveLocalizedLabelsAndEffectEditingKeepsTheExistingLayerName()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var panel = context.Window.Panels[WorkbenchPanelIds.EFFECTS];
        Assert.Null(panel.FindControl<TextBox>("LayerNameInput"));
        Assert.Null(panel.FindControl<TextBox>("LayerStartInput"));
        Assert.Null(panel.FindControl<TextBox>("LayerEndInput"));
        Assert.Null(panel.FindControl<Expander>("ClipCategory"));
        var originalName = context.Session.SelectedLayer!.Name;
        context.ViewModel.Effects.RotationText = "15";
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal(originalName, context.Session.SelectedLayer!.Name);
        Assert.Equal(15, context.Session.SelectedLayer.Transform.Rotation);
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        var timeline = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = culture });
            context.Window.UpdateLayout();
            var start = timeline.FindControl<TextBlock>("TimelineClipStartLabel")!;
            var end = timeline.FindControl<TextBlock>("TimelineClipEndLabel")!;
            Assert.Equal(Localization.Get("Workbench.Start"), start.Text);
            Assert.Equal(Localization.Get("Workbench.End"), end.Text);
            Assert.True(start.Bounds.Height > 0);
            Assert.True(end.Bounds.Height > 0);
        }
    }

    [AvaloniaFact]
    public async Task EmptyEditingMessagesDoNotReserveRowsAndValidationRemainsVisible()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var panel = context.Window.Panels[WorkbenchPanelIds.EFFECTS];
        context.ViewModel.Effects.EditTargetLabel = string.Empty;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        Assert.False(panel.FindControl<TextBlock>("EditingTarget")!.IsVisible);
        Assert.False(panel.FindControl<TextBlock>("ValidationMessage")!.IsVisible);
        context.ViewModel.Effects.BlurText = "unfinished";
        Assert.False(context.Session.TryCommitDrafts(false));
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var validation = panel.FindControl<TextBlock>("ValidationMessage")!;
        Assert.True(validation.IsVisible);
        Assert.False(string.IsNullOrEmpty(validation.Text));
        context.ViewModel.Effects.RestoreField("BlurInput");
        Dispatcher.UIThread.RunJobs();
        Assert.False(validation.IsVisible);
    }
}
