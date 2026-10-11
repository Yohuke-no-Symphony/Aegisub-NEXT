using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsKaraokeToolbarUiTests
{
    [AvaloniaFact]
    public async Task HighlightToggleGatesTimingActionsAcrossUndoRedoAndSubtitleSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "a😀b");
        var second = context.Session.Editor.AddSubtitle(new(5), new(9), "untimed");
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(first);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        var toolbar = UiTestActions.Find<WrapPanel>(host, "KaraokeTimingActions");
        var enable = UiTestActions.Find<ToolbarToggleButton>(host, "EnableKaraokeToggle");
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        AssertDisabledActions(toolbar, enable);
        Assert.Same(enable, toolbar.Children[0]);
        Assert.False(axis.IsEffectivelyEnabled);

        UiTestActions.Click(host, "EnableKaraokeToggle");
        var enabled = context.Session.Editor.Snapshot;
        var clips = enabled.Subtitles.Single(line => line.Id == first).Karaoke;
        Assert.Equal(3, clips.Length);
        Assert.True(enable.IsChecked);
        Assert.True(axis.IsEffectivelyEnabled);
        foreach (var name in new[]
        {
            "SubtitlePlayPauseButton", "SubtitleLoopToggle", "KaraokeSnapToggle", "KaraokeTimeLabelsToggle",
            "LinkedKaraokeTimingToggle", "GenerateAllTimingButton", "FitKaraokeAxisButton"
        })
        {
            Assert.True(UiTestActions.Find<Button>(host, name).IsEffectivelyEnabled, name);
        }
        Assert.False(UiTestActions.Find<Button>(host, "CreateSelectedTimingButton").IsEffectivelyEnabled);
        Assert.False(UiTestActions.Find<Button>(host, "MergeSelectedKaraokeGroupsButton").IsEffectivelyEnabled);

        UiTestActions.Click(host, "EnableKaraokeToggle");
        var disabled = context.Session.Editor.Snapshot;
        AssertDisabledActions(toolbar, enable);
        Assert.False(axis.IsEffectivelyEnabled);
        Assert.True(UiTestActions.Find<Button>(host, "RestoreCachedTimingButton").IsEffectivelyVisible);
        Assert.Equal(clips.ToArray(), disabled.Subtitles.Single(line => line.Id == first).InactiveKaraoke.ToArray());

        Assert.True(context.Session.Editor.Undo());
        Assert.Same(enabled, context.Session.Editor.Snapshot);
        Assert.True(enable.IsChecked);
        Assert.True(UiTestActions.Find<Button>(host, "GenerateAllTimingButton").IsEffectivelyEnabled);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(disabled, context.Session.Editor.Snapshot);
        AssertDisabledActions(toolbar, enable);

        UiTestActions.Click(host, "EnableKaraokeToggle");
        Assert.Equal(clips.ToArray(), context.Session.Editor.Snapshot.Subtitles.Single(line => line.Id == first).Karaoke.ToArray());
        context.Session.SelectCue(second);
        AssertDisabledActions(toolbar, enable);
        context.Session.SelectCue(first);
        Assert.True(enable.IsChecked);
        Assert.True(UiTestActions.Find<Button>(host, "GenerateAllTimingButton").IsEffectivelyEnabled);
    }

    private static void AssertDisabledActions(WrapPanel toolbar, ToolbarToggleButton enable)
    {
        Assert.True(enable.IsEffectivelyEnabled);
        Assert.False(enable.IsChecked);
        var buttons = toolbar.GetVisualDescendants().OfType<Button>().Where(button => button != enable).ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled, button.Name));
    }
}
