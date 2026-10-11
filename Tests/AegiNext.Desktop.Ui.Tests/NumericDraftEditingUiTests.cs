using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NumericDraftEditingUiTests
{
    [AvaloniaTheory]
    [InlineData("styles", "FontSizeInput")]
    [InlineData("effects", "PositionXInput")]
    [InlineData("effects", "KeyframeValueInput")]
    [InlineData("export", "QualityInput")]
    public async Task InvalidRawNumericInputPreventsPresetSwitchAndPreservesInput(string panelId, string fieldName)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        window.Layouts.Activate(panelId);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        if (fieldName == "KeyframeValueInput")
        {
            UiTestActions.SelectAnimationProperty(window, AnimationProperty.OPACITY);
        }
        var input = UiTestActions.Find<NumericDraftInput>(window, fieldName);
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        box.Text = "7e-";
        Dispatcher.UIThread.RunJobs();
        var snapshot = window.DocumentSnapshot;
        var layoutRoot = window.Layouts.Root;
        var presetId = window.Layouts.CurrentPresetId;

        Assert.False(await window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(snapshot, window.DocumentSnapshot);
        Assert.Same(layoutRoot, window.Layouts.Root);
        Assert.Equal(presetId, window.Layouts.CurrentPresetId);
        Assert.Equal("7e-", input.RawText);
        Assert.Equal("7e-", box.Text);
        Assert.Equal(panelId, window.ViewModel.InvalidPanelId);
        Assert.Equal(fieldName, window.ViewModel.InvalidFieldKey);
        input.RawText = fieldName is "FontSizeInput" ? "64" : fieldName is "QualityInput" ? "20" : "0";
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.TryCommitDrafts());
    }

    [AvaloniaFact]
    public async Task ValidRawFontSizeIsParsedCommittedAndRestoredByUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var original = Assert.Single(window.DocumentSnapshot.Subtitles).Style.FontSize;
        var input = UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput");
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        box.Text = "72.5";
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(72.5, Assert.Single(window.DocumentSnapshot.Subtitles).Style.FontSize);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(original, Assert.Single(window.DocumentSnapshot.Subtitles).Style.FontSize);
        Assert.Equal((decimal)original, input.Value);
    }
}
