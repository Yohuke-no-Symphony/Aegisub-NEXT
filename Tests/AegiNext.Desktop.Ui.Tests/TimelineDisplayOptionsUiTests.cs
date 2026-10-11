using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Analysis;
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

public sealed class TimelineDisplayOptionsUiTests
{
    [AvaloniaFact]
    public async Task AudioVisibilityButtonsAreIndependentAndSurviveFloatingTheFixedPanel()
    {
        await using var context = new MainWindowTestContext();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var originalViewport = timeline.Viewport;
        var originalPosition = timeline.Position;
        var originalDocument = context.Session.DocumentSnapshot;
        Assert.Equal(TopLevel.GetTopLevel(timeline)!.RenderScaling, context.ViewModel.Timeline.RenderScaling);
        UiTestActions.Click(context.Window, "TimelineSpectrumButton");
        Assert.False(context.ViewModel.Timeline.IsSpectrumVisible);
        Assert.False(timeline.IsSpectrumVisible);
        Assert.True(timeline.IsWaveformVisible);
        UiTestActions.Click(context.Window, "TimelineWaveformButton");
        Assert.False(context.ViewModel.Timeline.IsWaveformVisible);
        Assert.False(timeline.IsWaveformVisible);
        Assert.Equal(originalViewport, timeline.Viewport);
        Assert.Equal(originalPosition, timeline.Position);
        Assert.Same(originalDocument, context.Session.DocumentSnapshot);
        context.Window.Layouts.Float(WorkbenchPanelIds.TIMELINE);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        floating.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        floating.UpdateLayout();
        var panel = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        Assert.Same(timeline, panel.FindControl<SubtitleTimelineControl>("Timeline"));
        Assert.Same(floating, TopLevel.GetTopLevel(timeline));
        Assert.Equal(floating.RenderScaling, context.ViewModel.Timeline.RenderScaling);
        Assert.False(timeline.IsSpectrumVisible);
        Assert.False(timeline.IsWaveformVisible);
        var button = panel.FindControl<Button>("TimelineSpectrumButton")!;
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        floating.UpdateLayout();
        var buttonPoint = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), floating)!.Value;
        floating.MouseDown(buttonPoint, MouseButton.Left);
        floating.MouseUp(buttonPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(timeline.IsSpectrumVisible);
        Assert.False(timeline.IsWaveformVisible);
        Assert.Same(originalDocument, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public void HiddenAudioLayersKeepTheirAnalysisAndRenderIndependentlyWhenRestored()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = new SubtitleTimelineControl();
        var window = new Window { Width = 520, Height = 220, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            timeline.PixelsPerSecond = (timeline.Bounds.Width - timeline.HeaderWidth) / 10;
            timeline.SetSpectrogram(new SpectrogramData(3, 1, new MediaTime(10), [255, 255, 255],
                [0, 0, -0.5f, 0.5f, 0, 0]));
            timeline.SetAudioGraphPalette(new() { Waveform = "#F01234FF" });
            var viewport = timeline.Viewport;
            var graphPoint = new Point(timeline.HeaderWidth + timeline.Viewport.Width * 0.73,
                timeline.RulerHeight + timeline.Viewport.Height * 0.75);
            var wavePoint = new Point(timeline.HeaderWidth + timeline.Viewport.Width / 3,
                timeline.RulerHeight + timeline.Viewport.Height * 0.6);
            using var both = Capture(timeline);
            timeline.IsSpectrumVisible = false;
            using var waveOnly = Capture(timeline);
            Assert.NotEqual(Pixel(both, graphPoint), Pixel(waveOnly, graphPoint));
            Assert.True(HasRed(waveOnly, wavePoint));
            timeline.IsWaveformVisible = false;
            using var neither = Capture(timeline);
            Assert.False(HasRed(neither, wavePoint));
            timeline.IsSpectrumVisible = true;
            using var spectrumOnly = Capture(timeline);
            Assert.Equal(Pixel(both, graphPoint), Pixel(spectrumOnly, graphPoint));
            Assert.False(HasRed(spectrumOnly, wavePoint));
            timeline.IsWaveformVisible = true;
            using var restored = Capture(timeline);
            Assert.Equal(Pixel(both, graphPoint), Pixel(restored, graphPoint));
            Assert.True(HasRed(restored, wavePoint));
            Assert.Equal(viewport, timeline.Viewport);
            Assert.Equal(MediaTime.Zero, timeline.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SnapFrameUsesTheCommittedBoundaryAndClearsOnAltAndRelease()
    {
        var moving = new SubtitleLine { Start = new(1), End = new(2), Text = "Moving" };
        var otherTrack = new ProjectTrack { Name = "Other" };
        var neighbor = new SubtitleLine { Start = new(301, 100), End = new(4), Text = "Boundary" };
        var layer = Layer(moving);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 100, IsSnapEnabled = true };
        timeline.SetDocument(new()
        {
            Tracks = [ProjectTrack.Default, otherTrack], Subtitles = [moving, neighbor], Layers = [layer, Layer(neighbor) with { TrackId = otherTrack.Id }]
        }, moving.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = new Window { Width = 700, Height = 240, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            var origin = timeline.GetClipRectangle(layer.Id)!.Value.Center;
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Vector(103.3, 0));
            Assert.Equal(neighbor.Start, timeline.SnapTarget);
            var framePoint = new Point(timeline.HeaderWidth + 3.01 * 100 - 2, 180);
            using var snapped = Capture(timeline);
            window.MouseMove(origin + new Vector(103.3, 0), RawInputModifiers.Alt);
            Assert.Null(timeline.SnapTarget);
            using var bypassed = Capture(timeline);
            Assert.NotEqual(Pixel(snapped, framePoint), Pixel(bypassed, framePoint));
            window.MouseMove(origin + new Vector(103.3, 0));
            Assert.Equal(neighbor.Start, timeline.SnapTarget);
            window.MouseUp(origin + new Vector(103.3, 0), MouseButton.Left);
            Assert.Null(timeline.SnapTarget);
            Assert.NotNull(committed);
            Assert.Equal(neighbor.Start, committed.End);
            Assert.Equal(new MediaTime(1), committed.End - committed.Start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThemeChangeAdaptsBuiltInAudioAndCurvesWithoutChangingCustomColorsOrTime()
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "中文 / TEST 123" };
        var layer = Layer(cue) with { Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.25), new(new(2), 0.75)])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 50 };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, new(AnimationProperty.OPACITY))] };
        timeline.SetSpectrogram(new SpectrogramData(2, 1, new MediaTime(10), [0, 255], [0, 0, 0, 0]));
        var window = new Window { Width = 600, Height = 300, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = timeline.Viewport;
            var sample = new Point(timeline.HeaderWidth + 350, 230);
            using var dark = Capture(timeline);
            window.RequestedThemeVariant = ThemeVariant.Light;
            using var light = Capture(timeline);
            Assert.NotEqual(Pixel(dark, sample), Pixel(light, sample));
            var firstKey = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, MediaTime.Zero, 0.25)!.Value;
            var lastKey = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(2), 0.75)!.Value;
            var curvePoint = new Point((firstKey.X + lastKey.X) / 2, (firstKey.Y + lastKey.Y) / 2);
            var curvePixel = Pixel(light, curvePoint);
            var curveBackground = Pixel(light, curvePoint + new Vector(0, 6));
            Assert.True(Math.Abs(curvePixel.Red - curveBackground.Red) + Math.Abs(curvePixel.Green - curveBackground.Green) +
                Math.Abs(curvePixel.Blue - curveBackground.Blue) > 80);
            timeline.IsSpectrumVisible = false;
            timeline.IsWaveformVisible = false;
            using var lightWithoutAudio = Capture(timeline);
            Assert.True(Pixel(lightWithoutAudio, new(timeline.HeaderWidth + 410, 280)).Red > 220);
            timeline.IsSpectrumVisible = true;
            timeline.IsWaveformVisible = true;
            Assert.Equal(viewport, timeline.Viewport);
            var custom = new AudioGraphPalette { UseClassicSpectrum = false, Low = "#FFF0E0", Mid = "#E8BFA2", High = "#B25A36", Waveform = "#40609080" };
            timeline.SetAudioGraphPalette(custom);
            using var customLight = Capture(timeline);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            using var customDark = Capture(timeline);
            Assert.Equal(Pixel(customLight, sample), Pixel(customDark, sample));
            Assert.Equal(viewport, timeline.Viewport);
            Assert.Equal(MediaTime.Zero, timeline.Position);
            Assert.Equal(new MediaTime(1), layer.Start);
            CaptureFile(window, "timeline-theme-dark.png");
            window.RequestedThemeVariant = ThemeVariant.Light;
            timeline.SetAudioGraphPalette(new());
            CaptureFile(window, "timeline-theme-light.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static SKBitmap Capture(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static SKColor Pixel(SKBitmap bitmap, Point point) => bitmap.GetPixel((int)point.X, (int)point.Y);

    private static bool HasRed(SKBitmap bitmap, Point point)
    {
        for (var offset = -1; offset <= 1; offset++)
        {
            var pixel = bitmap.GetPixel((int)point.X + offset, (int)point.Y);
            if (pixel.Red > 150 && pixel.Green < 100 && pixel.Blue < 120)
            {
                return true;
            }
        }
        return false;
    }

    private static void CaptureFile(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
