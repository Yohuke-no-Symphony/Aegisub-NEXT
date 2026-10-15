using AegiNext.Desktop.Layouts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsPanelCleanupUiTests
{
    [AvaloniaFact]
    public async Task TimelineHasNoBoundaryInputsAndEffectEditingKeepsTheExistingLayerNameAcrossLanguageChanges()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var panel = context.Window.Panels[WorkbenchPanelIds.EFFECTS];
        Assert.Null(panel.FindControl<TextBox>("LayerNameInput"));
        Assert.Null(panel.FindControl<TextBox>("LayerStartInput"));
        Assert.Null(panel.FindControl<TextBox>("LayerEndInput"));
        Assert.Null(panel.FindControl<Expander>("ClipCategory"));
        const string ORIGINAL_NAME = "Existing 中文 layer";
        context.Session.Editor.UpdateLayer(context.Session.SelectedLayer!.Id, layer => layer with { Name = ORIGINAL_NAME });
        context.ViewModel.Effects.RotationText = "15";
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal(ORIGINAL_NAME, context.Session.SelectedLayer!.Name);
        Assert.Equal(15, context.Session.SelectedLayer.Transform.Rotation);
        var edited = context.Session.DocumentSnapshot;
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        var timeline = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = culture });
            Dispatcher.UIThread.RunJobs();
            context.Window.UpdateLayout();
            Assert.Null(timeline.FindControl<Control>("TimelineClipTimingSection"));
            Assert.Null(timeline.FindControl<TextBox>("TimelineClipStartInput"));
            Assert.Null(timeline.FindControl<TextBox>("TimelineClipEndInput"));
            Assert.Null(timeline.FindControl<TextBlock>("TimelineClipStartLabel"));
            Assert.Null(timeline.FindControl<TextBlock>("TimelineClipEndLabel"));
            Assert.Same(edited, context.Session.DocumentSnapshot);
            Assert.Equal(ORIGINAL_NAME, context.Session.SelectedLayer!.Name);
            Assert.Equal(15, context.Session.SelectedLayer.Transform.Rotation);
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
