using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectLibraryIntegrationUiTests
{
    [AvaloniaFact]
    public async Task ScopedPersonalTemplateSavedInSettingsGeneratesPairsFromWorkbenchWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(2), "ABCD");
        context.Session.SelectCue(cueId);
        await context.Session.EffectScripts.Completion;
        var original = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.EFFECTS);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        settings.ViewModel.Effects.Name = "Personal pairs";
        settings.ViewModel.Effects.Source = """
            effect "personal-pairs" version 3
            short-clip compress
            scope pairs current
                unit chunk(2)
                stagger 60ms
                segment pulse fixed 150ms pingpong
                    at 0 scale base ease-in-out
                    at 1 scale factor(1.25, 1.25)
                end
                segment rest flex 1
                end
            end
            """;
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await settings.ViewModel.Effects.SaveCommand.ExecutionTask!;
        await context.Session.EffectScripts.Completion;
        Dispatcher.UIThread.RunJobs();
        var preset = Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Assert.Equal(3, EffectScriptParser.Parse(preset.Source).Version);
        Assert.False(settings.ViewModel.Effects.IsDirty);
        Assert.Same(original, context.Session.DocumentSnapshot);
        settings.Close();

        UiTestActions.SelectEffectPreset(context.Window, preset.Id.ToString("D"));
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(context.ViewModel.EffectPresets.ApplyCommand).ExecutionTask!;

        var applied = context.Session.DocumentSnapshot;
        Assert.Empty(applied.Subtitles[0].AnimationRanges);
        Assert.Empty(applied.Layers[0].Tracks);
        var instance = Assert.Single(applied.Layers[0].Effects);
        var plan = EffectScriptCompiler.CompileInstance(EffectScriptParser.Parse(instance.Source), instance,
            applied.Layers[0], applied.Subtitles[0]);
        Assert.Equal([(0, 2), (2, 2)], plan.Ranges.Select(range => (range.Utf16Start, range.Utf16Length)));
        Assert.All(plan.Channels, channel => Assert.Equal(AnimationProperty.SCALE, channel.Target.Property));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(applied, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task RealSettingsSaveUpdatesPersonalLibraryAndPanelWithoutEditingProjectThenAppliesAtClipLength()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        await context.Session.EffectScripts.Completion;
        var original = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.EFFECTS);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        settings.ViewModel.Effects.Name = "Personal timing script";
        var source = settings.ViewModel.Effects.Source;
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await settings.ViewModel.Effects.SaveCommand.ExecutionTask!;
        await context.Session.EffectScripts.Completion;
        Dispatcher.UIThread.RunJobs();
        var preset = Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Assert.Equal(source, preset.Source);
        Assert.Equal("Personal timing script", preset.Name);
        Assert.False(settings.ViewModel.Effects.IsDirty);
        Assert.Same(original, context.Session.DocumentSnapshot);
        settings.Close();
        UiTestActions.SelectEffectPreset(context.Window, preset.Id.ToString("D"));
        Assert.Equal(preset.Id.ToString("D"), context.ViewModel.EffectPresets.SelectedPreset!.Id);
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(context.ViewModel.EffectPresets.ApplyCommand).ExecutionTask!;
        var layer = context.Session.SelectedLayer!;
        var instance = Assert.Single(layer.Effects);
        Assert.Equal(source, instance.Source);
        Assert.Equal(layer.End - layer.Start + layer.AnimationOffset, instance.End);
        Assert.Empty(layer.Tracks);
        context.Session.Editor.Undo();
        Assert.Same(original, context.Session.DocumentSnapshot);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY);
        var reopened = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        reopened.SelectPage(EffectLibraryPage.EFFECTS);
        Assert.Contains(reopened.ViewModel.Effects.Effects, item => item.Id == preset.Id);
    }

    [AvaloniaFact]
    public async Task InvalidTemplateCannotSaveOrModifyProjectAndSavedTemplateIsAppliedFromWorkbench()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.EFFECTS);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        var valid = settings.ViewModel.Effects.Source;
        var original = context.Session.DocumentSnapshot;
        settings.ViewModel.Effects.Source = "effect broken";
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await settings.ViewModel.Effects.SaveCommand.ExecutionTask!;
        await context.Session.EffectScripts.Completion;
        Assert.NotNull(settings.ViewModel.Effects.Error);
        Assert.Empty(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Assert.Same(original, context.Session.DocumentSnapshot);
        settings.ViewModel.Effects.Source = valid;
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await settings.ViewModel.Effects.SaveCommand.ExecutionTask!;
        await context.Session.EffectScripts.Completion;
        Assert.Same(original, context.Session.DocumentSnapshot);
        var preset = Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets);
        settings.Close();
        UiTestActions.SelectEffectPreset(context.Window, preset.Id.ToString("D"));
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(context.ViewModel.EffectPresets.ApplyCommand).ExecutionTask!;
        Assert.Single(context.Session.SelectedLayer!.Effects);
        Assert.False(settings.ViewModel.Effects.IsDirty);
    }
}
