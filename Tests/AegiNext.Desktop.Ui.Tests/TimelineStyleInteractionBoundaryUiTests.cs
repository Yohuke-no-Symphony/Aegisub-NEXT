using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineStyleInteractionBoundaryUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FloatingLineHeightDraftSurvivesSoloAndAllTrackButtonsWithoutContentUndoOrFocusChanges(bool invalid)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Fonts.EnsureLoadedAsync();
        Dispatcher.UIThread.RunJobs();
        var firstTrack = context.Session.DocumentSnapshot.Tracks[0].Id;
        var otherTrack = context.Session.Editor.AddTrack("Other");
        var first = context.Session.Editor.AddSubtitle(new(0), new(2), "First\nSecond", firstTrack);
        var second = context.Session.Editor.AddSubtitle(new(3), new(5), "Other", otherTrack);
        context.Session.Editor.SetKeyframe(first, AnimationProperty.OPACITY, new(new(1), 0.5));
        context.Session.Editor.SetKeyframe(second, AnimationProperty.OPACITY, new(new(1), 0.75));
        context.Session.SelectCue(first);
        var committed = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(committed);
        var row = new TimelineEffectClipId(first, new(AnimationProperty.OPACITY));
        context.Session.SetTimelineEffectClipCollapsed(row, true);
        context.Window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        context.Window.Layouts.Float(WorkbenchPanelIds.STYLES);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        floating.Width = 350;
        floating.Height = 1000;
        Flush(floating);
        var input = UiTestActions.Find<NumericDraftInput>(floating, "LineHeightInput");
        input.BringIntoView();
        Flush(floating);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        try
        {
            Type(floating, text, "1.23456789");
            var preview = context.Session.PreviewDocument;
            Assert.Equal(1.23456789, preview.Subtitles.Single(line => line.Id == first).Style.LineHeight);
            if (invalid)
            {
                Type(floating, text, "7e-");
                Assert.Same(preview, context.Session.PreviewDocument);
            }
            var rawText = invalid ? "7e-" : "1.23456789";
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            var selection = context.Session.SelectedLayerId;
            var viewport = timeline.Viewport;

            ClickSolo(context.Window, timeline, otherTrack);

            Assert.Equal(otherTrack, context.ViewModel.Timeline.SoloTrackId);
            Assert.Null(timeline.GetTrackHeaderRectangle(firstTrack));
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);
            UiTestActions.Click(context.Window, "TimelineCollapseAllTracksButton");
            Flush(context.Window);
            Assert.True(timeline.IsTrackCollapsed(firstTrack));
            Assert.True(timeline.IsTrackCollapsed(otherTrack));
            Assert.Equal(otherTrack, context.ViewModel.Timeline.SoloTrackId);
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);

            ClickSolo(context.Window, timeline, otherTrack);

            Assert.Null(context.ViewModel.Timeline.SoloTrackId);
            Assert.NotNull(timeline.GetTrackHeaderRectangle(firstTrack));
            Assert.True(timeline.IsTrackCollapsed(firstTrack));
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);
            UiTestActions.Click(context.Window, "TimelineExpandAllTracksButton");
            Flush(context.Window);
            Assert.False(timeline.IsTrackCollapsed(firstTrack));
            Assert.False(timeline.IsTrackCollapsed(otherTrack));
            Assert.True(timeline.IsEffectClipCollapsed(row));
            Assert.NotNull(timeline.GetEffectClipRectangle(row));
            Assert.NotNull(timeline.GetEffectClipRectangle(new(second, new(AnimationProperty.OPACITY))));
            Assert.Equal(viewport.StartSeconds, timeline.Viewport.StartSeconds);
            Assert.Equal(viewport.PixelsPerSecond, timeline.Viewport.PixelsPerSecond);
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);

            ClickTrackExpander(context.Window, timeline, firstTrack);

            Assert.True(timeline.IsTrackCollapsed(firstTrack));
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);
            ClickTrackExpander(context.Window, timeline, firstTrack);
            Assert.False(timeline.IsTrackCollapsed(firstTrack));
            Assert.True(timeline.IsEffectClipCollapsed(row));
            AssertDraftUnchanged(context, text, rawText, committed, preview, selection);
        }
        finally
        {
            context.ViewModel.Styles.Subtitle.LineHeightText = committed.Subtitles.Single(line => line.Id == first).Style.LineHeight
                .ToString(CultureInfo.CurrentCulture);
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
        }
    }

    private static void AssertDraftUnchanged(MainWindowTestContext context, TextBox text, string rawText,
        ProjectDocument committed, ProjectDocument preview, Guid? selected)
    {
        Assert.True(text.IsFocused);
        Assert.Equal(rawText, text.Text);
        Assert.Equal(rawText, context.ViewModel.Styles.Subtitle.LineHeightText);
        Assert.Same(committed, context.Session.DocumentSnapshot);
        Assert.Same(preview, context.Session.PreviewDocument);
        Assert.Equal(selected, context.Session.SelectedLayerId);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void ClickSolo(Window window, SubtitleTimelineControl timeline, Guid trackId)
    {
        Flush(window);
        var point = timeline.TranslatePoint(timeline.GetTrackSoloToggleRectangle(trackId)!.Value.Center, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static void Type(Window window, TextBox input, string value)
    {
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput(value);
        Flush(window);
    }

    private static void ClickTrackExpander(Window window, SubtitleTimelineControl timeline, Guid trackId)
    {
        Flush(window);
        var point = timeline.TranslatePoint(timeline.GetTrackExpanderRectangle(trackId)!.Value.Center, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
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
