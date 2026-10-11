using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MainDockContentUiTests
{
    private static readonly string[] standardActivePanelIds =
    [
        WorkbenchPanelIds.PREVIEW,
        WorkbenchPanelIds.TIMELINE,
        WorkbenchPanelIds.SUBTITLES,
        WorkbenchPanelIds.STYLES
    ];

    [AvaloniaFact]
    public async Task InitialMainWorkspaceContainsFourRealActiveViewsBeforeAnyLayoutSwitch()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;

        Assert.Equal(WorkspaceLayoutPresets.STANDARD, main.Layouts.CurrentPresetId);
        Assert.All(main.Layouts.Host.DataTemplates, template => Assert.False(template.Match(null)));
        Assert.Single(main.Layouts.Host.GetVisualDescendants().OfType<RootDockControl>());
        Assert.Equal(4, main.Layouts.Host.GetVisualDescendants().OfType<ToolDockControl>().Count());
        foreach (var id in standardActivePanelIds)
        {
            AssertAttached(main.Panels[id], main);
        }

        Assert.Same(main.ViewModel.Preview, main.Panels[WorkbenchPanelIds.PREVIEW].DataContext);
        Assert.Same(main.ViewModel.Timeline, main.Panels[WorkbenchPanelIds.TIMELINE].DataContext);
        Assert.Same(main.ViewModel.Subtitles, main.Panels[WorkbenchPanelIds.SUBTITLES].DataContext);
        Assert.Same(main.ViewModel.Styles, main.Panels[WorkbenchPanelIds.STYLES].DataContext);
        Assert.False(main.Layouts.IsModified);
    }

    [AvaloniaFact]
    public async Task OpeningMediaKeepsTimelineAndRealSubtitleInputsAttached()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var main = context.Window;
        await context.OpenMediaAsync();
        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        foreach (var id in standardActivePanelIds)
        {
            AssertAttached(main.Panels[id], main);
        }

        var timeline = UiTestActions.Find<SubtitleTimelineControl>(main, "Timeline");
        AssertAttached(timeline, main);
        Assert.True(timeline.Focus());
        Assert.Single(main.Panels[WorkbenchPanelIds.SUBTITLES].GetVisualDescendants()
            .OfType<TextBox>(), input => input.AcceptsReturn);
        main.Layouts.Activate(WorkbenchPanelIds.EFFECTS);
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        UiTestActions.ExpandEffectsCategory(main, "PathCategory");
        var path = UiTestActions.Find<Button>(main, "PathButton");
        AssertAttached(path, main);
        Assert.True(path.IsEffectivelyEnabled);
        UiTestActions.ExpandEffectsCategory(main, "TransformCategory");
        var reset = UiTestActions.Find<Button>(main, "ResetPositionButton");
        AssertAttached(reset, main);
        Assert.True(reset.IsEffectivelyEnabled);
        Assert.Same(main.ViewModel.Effects, main.Panels[WorkbenchPanelIds.EFFECTS].DataContext);
    }

    private static void AssertAttached(Control control, Window owner)
    {
        Assert.True(control.IsAttachedToVisualTree());
        Assert.Same(owner, TopLevel.GetTopLevel(control));
        Assert.True(control.Bounds.Width > 0);
        Assert.True(control.Bounds.Height > 0);
        Assert.NotNull(control.TranslatePoint(new Point(), owner));
    }
}
