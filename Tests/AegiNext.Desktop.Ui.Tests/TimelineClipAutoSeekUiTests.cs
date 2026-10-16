using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipAutoSeekUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeftToolbarUsesCompactSquaresInANarrowPanelAndKeepsLiveLocalizedToggleText(bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        using var panel = new TimelinePanelView(context.ViewModel.Timeline, context.Session);
        var window = new Window
        {
            Width = 320,
            Height = 300,
            Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        var previousLanguage = Localization.SelectedLanguageID;
        try
        {
            window.Show();
            Flush(window);
            foreach (var name in new[]
                     {
                         "TimelineSnapButton", "TimelineSpectrumButton", "TimelineWaveformButton", "TimelineStepButton",
                         "TimelineAutoSeekButton", "TimelineExpandAllTracksButton", "TimelineCollapseAllTracksButton",
                         "TimelineClassicTimingButton", "TimelineTimingPostProcessorButton"
                     })
            {
                var button = panel.FindControl<Button>(name)!;
                Assert.Equal(new Size(24, 24), button.Bounds.Size);
                var content = Assert.IsAssignableFrom<Control>(button.Content);
                Assert.Equal(new Size(16, 16), content.Bounds.Size);
                Assert.Equal(new Point(12, 12), content.TranslatePoint(new(8, 8), button)!.Value);
            }

            var toggle = panel.FindControl<ToolbarToggleButton>("TimelineAutoSeekButton")!;
            Assert.False(toggle.IsChecked);
            Localization.SetLanguage("en-US");
            Flush(window);
            Assert.Equal("Jump to selected clip", AutomationProperties.GetName(toggle));
            Localization.SetLanguage("zh-CN");
            Flush(window);
            Assert.Equal("自动跳转到选中片段", AutomationProperties.GetName(toggle));
            Assert.Equal(Localization.Get("Workbench.TimelineAutoSeekHint"), ToolTip.GetTip(toggle));
            UiTestActions.Click(window, toggle);
            Assert.True(context.ViewModel.Timeline.IsAutoSeekEnabled);
            Assert.True(context.Session.Preferences.TimelineAutoSeekEnabled);
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("timeline", false)]
    [InlineData("timeline", true)]
    [InlineData("subtitle-list", false)]
    [InlineData("subtitle-list", true)]
    [InlineData("canvas", false)]
    [InlineData("canvas", true)]
    public async Task AcceptedSelectionSeeksThePrimaryClipAndPreservesPlaybackAndTheWholeSelection(string source, bool playing)
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var document = SetDocument(context, new(6501, 1000), new(8501, 1000));
        var first = document.Layers[0];
        var primary = document.Layers[1];
        context.Session.SelectLayer(first.Id, [first.Id]);
        await context.Session.SeekProjectTimeAsync(new(10));
        UiTestActions.Click(context.Window, "TimelineAutoSeekButton");
        Assert.Equal(new MediaTime(10), context.Session.ProjectPosition);
        if (playing)
        {
            await context.Controller.PlayAsync();
        }

        switch (source)
        {
            case "timeline":
                ClickTimelineClip(context, primary.Id, RawInputModifiers.Control);
                break;
            case "subtitle-list":
                ClickSubtitleRow(context, primary.SubtitleId!.Value, RawInputModifiers.Shift);
                break;
            case "canvas":
                context.ViewModel.Effects.SelectLayer(primary.Id, [first.Id, primary.Id]);
                break;
        }

        await WaitForPositionAsync(context, primary.Start);

        Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal(primary.Id, context.Session.SelectedLayerId);
        Assert.Equal(new[] { first.Id, primary.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaFact]
    public async Task RapidSelectionKeepsTheLatestClipTargetWhenAnEarlierDecodeCompletes()
    {
        await using var context = new SeekSchedulingTestContext();
        await context.OpenMediaAsync();
        var first = new SubtitleLine { Text = "First", Start = new(5), End = new(7) };
        var second = new SubtitleLine { Text = "Latest", Start = new(10), End = new(12) };
        var session = context.Window.Session;
        session.Editor.Reset(session.DocumentSnapshot with
        {
            Subtitles = [first, second], Layers = [Layer(first), Layer(second)]
        });
        var earlier = context.Source.BlockNextSeek(first.Start);
        var latest = context.Source.BlockNextSeek(second.Start);
        session.ViewModel.Timeline.IsAutoSeekEnabled = true;
        session.SelectCue(first.Id);
        await earlier.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        session.SelectCue(second.Id);

        Assert.Equal(second.Start, session.ViewModel.Timeline.Position);
        Assert.Equal(10, session.ViewModel.Preview.Position);
        earlier.Release();
        await latest.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Flush(context.Window);
        Assert.Equal(second.Start, session.ViewModel.Timeline.Position);
        Assert.Equal(second.Id, session.SelectedCueId);
        latest.Release();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Controller.Snapshot.Position != second.Start || context.Controller.Snapshot.PresentedAtPosition != second.Start)
        {
            Assert.True(DateTime.UtcNow < deadline, "The latest clip selection did not finish seeking.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.Equal(second.Start, session.ProjectPosition);
        Assert.Equal(second.Id, session.SelectedCueId);
        Assert.Null(session.LastError);
        Assert.Equal(second.Start, context.Source.SeekTargets.Last());
    }

    [AvaloniaFact]
    public async Task DisabledSelectionDocumentRefreshAndRejectedDraftSelectionDoNotMoveThePlayhead()
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var document = SetDocument(context, new(6), new(8));
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id]);
        await context.Session.SeekProjectTimeAsync(new(10));

        context.Session.SelectCue(document.Subtitles[1].Id);
        Assert.Equal(new MediaTime(10), context.Session.ProjectPosition);
        context.ViewModel.Timeline.IsAutoSeekEnabled = true;
        context.Session.Editor.UpdateLayer(document.Layers[0].Id, layer => layer with { Name = "Refreshed" });
        Flush(context.Window);
        Assert.Equal(new MediaTime(10), context.Session.ProjectPosition);

        var row = context.ViewModel.Subtitles.Rows.Single(value => value.Id == document.Subtitles[1].Id);
        row.StartText = "unfinished";
        context.Session.SelectCue(document.Subtitles[0].Id);
        Flush(context.Window);

        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.Equal(new MediaTime(10), context.Session.ProjectPosition);
        row.Accept(document.Subtitles[1]);
    }

    [AvaloniaFact]
    public async Task SelectingWithoutMediaDoesNotSubmitASeek()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var document = SetDocument(context, new(6), new(8));
        context.Session.InteractionDiagnostics.Enabled = true;
        context.ViewModel.Timeline.IsAutoSeekEnabled = true;

        context.Session.SelectCue(document.Subtitles[1].Id);
        Flush(context.Window);

        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.False(context.ViewModel.Preview.CanSeek);
        Assert.DoesNotContain(context.Session.InteractionDiagnostics.Events, value => value.Stage == "accepted");
        Assert.Null(context.Session.LastError);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(-1, 1, 0)]
    [InlineData(21, 22, 20)]
    public async Task SelectionClampsOutOfMediaClipStartsThroughTheSharedPlaybackCoordinator(int start, int end, int expected)
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var document = SetDocument(context, new(start), new(end));
        context.ViewModel.Timeline.IsAutoSeekEnabled = true;

        context.Session.SelectCue(document.Subtitles[1].Id);

        await WaitForPositionAsync(context, new(expected));
        Assert.Null(context.Session.LastError);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    private static MainWindowTestContext CreateContext()
    {
        return new(videoSourceFactory: () => new(1, Enumerable.Range(0, 201).Select(index => 3000L + index * 100).ToArray()),
            mediaStart: new(3));
    }

    private static ProjectDocument SetDocument(MainWindowTestContext context, MediaTime start, MediaTime end)
    {
        var first = new SubtitleLine { Text = "First", Start = new(2), End = new(3) };
        var second = new SubtitleLine { Text = "Primary", Start = start, End = end };
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [first, second],
            Layers = [Layer(first), Layer(second)]
        });
        Flush(context.Window);
        return context.Session.DocumentSnapshot;
    }

    private static ProjectLayer Layer(SubtitleLine cue)
    {
        return new() { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
    }

    private static void ClickTimelineClip(MainWindowTestContext context, Guid id, RawInputModifiers modifiers)
    {
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        context.ViewModel.Timeline.Viewport = new(0, 32);
        Flush(context.Window);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var point = timeline.TranslatePoint(timeline.GetClipRectangle(id)!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
    }

    private static void ClickSubtitleRow(MainWindowTestContext context, Guid id, RawInputModifiers modifiers)
    {
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == id);
        list.ScrollIntoView(row);
        Flush(context.Window);
        var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(row));
        var cell = container.GetVisualDescendants().OfType<TextBlock>().Single(value =>
            value.IsEffectivelyVisible && value.GetVisualParent() is Grid grid && grid.ColumnDefinitions.Count == 5 &&
            value.DataContext is SubtitleRow subtitle && subtitle.Id == id && Grid.GetColumn(value) == 0 &&
            value.Text == subtitle.ContentType);
        var point = cell.TranslatePoint(new(cell.Bounds.Width / 2, cell.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
    }

    private static async Task WaitForPositionAsync(MainWindowTestContext context, MediaTime target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Session.ProjectPosition != target || context.Controller.Snapshot.PresentedAtPosition != target + new MediaTime(3))
        {
            Assert.True(DateTime.UtcNow < deadline, "Clip selection did not reach the expected preview position.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
