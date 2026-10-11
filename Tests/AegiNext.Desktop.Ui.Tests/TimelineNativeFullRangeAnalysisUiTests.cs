using AegiNext.Media.Processes;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;
using AegiNext.Media.Probing;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证真实原生压缩音频经工作区打开、全片缩放和高 DPI 绘制的完整流程。</summary>
public sealed class TimelineNativeFullRangeAnalysisUiTests
{
    private static JsonSerializerOptions CaptureJsonOptions { get; } = new() { WriteIndented = true };

    /// <summary>44.1 kHz AAC 在真实分析会话中缩到五分钟全片后，两层均覆盖有效媒体像素。</summary>
    [AvaloniaFact]
    public async Task FiveMinuteAacZoomsOutThroughTheRealSessionAndRendersBothLayersAtTwoTimesScaling()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AEGINEXT_RUN_AUDIO_TESTS") == "1",
            "真实压缩音频分析需要当前原生库和 AEGINEXT_FFMPEG_PATH。");
        var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.True(Path.IsPathFullyQualified(executable));
        var path = Path.Combine(Path.GetTempPath(), $"aeginext-full-range-ui-{Guid.NewGuid():N}.m4a");
        try
        {
            var encoded = await MediaToolProcess.RunAsync(executable,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=6000:sample_rate=44100:duration=300",
                    "-c:a", "aac", "-b:a", "128k", "-y", path], TimeSpan.FromSeconds(60), 65536, 65536,
                TestContext.Current.CancellationToken);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);
            await VerifyAsync(path, new(0, MediaTime.Zero, new(300), 0, VideoWidth: 1, VideoHeight: 1), true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>按显式素材路径验证实际流元数据、原生分析与全片缩放的结果发布。</summary>
    [AvaloniaFact]
    public async Task SuppliedCompressedVideoPublishesTheWholeMediaThroughTheRealSession()
    {
        var path = Environment.GetEnvironmentVariable("AEGINEXT_FULL_RANGE_MEDIA_PATH");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AEGINEXT_RUN_AUDIO_TESTS") == "1" &&
            !string.IsNullOrWhiteSpace(path), "原始素材验证需要 AEGINEXT_FULL_RANGE_MEDIA_PATH 与原生音频库。");
        Assert.True(Path.IsPathFullyQualified(path));
        var media = await VideoPreviewProbe.ProbeAsync(path, TestContext.Current.CancellationToken);
        await VerifyAsync(path, media, false);
    }

    private static async Task VerifyAsync(string path, VideoPreviewMedia media, bool constantTone)
    {
        var duration = Assert.IsType<MediaTime>(media.Duration);
        var seconds = (double)duration.Numerator / duration.Denominator;
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, (long)Math.Ceiling(seconds * 1000));
        await using var context = new MainWindowTestContext(
            (_, _, initial, _) => Task.FromResult(new AudioPlaybackSession(playback, output, initial)),
            videoSourceFactory: () => video, mediaProbe: (_, _) => Task.FromResult(media));
        context.Window.Width = 1464;
        context.Window.Height = 650;
        context.Window.SetRenderScaling(2);
        var pane = Assert.IsAssignableFrom<IToolDock>(context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.TIMELINE].Owner);
        var split = Assert.IsAssignableFrom<IProportionalDock>(pane.Owner);
        Assert.NotNull(split.VisibleDockables);
        var siblings = split.VisibleDockables.Where(value => value is not ISplitter && !ReferenceEquals(value, pane)).ToArray();
        var remaining = siblings.Sum(value => value.Proportion);
        foreach (var sibling in siblings)
        {
            sibling.Proportion = 0.4 * sibling.Proportion / remaining;
        }
        pane.Proportion = 0.6;
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        await context.Window.OpenMediaAsync(path, false);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with
        {
            StartSeconds = seconds / 2,
            PixelsPerSecond = timeline.Viewport.Width / 8
        };
        model.SuspendPlaybackFollow();
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(string.IsNullOrEmpty(model.AnalysisStatus), model.AnalysisStatus);
        Assert.NotNull(model.Waveform);
        Assert.NotNull(model.Spectrogram);
        var document = context.Session.DocumentSnapshot;
        var position = context.Session.ProjectPosition;
        var playbackSeeks = playback.SeekCount;
        var videoSeeks = video.SeekCount;
        var pointer = timeline.TranslatePoint(new Point(timeline.HeaderWidth + timeline.Viewport.Width / 2,
            timeline.RulerHeight + timeline.Viewport.Height * 0.85), context.Window)!.Value;
        var wheelEvents = 0;
        while (timeline.VisibleDuration < seconds)
        {
            Assert.True(wheelEvents++ < 40);
            context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
        }
        var plan = Assert.IsType<WaveformViewportPlan>(WaveformViewportPlanner.Create(timeline.Viewport, 2, duration));
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken);
        Assert.True(string.IsNullOrEmpty(model.AnalysisStatus), model.AnalysisStatus);
        var waveform = Assert.IsType<WaveformData>(model.Waveform);
        var spectrum = Assert.IsType<SpectrogramData>(model.Spectrogram);
        Assert.Equal(plan.Analysis, waveform.Request);
        Assert.Equal(MediaTime.Zero, waveform.Start);
        Assert.True(waveform.End >= duration && spectrum.Start <= MediaTime.Zero && spectrum.End >= duration);
        for (var quarter = 0; quarter < 4; quarter++)
        {
            var first = waveform.BucketCount * quarter / 4;
            var after = waveform.BucketCount * (quarter + 1) / 4;
            Assert.Contains(waveform.Peaks.Slice(first * 2, (after - first) * 2).ToArray(), value => Math.Abs(value) > 0.00001F);
        }
        Assert.Equal(position, context.Session.ProjectPosition);
        Assert.Equal(playbackSeeks, playback.SeekCount);
        Assert.Equal(videoSeeks, video.SeekCount);
        Assert.Same(document, context.Session.DocumentSnapshot);
        timeline.SetAudioGraphPalette(new()
        {
            UseClassicSpectrum = false, AdaptToTheme = false,
            Low = "#000000", Mid = "#0000FF", High = "#0000FF", Waveform = "#FF0000FF"
        });
        using var image = Capture(timeline);
        var firstPixel = (int)Math.Ceiling((timeline.HeaderWidth + 3) * 2);
        var lastPixel = (int)Math.Floor((timeline.HeaderWidth + seconds * timeline.PixelsPerSecond - 3) * 2);
        var waveformTop = Math.Max(0, (int)Math.Floor((timeline.RulerHeight + timeline.Viewport.Height * 0.1) * 2) - 1);
        var waveformBottom = Math.Min(image.Height, (int)Math.Ceiling((timeline.RulerHeight + timeline.Viewport.Height * 0.9) * 2) + 1);
        var spectrumTop = (int)Math.Ceiling((timeline.RulerHeight + 1) * 2);
        var spectrumBottom = (int)Math.Floor((timeline.RulerHeight + timeline.Viewport.Height * 0.18) * 2);
        var waveformColumns = 0;
        var spectrumColumns = 0;
        Assert.True(waveformBottom > waveformTop);
        for (var x = firstPixel; x < lastPixel; x++)
        {
            for (var y = waveformTop; y < waveformBottom; y++)
            {
                var color = image.GetPixel(x, y);
                if (color.Red > color.Green + 64 && color.Red > color.Blue + 64)
                {
                    waveformColumns++;
                    break;
                }
            }
            for (var y = spectrumTop; y < spectrumBottom; y++)
            {
                var color = image.GetPixel(x, y);
                if (color.Blue > 180 && color.Red < 80 && color.Green < 80)
                {
                    spectrumColumns++;
                    break;
                }
            }
        }
        Save(path, timeline, image, waveform, spectrum, wheelEvents, lastPixel - firstPixel, waveformColumns, spectrumColumns);
        Assert.True(waveformColumns >= (lastPixel - firstPixel) * 0.95,
            $"全片波形仅覆盖 {waveformColumns}/{lastPixel - firstPixel} 个像素列。");
        if (constantTone)
        {
            Assert.True(spectrumColumns >= (lastPixel - firstPixel) * 0.95,
                $"五分钟 AAC 的频谱仅覆盖 {spectrumColumns}/{lastPixel - firstPixel} 个像素列。");
        }
    }

    private static SKBitmap Capture(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(timeline.Bounds.Width * 2),
            (int)Math.Ceiling(timeline.Bounds.Height * 2)));
        using (var drawing = target.CreateDrawingContext())
        {
            using var transform = drawing.PushTransform(Matrix.CreateScale(2, 2));
            timeline.Render(drawing);
        }
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Save(string path, SubtitleTimelineControl timeline, SKBitmap image, WaveformData waveform,
        SpectrogramData spectrum, int wheelEvents, int columns, int waveformColumns, int spectrumColumns)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        var name = "native-full-range-" + Path.GetFileNameWithoutExtension(path);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoded.SaveTo(output);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            Media = path, Scaling = 2, Viewport = timeline.Viewport, WaveformStart = waveform.Start,
            WaveformEnd = waveform.End, SpectrumStart = spectrum.Start, SpectrumEnd = spectrum.End,
            WaveformBuckets = waveform.BucketCount, SpectrumColumns = spectrum.Width,
            WheelEvents = wheelEvents, PixelColumns = columns, WaveformPixelColumns = waveformColumns,
            SpectrumPixelColumns = spectrumColumns
        }, CaptureJsonOptions));
    }
}
