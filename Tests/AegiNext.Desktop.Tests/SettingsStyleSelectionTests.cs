using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsStyleSelectionTests
{
    [Fact]
    public void FullLibraryExportOpensWithoutSavedSelectionAndKeepsTheInvalidDraft()
    {
        var model = new StyleLibraryViewModel();
        model.AddCommand.Execute(null);
        model.Name = "Unsaved style";
        model.FontSizeText = "7e-";
        var pending = model.Draft;
        var exports = 0;
        var saves = 0;
        model.ExportRequested += (_, _) => exports++;
        model.UpsertRequested += (_, _) => saves++;

        Assert.True(model.ExportCommand.CanExecute(null));
        model.ExportCommand.Execute(null);

        Assert.Equal(1, exports);
        Assert.Equal(0, saves);
        Assert.Equal(pending, model.Draft);
        Assert.Equal("7e-", model.FontSizeText);
        Assert.True(model.IsDirty);
        Assert.Empty(model.SelectedIds);
    }

    [Fact]
    public void FullExportDoesNotUseSelectedStylesOrValidateEditorText()
    {
        var values = Enumerable.Range(1, 3).Select(index => new SubtitleStylePreset(Guid.NewGuid(), $"Style {index}", new())).ToArray();
        var model = new StyleLibraryViewModel();
        model.UpdateStyles(values);
        model.Name = "unsaved";
        model.FontSizeText = "invalid";
        model.SelectStyles(values[0].Id, [values[0].Id, values[2].Id]);
        var exports = 0;
        model.ExportRequested += (_, _) => exports++;

        model.ExportCommand.Execute(null);

        Assert.Equal(1, exports);
        Assert.Equal(values, model.Styles);
        Assert.Equal(new[] { values[0].Id, values[2].Id }, model.SelectedIds);
        Assert.False(model.CanEdit);
        Assert.True(model.DeleteCommand.CanExecute(null));
        Assert.False(model.SaveCommand.CanExecute(null));
        model.SelectStyles(null, []);
        Assert.True(model.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void NewDraftStaysOutsideListAndDeleteWaitsForConfirmation()
    {
        var model = new StyleLibraryViewModel();
        var requests = 0;
        model.DeleteRequested += (_, _) => requests++;
        model.AddCommand.Execute(null);
        Assert.NotNull(model.Draft);
        Assert.Empty(model.Styles);
        Assert.True(model.DeleteCommand.CanExecute(null));

        model.DeleteCommand.Execute(null);

        Assert.NotNull(model.Draft);
        Assert.Equal(1, requests);
        model.DiscardDraft();
        Assert.Null(model.Draft);
        Assert.Empty(model.Styles);
    }

    [Fact]
    public async Task SavingBeforeSwitchWaitsForPersistenceAndFailuresKeepTheDraft()
    {
        var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new());
        var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([first, second]);
        var completion = new TaskCompletionSource<bool>();
        model.SaveDraftAsync = _ => completion.Task;
        model.Name = "Edited";

        var switching = model.SelectStylesAsync(second.Id, [second.Id]);
        Assert.False(switching.IsCompleted);
        Assert.Equal(first.Id, model.SelectedStyle!.Id);
        completion.SetResult(false);
        Assert.False(await switching);
        Assert.Equal("Edited", model.Name);
        Assert.Equal(first.Id, model.SelectedStyle.Id);

        model.SaveDraftAsync = preset =>
        {
            model.UpdateStyles([preset, second], preset.Id);
            return Task.FromResult(true);
        };
        Assert.True(await model.SelectStylesAsync(second.Id, [second.Id]));
        Assert.Equal("Edited", model.Styles[0].Name);
        Assert.Equal(second.Id, model.SelectedStyle!.Id);
    }

    [Fact]
    public async Task InvalidNumbersBlockSavingAndDiscardRestoresTheStoredStyle()
    {
        var saved = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([saved]);
        var saves = 0;
        model.SaveDraftAsync = _ => { saves++; return Task.FromResult(true); };
        model.FontSizeText = "7e-";
        Assert.False(await model.SavePendingAsync());
        Assert.Equal(0, saves);
        Assert.Equal("7e-", model.FontSizeText);

        model.DiscardDraft();

        Assert.Equal(saved, model.Draft);
        Assert.Null(model.Error);
        Assert.False(model.IsDirty);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task LeavingHonorsDiscardAndCancelWithoutSaving(int choice, bool expected)
    {
        var saved = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([saved]);
        model.Name = "Changed";
        model.ConfirmLeaveAsync = () => Task.FromResult(choice);
        var saves = 0;
        model.SaveDraftAsync = _ => { saves++; return Task.FromResult(true); };

        Assert.Equal(expected, await model.PrepareToLeaveAsync());
        Assert.Equal(expected ? "Saved" : "Changed", model.Name);
        Assert.Equal(0, saves);
    }
}
