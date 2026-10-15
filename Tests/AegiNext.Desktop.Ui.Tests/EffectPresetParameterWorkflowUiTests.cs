using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.EffectPresets;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectPresetParameterWorkflowUiTests
{
    [AvaloniaFact]
    public async Task PresetDefaultsRemainUnchangedAfterEditingOneInstanceAndAddingAnotherWithIndependentUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = await UiTestActions.CreateSubtitleAsync(context, new(4));
        await context.Session.EffectScripts.Completion;
        var before = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(before);
        context.Session.SelectCue(cueId);
        UiTestActions.SelectBuiltinPreset(context.Window, "fade-in");
        var list = UiTestActions.Find<ListBox>(context.Window, "EffectPresetList");
        var presets = Assert.Single(list.GetVisualAncestors().OfType<EffectPresetsPanelView>());
        Assert.Empty(presets.GetVisualDescendants().OfType<EffectScriptParameterEditor>());
        var card = context.ViewModel.EffectPresets.SelectedPreset!;
        var source = card.Source;
        Assert.Equal(new MediaTime(3, 10), card.DefaultParameters["duration"].Duration);

        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await context.ViewModel.EffectPresets.ApplyCommand.ExecutionTask!;
        Assert.Null(context.Session.LastError);
        var firstAdded = context.Session.DocumentSnapshot;
        var layer = firstAdded.Layers[0];
        var first = Assert.Single(layer.Effects);
        Assert.Equal(new MediaTime(3, 10), first.Parameters["duration"].Duration);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Assert.True(context.ViewModel.Timeline.SelectEffectClip(new(new(layer.Id, new(AnimationProperty.OPACITY), first.Id))));
        Flush(context.Window);
        var editor = UiTestActions.Find<EffectScriptParameterEditor>(context.Window, "InstanceParameterEditor");
        var duration = editor.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "EffectParameter_duration");
        duration.BringIntoView();
        Flush(context.Window);
        Assert.True(duration.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("0.6s");
        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);
        var firstEdited = context.Session.DocumentSnapshot;
        Assert.Equal(new MediaTime(3, 5), Assert.Single(firstEdited.Layers[0].Effects).Parameters["duration"].Duration);
        Assert.False(context.Session.HasProjectDrafts);

        UiTestActions.SelectBuiltinPreset(context.Window, "fade-in");
        Assert.Empty(presets.GetVisualDescendants().OfType<EffectScriptParameterEditor>());
        Assert.Equal(source, context.ViewModel.EffectPresets.SelectedPreset!.Source);
        Assert.Equal(new MediaTime(3, 10), context.ViewModel.EffectPresets.SelectedPreset.DefaultParameters["duration"].Duration);
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await context.ViewModel.EffectPresets.ApplyCommand.ExecutionTask!;
        Assert.Null(context.Session.LastError);
        var effects = context.Session.DocumentSnapshot.Layers[0].Effects;
        Assert.Equal(2, effects.Length);
        Assert.Equal(first.Id, effects[0].Id);
        Assert.NotEqual(effects[0].Id, effects[1].Id);
        Assert.Equal(new MediaTime(3, 5), effects[0].Parameters["duration"].Duration);
        Assert.Equal(new MediaTime(3, 10), effects[1].Parameters["duration"].Duration);
        Assert.All(effects, instance => Assert.Equal(source, instance.Source));
        Assert.Empty(context.Session.DocumentSnapshot.Layers[0].Tracks);

        Assert.True(context.Session.Editor.Undo());
        Assert.Same(firstEdited, context.Session.DocumentSnapshot);
        Assert.Equal(new MediaTime(3, 5), Assert.Single(context.Session.DocumentSnapshot.Layers[0].Effects).Parameters["duration"].Duration);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(firstAdded, context.Session.DocumentSnapshot);
        Assert.Equal(new MediaTime(3, 10), Assert.Single(context.Session.DocumentSnapshot.Layers[0].Effects).Parameters["duration"].Duration);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Empty(context.Session.DocumentSnapshot.Layers[0].Effects);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(source, card.Source);
        Assert.Equal(new MediaTime(3, 10), card.DefaultParameters["duration"].Duration);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
