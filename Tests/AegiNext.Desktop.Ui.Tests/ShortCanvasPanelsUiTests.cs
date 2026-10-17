using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ShortCanvasPanelsUiTests
{
    [AvaloniaTheory]
    [InlineData(50, 260)]
    [InlineData(90, 260)]
    [InlineData(130, 260)]
    [InlineData(50, 520)]
    [InlineData(90, 520)]
    [InlineData(130, 520)]
    public async Task PreviewOptionsAndTransportRemainReachableAndPictureScalingRecovers(double height, double width)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var panel = context.Window.Panels[WorkbenchPanelIds.PREVIEW];
        var host = Float(context, WorkbenchPanelIds.PREVIEW, width);
        try
        {
            SetPanelHeight(host, panel, 360);
            var canvas = UiTestActions.Find<EffectCanvasControl>(host, "EffectCanvas");
            var originalPicture = canvas.ProjectRectangle;
            var originalDocument = context.Session.DocumentSnapshot;
            var scroll = UiTestActions.Find<OverflowScrollViewer>(host, "PreviewPanelScrollViewer");
            SetPanelHeight(host, panel, height);
            AssertViewportFits(scroll, panel, height);
            var surface = UiTestActions.Find<Grid>(host, "VideoSurface");
            var quality = UiTestActions.Find<ComboBox>(host, "QualityCombo");
            AssertFits(quality, surface);
            AssertFits(UiTestActions.Find<Border>(host, "ModeBadge"), surface);
            foreach (var name in new[] { "QualityCombo", "ModeBadge", "PositionSlider", "TransportRow",
                         "PlayButton", "TimeLabel", "MuteButton", "VolumeSlider" })
            {
                var control = UiTestActions.Find<Control>(host, name);
                control.BringIntoView();
                Flush(host);
                AssertFits(control, Presenter(scroll));
            }

            var picture = canvas.ProjectRectangle;
            Assert.True(picture.Width > 0 && picture.Height > 0);
            Assert.True(new Rect(canvas.Bounds.Size).Contains(picture));
            Assert.Equal((double)originalDocument.Width / originalDocument.Height, picture.Width / picture.Height, 6);
            Assert.True(picture.Height < originalPicture.Height);
            quality.BringIntoView();
            Flush(host);
            var point = quality.TranslatePoint(new(quality.Bounds.Width / 2, quality.Bounds.Height / 2), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            host.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(quality.IsDropDownOpen);
            quality.IsDropDownOpen = false;

            SetPanelHeight(host, panel, 360);
            Assert.Equal(originalPicture, canvas.ProjectRectangle);
            Assert.InRange(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height), 0, 1);
            Assert.InRange(scroll.Offset.Y, 0, 1);
            Assert.Same(originalDocument, context.Session.DocumentSnapshot);
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(130)]
    public async Task TimelineToolbarKeepsNormalAnchoringAndScrollsEveryActionIntoShortViewports(double height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var panel = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        var host = Float(context, WorkbenchPanelIds.TIMELINE, 420);
        try
        {
            SetPanelHeight(host, panel, 186);
            var scroll = UiTestActions.Find<OverflowScrollViewer>(host, "TimelineToolbarOverflowScroller");
            var inner = UiTestActions.Find<ScrollViewer>(host, "TimelineToolbarScroller");
            var classic = UiTestActions.Find<ToolbarToggleButton>(host, "TimelineClassicTimingButton");
            var processor = UiTestActions.Find<Button>(host, "TimelineTimingPostProcessorButton");
            var originalBottomInset = panel.Bounds.Height - BoundsIn(processor, panel).Bottom;
            Assert.True(inner.Extent.Height > inner.Viewport.Height);
            Assert.InRange(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height), 0, 1);
            AssertFits(classic, Presenter(scroll));
            AssertFits(processor, Presenter(scroll));
            SetPanelHeight(host, panel, height);
            AssertViewportFits(scroll, panel, height);
            Assert.True(inner.Viewport.Height > 0);

            if (scroll.Extent.Height > scroll.Viewport.Height + 1)
            {
                scroll.Offset = default;
                inner.Offset = new(0, inner.Extent.Height);
                Flush(host);
                var point = Presenter(inner).TranslatePoint(new(12, inner.Viewport.Height / 2), host)!.Value;
                host.MouseWheel(point, new(0, -2));
                Flush(host);
                Assert.True(scroll.Offset.Y > 0);
            }

            foreach (var name in new[] { "TimelineSnapButton", "TimelineStepButton", "TimelineSpectrumButton",
                         "TimelineWaveformButton", "TimelineAutoSeekButton", "TimelineExpandAllTracksButton",
                         "TimelineCollapseAllTracksButton", "TimelineClassicTimingButton", "TimelineTimingPostProcessorButton" })
            {
                var control = UiTestActions.Find<Control>(host, name);
                control.BringIntoView();
                Flush(host);
                AssertFits(control, Presenter(scroll));
                Assert.Equal(24, control.Bounds.Width);
                Assert.Equal(24, control.Bounds.Height);
                if (control.GetVisualAncestors().Contains(inner))
                {
                    AssertFits(control, Presenter(inner));
                }
            }

            SetPanelHeight(host, panel, 186);
            Assert.InRange(scroll.Offset.Y, 0, 1);
            Assert.InRange(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height), 0, 1);
            Assert.Equal(originalBottomInset, panel.Bounds.Height - BoundsIn(processor, panel).Bottom, 6);
            Assert.True(BoundsIn(inner, panel).Bottom <= BoundsIn(classic, panel).Top);
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(50, 260)]
    [InlineData(90, 260)]
    [InlineData(130, 260)]
    [InlineData(50, 520)]
    [InlineData(90, 520)]
    [InlineData(130, 520)]
    public async Task TrackRenameUsesTheAvailableSpaceWithoutStretchingItsNormalBackground(double height, double width)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.SelectTrack(Assert.IsType<Guid>(context.Session.CurrentTrackId));
        var originalDocument = context.Session.DocumentSnapshot;
        var panel = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        var host = Float(context, WorkbenchPanelIds.TIMELINE, width);
        try
        {
            SetPanelHeight(host, panel, 360);
            context.ViewModel.Timeline.RenameTrackCommand.Execute(null);
            Flush(host);
            Assert.True(context.ViewModel.Timeline.IsRenamingTrack);
            var frame = UiTestActions.Find<Border>(host, "TrackRenameEditor");
            var slot = UiTestActions.Find<Grid>(host, "TrackRenameViewport");
            var normalHeight = frame.Bounds.Height;
            Assert.True(normalHeight < slot.Bounds.Height);
            var scroll = UiTestActions.Find<ScrollViewer>(host, "TrackRenameScrollViewer");
            SetPanelHeight(host, panel, height);
            AssertFits(frame, panel);
            AssertFits(frame, slot);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Viewport.Height <= slot.Bounds.Height);
            AssertScrollEndpoints(host, scroll);
            if (height >= 90)
            {
                foreach (var name in new[] { "TimelineTrackNameInput", "ConfirmTrackRenameButton", "CancelTrackRenameButton" })
                {
                    var control = UiTestActions.Find<Control>(host, name);
                    control.BringIntoView();
                    Flush(host);
                    AssertFits(control, Presenter(scroll));
                }
            }

            SetPanelHeight(host, panel, 360);
            Assert.Equal(normalHeight, frame.Bounds.Height, 6);
            Assert.True(frame.Bounds.Height < slot.Bounds.Height);
            Assert.InRange(scroll.Offset.Y, 0, 1);
            Assert.Same(originalDocument, context.Session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Timeline.CancelTrackRenameCommand.Execute(null);
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ANewDirectToolbarTargetSupersedesAPendingNestedScrollRequest()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var panel = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        var host = Float(context, WorkbenchPanelIds.TIMELINE, 420);
        try
        {
            SetPanelHeight(host, panel, 50);
            var outer = UiTestActions.Find<OverflowScrollViewer>(host, "TimelineToolbarOverflowScroller");
            var inner = UiTestActions.Find<ScrollViewer>(host, "TimelineToolbarScroller");
            inner.Offset = new(0, inner.Extent.Height);
            Flush(host);
            UiTestActions.Find<Control>(host, "TimelineSnapButton").BringIntoView();
            var latest = UiTestActions.Find<Control>(host, "TimelineTimingPostProcessorButton");
            latest.BringIntoView();
            Flush(host);
            AssertFits(latest, Presenter(outer));
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static WorkbenchFloatingHostWindow Float(MainWindowTestContext context, string panelId, double width)
    {
        context.Window.Layouts.Float(panelId);
        Dispatcher.UIThread.RunJobs();
        var host = Assert.IsType<WorkbenchFloatingHostWindow>(Assert.Single(context.Window.Layouts.FloatingWindows));
        host.Width = width;
        host.Height = 450;
        Flush(host);
        return host;
    }

    private static void SetPanelHeight(WorkbenchFloatingHostWindow host, Control panel, double height)
    {
        var chromeHeight = host.DockHost.Bounds.Height - panel.Bounds.Height;
        host.DockHost.VerticalAlignment = VerticalAlignment.Top;
        host.DockHost.Height = height + chromeHeight;
        Flush(host);
        Assert.InRange(Math.Abs(panel.Bounds.Height - height), 0, 1);
    }

    private static void AssertViewportFits(ScrollViewer scroll, Control panel, double height)
    {
        Assert.InRange(scroll.Viewport.Height, 1, height + 1);
        AssertFits(scroll, panel);
    }

    private static void AssertScrollEndpoints(Window host, ScrollViewer scroll)
    {
        var presenter = Presenter(scroll);
        var content = Assert.IsAssignableFrom<Control>(scroll.Content);
        scroll.Offset = default;
        Flush(host);
        var top = content.TranslatePoint(default, presenter)!.Value.Y - content.Margin.Top;
        Assert.InRange(top, 0, presenter.Bounds.Height);
        scroll.Offset = new(0, scroll.Extent.Height);
        Flush(host);
        var bottom = content.TranslatePoint(new(0, content.Bounds.Height), presenter)!.Value.Y + content.Margin.Bottom;
        Assert.InRange(bottom, 1, presenter.Bounds.Height + 1);
        Assert.InRange(Math.Abs(scroll.Offset.Y - Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)), 0, 1);
    }

    private static Rect BoundsIn(Control control, Visual relativeTo)
    {
        return new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);
    }

    private static void AssertFits(Control control, Control container)
    {
        var bounds = BoundsIn(control, container);
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.InRange(bounds.Left, -0.001, container.Bounds.Width - bounds.Width + 0.001);
        Assert.InRange(bounds.Top, -0.001, container.Bounds.Height - bounds.Height + 0.001);
    }

    private static ScrollContentPresenter Presenter(ScrollViewer scroll)
    {
        return scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First(presenter =>
            ReferenceEquals(presenter.GetVisualAncestors().OfType<ScrollViewer>().First(), scroll));
    }

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
