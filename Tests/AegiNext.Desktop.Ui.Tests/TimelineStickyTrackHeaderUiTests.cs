using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineStickyTrackHeaderUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TallTrackKeepsItsRenderedInformationAndButtonHitsAtTheViewportTop(bool styled, bool dark)
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(styled);
        var track = document.Tracks[0];
        using var timeline = CreateTimeline(document);
        var window = new Window
        {
            Width = 800, Height = 180, Content = timeline,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            Flush(window);
            var header = timeline.GetTrackHeaderRectangle(track.Id)!.Value;
            var expander = timeline.GetTrackExpanderRectangle(track.Id)!.Value;
            var solo = timeline.GetTrackSoloToggleRectangle(track.Id)!.Value;
            var badge = timeline.GetTrackStyleBadgeRectangle(track.Id);
            var originalClip = timeline.GetClipRectangle(document.Layers[0].Id)!.Value;
            using var before = Capture(timeline);

            timeline.SetViewport(timeline.Viewport with { VerticalOffset = 100 }, 20);
            Flush(window);

            Assert.Equal(header.Top - 100, timeline.GetTrackHeaderRectangle(track.Id)!.Value.Top);
            Assert.Equal(originalClip.Top - 100, timeline.GetClipRectangle(document.Layers[0].Id)!.Value.Top);
            Assert.Equal(expander, timeline.GetTrackExpanderRectangle(track.Id));
            Assert.Equal(solo, timeline.GetTrackSoloToggleRectangle(track.Id));
            Assert.Equal(badge, timeline.GetTrackStyleBadgeRectangle(track.Id));
            using var after = Capture(timeline);
            var contentHeight = styled ? 48 : 28;
            for (var y = (int)timeline.RulerHeight; y < timeline.RulerHeight + contentHeight; y++)
            {
                for (var x = 0; x < timeline.HeaderWidth; x++)
                {
                    Assert.Equal(before.GetPixel(x, y), after.GetPixel(x, y));
                }
            }

            Guid? soloRequest = null;
            TimelineTrackCollapseEventArgs? collapseRequest = null;
            var otherRequests = 0;
            timeline.TrackSoloRequested += (_, e) => soloRequest = e.TrackId;
            timeline.TrackCollapseRequested += (_, e) => collapseRequest = e;
            timeline.TrackSelected += (_, _) => otherRequests++;
            timeline.SeekRequested += (_, _) => otherRequests++;
            window.MouseMove(solo.Center);
            Assert.Equal(Localization.Get("Workbench.SoloSubtitleTrack"), ToolTip.GetTip(timeline));
            Click(window, timeline, solo.Center);
            Assert.Equal(track.Id, soloRequest);
            Click(window, timeline, expander.Center);
            Assert.NotNull(collapseRequest);
            Assert.Equal(track.Id, collapseRequest.Id);
            Assert.True(collapseRequest.IsCollapsed);
            Assert.Equal(0, otherRequests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsufficientRemainingTrackSpacePushesItsInformationOutBeforeTheNextHeader(bool styled)
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(styled);
        using var timeline = CreateTimeline(document);
        var window = new Window { Width = 800, Height = 180, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var first = document.Tracks[0].Id;
            var second = document.Tracks[1].Id;
            var height = timeline.GetTrackHeaderRectangle(first)!.Value.Height;
            var contentHeight = styled ? 48 : 28;
            var offset = height - contentHeight;
            timeline.SetViewport(timeline.Viewport with { VerticalOffset = offset }, 20);
            var pinned = timeline.GetTrackExpanderRectangle(first)!.Value;
            Assert.Equal(timeline.RulerHeight + 4, pinned.Top);
            var badge = timeline.GetTrackStyleBadgeRectangle(first);

            timeline.SetViewport(timeline.Viewport with { VerticalOffset = offset + 10 }, 20);
            Flush(window);

            Assert.Equal(pinned.Top - 10, timeline.GetTrackExpanderRectangle(first)!.Value.Top);
            Assert.Equal(pinned.Top - 10, timeline.GetTrackSoloToggleRectangle(first)!.Value.Top);
            var next = timeline.GetTrackHeaderRectangle(second)!.Value;
            if (badge is { } originalBadge)
            {
                var movedBadge = timeline.GetTrackStyleBadgeRectangle(first)!.Value;
                Assert.Equal(originalBadge.Top - 10, movedBadge.Top);
                Assert.True(movedBadge.Bottom <= next.Top);
            }
            Assert.Equal(next.Top + 4, timeline.GetTrackExpanderRectangle(second)!.Value.Top);

            timeline.SetViewport(timeline.Viewport with { VerticalOffset = height }, 20);
            Flush(window);
            Assert.True(timeline.GetTrackExpanderRectangle(first)!.Value.Bottom <= timeline.RulerHeight);
            Assert.Equal(timeline.RulerHeight + 4, timeline.GetTrackExpanderRectangle(second)!.Value.Top);
            Guid? selected = null;
            timeline.TrackSoloRequested += (_, e) => selected = e.TrackId;
            Click(window, timeline, timeline.GetTrackSoloToggleRectangle(second)!.Value.Center);
            Assert.Equal(second, selected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ViewportTooShortForTheInformationKeepsTheHeaderInNormalFlow()
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(true);
        using var timeline = CreateTimeline(document);
        var window = new Window { Width = 800, Height = 50, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            Assert.True(timeline.Bounds.Height - timeline.RulerHeight < 48);
            timeline.SetViewport(timeline.Viewport with { VerticalOffset = 40 }, 20);
            var header = timeline.GetTrackHeaderRectangle(document.Tracks[0].Id)!.Value;
            Assert.Equal(header.Top + 4, timeline.GetTrackExpanderRectangle(document.Tracks[0].Id)!.Value.Top);
            Assert.Equal(header.Top + 4, timeline.GetTrackSoloToggleRectangle(document.Tracks[0].Id)!.Value.Top);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument CreateDocument(bool styled)
    {
        var first = new ProjectTrack
        {
            Name = "轨道 Track 123", StylePresetId = styled ? Guid.NewGuid() : null,
            StylePresetName = styled ? "样式 Style 123" : null, DefaultStyle = styled ? new() : null
        };
        var second = new ProjectTrack { Name = "Next 下一轨" };
        var layer = new ProjectLayer
        {
            TrackId = first.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(10),
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(1), new ScenePoint(10, 20))]),
                new(AnimationProperty.SCALE, [new(new(1), new ScenePoint(1, 1))]),
                new(AnimationProperty.ROTATION, [new(new(1), 20)])
            ]
        };
        return new() { Tracks = [first, second], Layers = [layer, layer with { Id = Guid.NewGuid(), TrackId = second.Id }] };
    }

    private static SubtitleTimelineControl CreateTimeline(ProjectDocument document)
    {
        var timeline = new SubtitleTimelineControl { IsSpectrumVisible = false, IsWaveformVisible = false };
        timeline.SetDocument(document, null, null);
        timeline.TimelineViewState = TimelineEffectClipTestState.ExpandAll(document);
        return timeline;
    }

    private static void Click(Window window, SubtitleTimelineControl timeline, Point point)
    {
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static SKBitmap Capture(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        bitmap.Render(control);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
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
