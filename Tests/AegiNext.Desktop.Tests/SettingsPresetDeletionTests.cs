using AegiNext.Desktop.EffectLibrary;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.Settings.Export;
using AegiNext.Desktop.EffectLibrary.Styles;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsPresetDeletionTests
{
    [Fact]
    public void StyleBatchDeletionRequestsOnlySelectedSavedIdentities()
    {
        var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new());
        var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new());
        var third = new SubtitleStylePreset(Guid.NewGuid(), "Third", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([first, second, third]);
        model.SelectStyles(second.Id, [second.Id, first.Id, second.Id]);
        LibraryStyleDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        Assert.True(model.DeleteCommand.CanExecute(null));
        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
        Assert.False(requested.IsDraftOnly);
        Assert.Null(requested.DraftId);
        Assert.Equal(new[] { first, second, third }, model.Styles.ToArray());
        model.SelectStyles(third.Id, [third.Id]);
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
    }

    [Fact]
    public void UnsavedInvalidStyleRequestsConfirmationAndKeepsTheEntireDraft()
    {
        var model = new StyleLibraryViewModel();
        model.AddCommand.Execute(null);
        model.Name = "Unsaved";
        model.FontSizeText = "7e-";
        var draft = model.Draft;
        LibraryStyleDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Empty(requested.Ids);
        Assert.True(requested.IsDraftOnly);
        Assert.Equal(draft!.Id, requested.DraftId);
        Assert.Equal(draft, model.Draft);
        Assert.Equal("7e-", model.FontSizeText);
        Assert.True(model.IsDirty);
        model.DiscardDraft();
        Assert.Null(model.Draft);
    }

    [Fact]
    public void EffectBatchDeletionRequestsEverySelectedPersonalIdentity()
    {
        var first = Effect("First");
        var second = Effect("Second");
        var third = Effect("Third");
        var model = new EffectScriptLibraryViewModel();
        model.UpdateEffects([first, second, third]);
        model.SelectEffects(second.Id, [first.Id, second.Id]);
        LibraryEffectDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        Assert.True(model.DeleteCommand.CanExecute(null));
        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
        Assert.False(requested.IsDraftOnly);
        Assert.Null(requested.DraftId);
        Assert.Contains(model.Effects, item => item.Id == first.Id);
        Assert.Contains(model.Effects, item => item.Id == second.Id);
        model.SelectEffects(third.Id, [third.Id]);
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
    }

    [Fact]
    public void MixedBuiltinAndPersonalEffectSelectionCannotDeleteAnyItem()
    {
        var personal = Effect("Personal");
        var model = new EffectScriptLibraryViewModel();
        model.UpdateEffects([personal]);
        var builtin = model.Effects.First(item => item.IsBuiltin);
        model.SelectEffects(personal.Id, [builtin.Id, personal.Id]);
        var requested = 0;
        model.DeleteRequested += (_, _) => requested++;

        Assert.False(model.CanDelete);
        Assert.False(model.DeleteCommand.CanExecute(null));
        model.DeleteCommand.Execute(null);

        Assert.Equal(0, requested);
        Assert.Equal(new[] { builtin.Id, personal.Id }, model.SelectedIds.ToArray());
    }

    [Fact]
    public async Task UnsavedInvalidEffectRequestsConfirmationAndKeepsDiagnostics()
    {
        var model = new EffectScriptLibraryViewModel();
        await model.AddCommand.ExecuteAsync(null);
        model.Source = "effect broken";
        model.ValidateCommand.Execute(null);
        var draft = model.Draft;
        var error = model.Error;
        LibraryEffectDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Empty(requested.Ids);
        Assert.True(requested.IsDraftOnly);
        Assert.Equal(draft!.Id, requested.DraftId);
        Assert.Equal(draft, model.Draft);
        Assert.Equal("effect broken", model.Source);
        Assert.Equal(error, model.Error);
        Assert.True(model.IsDirty);
        model.DiscardDraft();
        Assert.Null(model.Draft);
    }

    [Fact]
    public async Task ExportBatchDeletionRequestsOnlySelectedSavedIdentities()
    {
        var first = new VideoExportPreset(Guid.NewGuid(), "First", new());
        var second = new VideoExportPreset(Guid.NewGuid(), "Second", new());
        var third = new VideoExportPreset(Guid.NewGuid(), "Third", new());
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([first, second, third]);
        Assert.True(await model.SelectPresetsAsync(second.Id, [first.Id, second.Id]));
        SettingsExportPresetDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        Assert.True(model.DeleteCommand.CanExecute(null));
        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
        Assert.False(requested.IsDraftOnly);
        Assert.Null(requested.DraftId);
        Assert.Equal(new[] { first, second, third }, model.ExportPresets.ToArray());
        Assert.True(await model.SelectPresetsAsync(third.Id, [third.Id]));
        Assert.Equal(new[] { first.Id, second.Id }, requested.Ids.ToArray());
    }

    [Fact]
    public async Task UnsavedInvalidExportPresetRequestsConfirmationAndKeepsInput()
    {
        var model = new ExportSettingsViewModel();
        model.AddCommand.Execute(null);
        await model.SelectionCompletion;
        model.Name = "Unsaved";
        model.QualityText = "7e-";
        var draft = model.Draft;
        SettingsExportPresetDeleteEventArgs? requested = null;
        model.DeleteRequested += (_, args) => requested = args;

        model.DeleteCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Empty(requested.Ids);
        Assert.True(requested.IsDraftOnly);
        Assert.Equal(draft!.Id, requested.DraftId);
        Assert.Equal(draft, model.Draft);
        Assert.Equal("7e-", model.QualityText);
        Assert.True(model.IsDirty);
        model.DiscardDraft();
        Assert.Null(model.Draft);
    }

    [Fact]
    public void BusyAndEmptySelectionDoNotProduceDeletionRequests()
    {
        var style = new StyleLibraryViewModel();
        var effect = new EffectScriptLibraryViewModel();
        var export = new ExportSettingsViewModel();
        effect.SelectEffects(null, []);
        var requested = 0;
        style.DeleteRequested += (_, _) => requested++;
        effect.DeleteRequested += (_, _) => requested++;
        export.DeleteRequested += (_, _) => requested++;
        Assert.False(style.DeleteCommand.CanExecute(null));
        Assert.False(effect.DeleteCommand.CanExecute(null));
        Assert.False(export.DeleteCommand.CanExecute(null));
        style.UpdateStyles([new(Guid.NewGuid(), "Saved", new())]);
        effect.UpdateEffects([Effect("Saved")]);
        effect.SelectEffects(effect.Effects.Last().Id, [effect.Effects.Last().Id]);
        export.UpdatePresets([new(Guid.NewGuid(), "Saved", new())]);
        style.IsBusy = true;
        effect.IsBusy = true;
        export.IsBusy = true;

        style.DeleteCommand.Execute(null);
        effect.DeleteCommand.Execute(null);
        export.DeleteCommand.Execute(null);

        Assert.Equal(0, requested);
    }

    [Fact]
    public void DeletionArgumentsFixAndDeduplicateInputsAndPreserveSingleIdentityCompatibility()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var ids = new List<Guid> { second, first, second };
        var style = new LibraryStyleDeleteEventArgs(ids);
        var effect = new LibraryEffectDeleteEventArgs(ids);
        var export = new SettingsExportPresetDeleteEventArgs(ids);
        ids.Clear();

        Assert.Equal(new[] { second, first }, style.Ids.ToArray());
        Assert.Equal(new[] { second, first }, effect.Ids.ToArray());
        Assert.Equal(new[] { second, first }, export.Ids.ToArray());
        Assert.Equal(first, new LibraryStyleDeleteEventArgs(first).Id);
        Assert.Equal(first, new LibraryEffectDeleteEventArgs(first).Id);
        Assert.Equal(first, new SettingsExportPresetDeleteEventArgs(first).Id);
    }

    private static EffectScriptPreset Effect(string name)
    {
        var id = Guid.NewGuid();
        var source = BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"custom-{id:N}\"", StringComparison.Ordinal);
        return new(id, name, source);
    }
}
