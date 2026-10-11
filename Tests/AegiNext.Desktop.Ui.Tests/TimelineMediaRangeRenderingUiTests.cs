using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证视频有效范围的实际背景像素与媒体时长同步。</summary>
public sealed class TimelineMediaRangeRenderingUiTests
{
    /// <summary>视频底色只改变实际视频范围，后段工程内容保持原背景。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaDurationChangesOnlyTheBackgroundWithinTheVideo(bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT
        });
        var model = context.ViewModel.Timeline;
        model.IsSpectrumVisible = false;
        model.IsWaveformVisible = false;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, timeline.ActualThemeVariant);
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = 60 };
        var viewport = timeline.Viewport;
        using var before = Capture(timeline);
        model.MediaDuration = 4.25;
        using var after = Capture(timeline);

        Assert.NotEqual(Pixel(before, timeline, 2.37), Pixel(after, timeline, 2.37));
        Assert.Equal(Pixel(before, timeline, 5.37), Pixel(after, timeline, 5.37));
        Assert.Equal(viewport, timeline.Viewport);
        Assert.Equal(before.GetPixel(20, 10), after.GetPixel(20, 10));
        Assert.Equal(before.GetPixel(TimeX(timeline, 2.37), 10), after.GetPixel(TimeX(timeline, 2.37), 10));
    }

    /// <summary>自定义 RGBA 只与视频范围内的背景混合，透明颜色可关闭底色。</summary>
    [AvaloniaTheory]
    [InlineData("#336699FF", 255)]
    [InlineData("#33669980", 128)]
    [InlineData("#33669900", 0)]
    public void ConfiguredMediaRangeColorUsesRgbaOverTheExistingBackground(string color, int alpha)
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var window = CreateWindow(timeline);
        try
        {
            timeline.SetViewport(new(0, 60), 10);
            timeline.SetClipPalette(Palette("#33669900"));
            timeline.SetMediaDuration(4.25);
            using var before = Capture(timeline);
            var viewport = timeline.Viewport;
            timeline.SetClipPalette(Palette(color));
            using var after = Capture(timeline);

            AssertColor(Blend(new(51, 102, 153), Pixel(before, timeline, 2.37), alpha),
                Pixel(after, timeline, 2.37));
            Assert.Equal(Pixel(before, timeline, 5.37), Pixel(after, timeline, 5.37));
            Assert.Equal(viewport, timeline.Viewport);
            Assert.Equal(before.GetPixel(20, 40), after.GetPixel(20, 40));
            Assert.Equal(before.GetPixel(TimeX(timeline, 2.37), 10), after.GetPixel(TimeX(timeline, 2.37), 10));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>同一快照和视口中缩短或清除媒体会立即移除旧缓存中的底色。</summary>
    [AvaloniaFact]
    public void ShorteningAndClearingMediaInvalidateTheBackgroundWithoutChangingTheViewport()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var window = CreateWindow(timeline);
        try
        {
            timeline.SetViewport(new(0, 60), 120);
            timeline.SetClipPalette(Palette("#336699FF"));
            using var empty = Capture(timeline);
            var viewport = timeline.Viewport;
            timeline.SetMediaDuration(10);
            using var longer = Capture(timeline);
            Assert.Equal(new SKColor(51, 102, 153), Pixel(longer, timeline, 6.37));

            timeline.SetMediaDuration(4);
            using var shorter = Capture(timeline);
            Assert.Equal(Pixel(empty, timeline, 6.37), Pixel(shorter, timeline, 6.37));
            Assert.Equal(Pixel(longer, timeline, 2.37), Pixel(shorter, timeline, 2.37));
            Assert.Equal(viewport, timeline.Viewport);

            timeline.SetMediaDuration(0);
            using var cleared = Capture(timeline);
            Assert.Equal(Pixel(empty, timeline, 2.37), Pixel(cleared, timeline, 2.37));
            Assert.Equal(Pixel(empty, timeline, 6.37), Pixel(cleared, timeline, 6.37));
            Assert.Equal(viewport, timeline.Viewport);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>平移视口只显示与视频范围相交的部分，滚出视频后不残留底色。</summary>
    [AvaloniaFact]
    public void HorizontalNavigationClipsMediaColorToItsRelativeTimeRange()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var window = CreateWindow(timeline);
        try
        {
            timeline.SetClipPalette(Palette("#336699FF"));
            timeline.SetViewport(new(3, 60), 120);
            using var empty = Capture(timeline);
            var background = Pixel(empty, timeline, 4.87);
            timeline.SetMediaDuration(4.25);
            using var intersecting = Capture(timeline);
            Assert.Equal(new SKColor(51, 102, 153), Pixel(intersecting, timeline, 3.37));
            Assert.Equal(Pixel(empty, timeline, 4.87), Pixel(intersecting, timeline, 4.87));

            timeline.SetViewport(timeline.Viewport with { StartSeconds = 6 }, 120);
            using var beyond = Capture(timeline);
            Assert.Equal(background, Pixel(beyond, timeline, 6.37));

            timeline.SetViewport(timeline.Viewport with { StartSeconds = 0 }, 120);
            using var restored = Capture(timeline);
            Assert.Equal(new SKColor(51, 102, 153), Pixel(restored, timeline, 2.37));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>默认视频底色跟随主题更新，并能在切回原主题时复用相同视觉结果。</summary>
    [AvaloniaFact]
    public void DefaultMediaRangeAndSurfaceRefreshWhenTheThemeChanges()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var window = CreateWindow(timeline);
        try
        {
            timeline.SetViewport(new(0, 60), 10);
            timeline.SetMediaDuration(4.25);
            using var light = Capture(timeline);
            Assert.Equal(ThemeVariant.Light, timeline.ActualThemeVariant);
            Assert.Equal(new SKColor(242, 245, 250), Pixel(light, timeline, 5.37));
            Assert.NotEqual(Pixel(light, timeline, 5.37), Pixel(light, timeline, 2.37));

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Flush(window);
            using var dark = Capture(timeline);
            Assert.Equal(ThemeVariant.Dark, timeline.ActualThemeVariant);
            Assert.Equal(new SKColor(19, 26, 38), Pixel(dark, timeline, 5.37));
            Assert.NotEqual(Pixel(dark, timeline, 5.37), Pixel(dark, timeline, 2.37));
            Assert.NotEqual(Pixel(light, timeline, 2.37), Pixel(dark, timeline, 2.37));

            window.RequestedThemeVariant = ThemeVariant.Light;
            Flush(window);
            using var restored = Capture(timeline);
            Assert.Equal(Pixel(light, timeline, 2.37), Pixel(restored, timeline, 2.37));
            Assert.Equal(Pixel(light, timeline, 5.37), Pixel(restored, timeline, 5.37));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>底色可见于不透明频谱之上，同时保留频谱明暗、波形、clip 和网格前景。</summary>
    [AvaloniaFact]
    public void MediaRangeRemainsVisibleOverOpaqueSpectrumAndBelowWaveformClipsAndGrid()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "Subtitle" };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        timeline.SetAudioGraphPalette(new()
        {
            AdaptToTheme = false, UseClassicSpectrum = false,
            Low = "#101010", Mid = "#808080", High = "#E0E0E0", Waveform = "#FF0000FF"
        });
        timeline.SetSpectrogram(new(10, 1, new(10),
            Enumerable.Range(0, 10).Select(index => (byte)(index % 2 == 0 ? 0 : 255)).ToArray(), []));
        var waveform = new WaveformData(new(MediaTime.Zero, 8192, 64),
            Enumerable.Range(0, 128).Select(index => index % 2 == 0 ? -0.25f : 0.25f).ToArray());
        timeline.SetWaveform(waveform, null, new(10));
        timeline.IsSpectrumVisible = true;
        timeline.IsWaveformVisible = true;
        var window = CreateWindow(timeline);
        try
        {
            timeline.SetViewport(new(0.5 / 60, 60), 10);
            var palette = Palette("#33669900") with
            {
                SelectedClip = "#00FF00FF", SelectedRangeFill = "#00000000",
                InactiveRangeFill = "#00000000", StartLine = "#00000000", EndLine = "#00000000"
            };
            timeline.SetClipPalette(palette);
            timeline.SetMediaDuration(4.25);
            using var before = Capture(timeline);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var clipX = (int)clip.Left + 20;
            var clipY = (int)clip.Bottom - 5;
            var waveY = (int)(timeline.RulerHeight + (timeline.Bounds.Height - timeline.RulerHeight) / 2);
            var gridX = TimeX(timeline, 4);

            timeline.SetClipPalette(palette with { MediaRangeFill = "#33669980" });
            using var after = Capture(timeline);

            AssertColor(Blend(new(51, 102, 153), Pixel(before, timeline, 2.37), 128), Pixel(after, timeline, 2.37));
            Assert.NotEqual(Pixel(before, timeline, 2.37), Pixel(after, timeline, 2.37));
            Assert.NotEqual(Pixel(after, timeline, 2.37), Pixel(after, timeline, 3.37));
            Assert.Equal(Pixel(before, timeline, 5.37), Pixel(after, timeline, 5.37));
            Assert.Equal(new SKColor(255, 0, 0), after.GetPixel(TimeX(timeline, 3.37), waveY));
            Assert.Equal(before.GetPixel(TimeX(timeline, 3.37), waveY), after.GetPixel(TimeX(timeline, 3.37), waveY));
            Assert.Equal(new SKColor(0, 255, 0), after.GetPixel(clipX, clipY));
            Assert.Equal(before.GetPixel(clipX, clipY), after.GetPixel(clipX, clipY));
            Assert.Equal(new SKColor(196, 206, 220), after.GetPixel(gridX, (int)timeline.Bounds.Height - 15));
            Assert.Equal(before.GetPixel(gridX, (int)timeline.Bounds.Height - 15),
                after.GetPixel(gridX, (int)timeline.Bounds.Height - 15));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>仅有长字幕不会产生视频范围，短视频加载后也不会扩大到字幕末尾。</summary>
    [AvaloniaFact]
    public async Task LongerSubtitleContentDoesNotInventOrExtendTheVideoBackground()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = WorkbenchTheme.LIGHT, TimelineClips = Palette("#336699FF")
        });
        var model = context.ViewModel.Timeline;
        model.IsSpectrumVisible = false;
        model.IsWaveformVisible = false;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = 60 };
        using var empty = Capture(timeline);
        context.Session.Editor.AddSubtitle(new(110), new(120), "Later subtitle");
        using var subtitles = Capture(timeline);
        Assert.Equal(120, model.FullDuration);
        Assert.Equal(0, model.MediaDuration);
        Assert.Equal(Pixel(empty, timeline, 2.37), Pixel(subtitles, timeline, 2.37));

        model.MediaDuration = 4.25;
        using var video = Capture(timeline);
        Assert.Equal(120, model.FullDuration);
        Assert.Equal(new SKColor(51, 102, 153), Pixel(video, timeline, 2.37));
        Assert.Equal(Pixel(subtitles, timeline, 5.37), Pixel(video, timeline, 5.37));
        Assert.Equal(new MediaTime(120), Assert.Single(context.Session.DocumentSnapshot.Subtitles).End);
    }

    /// <summary>非零媒体起点的视频仍从工程零点着色，而不是绝对媒体时间。</summary>
    [AvaloniaFact]
    public async Task MediaWithANonzeroStartUsesTheProjectRelativeBackgroundRange()
    {
        await using var context = new MainWindowTestContext(
            mediaProbe: (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(900), new(17, 4), VideoWidth: 1, VideoHeight: 1)),
            videoSourceFactory: () => new PreviewTestSource(1, 900000, 901000, 902000, 903000, 904000, 904250));
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = WorkbenchTheme.LIGHT, TimelineClips = Palette("#336699FF")
        });
        var model = context.ViewModel.Timeline;
        model.IsSpectrumVisible = false;
        model.IsWaveformVisible = false;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = 60 };
        context.Session.Editor.AddSubtitle(new(110), new(120), "Later subtitle");
        using var before = Capture(timeline);
        await context.OpenMediaAsync();
        using var after = Capture(timeline);

        Assert.Equal(new MediaTime(900), context.Controller.Snapshot.Start);
        Assert.Equal(MediaTime.Zero, context.Session.ProjectPosition);
        Assert.Equal(4.25, model.MediaDuration);
        Assert.Equal(new SKColor(51, 102, 153), Pixel(after, timeline, 2.37));
        Assert.Equal(Pixel(before, timeline, 5.37), Pixel(after, timeline, 5.37));
    }

    private static SubtitleTimelineControl CreateTimeline()
    {
        var timeline = new SubtitleTimelineControl { IsSpectrumVisible = false, IsWaveformVisible = false };
        timeline.SetDocument(new(), null, null);
        return timeline;
    }

    private static Window CreateWindow(SubtitleTimelineControl timeline)
    {
        var window = new Window { Width = 800, Height = 220, Content = timeline, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Flush(window);
        return window;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static TimelineClipPalette Palette(string mediaRangeFill) => new()
    {
        AdaptToTheme = false, MediaRangeFill = mediaRangeFill
    };

    private static SKColor Blend(SKColor foreground, SKColor background, int alpha)
    {
        var opacity = alpha / 255d;
        return new((byte)Math.Round(foreground.Red * opacity + background.Red * (1 - opacity)),
            (byte)Math.Round(foreground.Green * opacity + background.Green * (1 - opacity)),
            (byte)Math.Round(foreground.Blue * opacity + background.Blue * (1 - opacity)));
    }

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 1);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 1);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 1);
        Assert.Equal(expected.Alpha, actual.Alpha);
    }

    private static SKBitmap Capture(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static int TimeX(SubtitleTimelineControl timeline, double seconds) =>
        (int)(timeline.HeaderWidth + (seconds - timeline.ViewStart) * timeline.PixelsPerSecond);

    private static SKColor Pixel(SKBitmap image, SubtitleTimelineControl timeline, double seconds) =>
        image.GetPixel(TimeX(timeline, seconds), (int)timeline.Bounds.Height - 15);
}
