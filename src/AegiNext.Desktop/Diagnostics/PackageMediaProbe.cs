using System.Runtime.InteropServices;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Diagnostics;

internal static class PackageMediaProbe
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string mediaPath, string reportPath)
    {
        mediaPath = Path.GetFullPath(mediaPath);
        reportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var runDirectory = Path.Combine(Path.GetDirectoryName(reportPath)!, "媒体 验收 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var checks = new List<string>();
        var failures = new List<string>();
        try
        {
            var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
            var info = await probe.ProbeAsync(mediaPath);
            var video = info.Asset.Streams.First(stream => stream.CodecType == "video" && stream.Video is not null);
            var audio = info.Asset.Streams.First(stream => stream.CodecType == "audio");
            var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: mediaPath);
            var cue = new SubtitleLine { Start = MediaTime.Zero, End = new(30), Text = "AegiNext 独立发布字幕验收" };
            var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End };
            layer = layer with { Tracks = EffectScriptCompiler.Compile(BuiltinEffectScripts.Get("pop-in").Script, layer, cue.Style) };
            var colorScript = EffectScriptParser.Parse("""
                effect "package-color" version 1
                short-clip compress
                segment intro fixed 500ms
                  at 0 fill rgba(1, 0.02, 0.05, 0.9) linear
                  at 1 fill rgba(0.02, 0.05, 1, 0.8) linear
                end
                segment hold flex 1
                  at 0 fill rgba(0.02, 0.05, 1, 0.8) hold
                  at 1 fill rgba(0.02, 0.05, 1, 0.8) hold
                end
                """);
            layer = layer with { Tracks = layer.Tracks.AddRange(EffectScriptCompiler.Compile(colorScript, layer, cue.Style)) };
            var document = new ProjectDocument
            {
                Width = video.Video!.Width!.Value, Height = video.Video.Height!.Value,
                Media = new(asset.Id, video.Index, audio.Index, MediaTime.Zero), Assets = [asset], Subtitles = [cue],
                Layers = [layer]
            };
            var roundTrip = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(document))!;
            var colorFrames = roundTrip.Layers[0].Tracks.Single(track => track.Property == AnimationProperty.FILL).Keyframes;
            if (colorFrames.Any(frame => !frame.Value.IsColor) || colorFrames[0].Value.Color != new SceneColor(1, 0.02, 0.05, 0.9))
            {
                throw new InvalidDataException("The package serializer did not preserve complete linear RGBA keyframes.");
            }
            checks.Add("Package serializer preserved complete linear RGBA keyframes alongside vector animation.");
            checks.Add("Package FFprobe opened the supplied media path.");
            using (var navigator = new VideoFrameNavigator(token => FfmpegVideoDecoder.Open(mediaPath, video.Index, token)))
            using (var converter = new SdrVideoConverter())
            using (var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(runDirectory)))
            {
                for (var index = 0; index < 3; index++)
                {
                    var target = new MediaTime(index, 2);
                    using var decoded = navigator.SeekFrame(target) ?? throw new InvalidDataException("No decoded video frame.");
                    var background = converter.Convert(decoded.Frame);
                    var composed = renderer.ComposePreview(document, target, background.Pixels.Span, background.Width,
                        background.Height, background.Width * 4, background.Width, background.Height);
                    if (composed.Length != background.Width * background.Height * 4)
                    {
                        throw new InvalidDataException("Invalid preview frame size.");
                    }
                    if (!ContainsOverlay(composed, background.Pixels.Span))
                    {
                        throw new InvalidDataException("The subtitle did not change the preview pixels.");
                    }
                }
            }
            checks.Add("Package decode, SDR conversion and DSL vector/color-animated subtitle composition completed at three seek positions.");
            using (var decoder = FfmpegAudioDecoder.Open(mediaPath, audio.Index))
            using (var output = new SdlAudioOutput())
            {
                var prefilled = 0;
                while (prefilled < 6000)
                {
                    var block = decoder.Read() ?? throw new InvalidDataException("No decoded audio samples.");
                    output.Write(block.Samples.Span);
                    prefilled += block.FrameCount;
                }
                var queued = output.QueuedFrames;
                output.SetPaused(false);
                var started = Stopwatch.StartNew();
                while (output.QueuedFrames >= queued && started.Elapsed < TimeSpan.FromSeconds(3))
                {
                    await Task.Delay(20);
                }
                if (queued <= 0 || output.QueuedFrames >= queued)
                {
                    throw new InvalidDataException("The real SDL audio device did not consume queued PCM.");
                }
                output.SetPaused(true);
                output.Clear();
            }
            checks.Add("Package audio DLL loaded, decoded PCM and the real SDL device consumed the playback queue.");
            var exporter = new VideoExporter();
            var exportPath = Path.Combine(runDirectory, "字幕 成片.mp4");
            var request = new VideoExportRequest(document, runDirectory, exportPath)
            {
                Codec = VideoCodec.H264, Preset = "ultrafast", AudioMode = AudioExportMode.Copy
            };
            var result = await exporter.ExportAsync(request);
            var encoded = await probe.ProbeAsync(result.OutputPath);
            if (result.Frames == 0 || !encoded.Asset.Streams.Any(stream => stream.CodecType == "audio"))
            {
                throw new InvalidDataException("The package worker did not produce video and copied audio.");
            }
            checks.Add($"Package worker exported {result.Frames} DSL vector/color-animated subtitle frames and preserved audio.");
            VerifyExportedColor(document, mediaPath, video.Index, result.OutputPath,
                encoded.Asset.Streams.First(stream => stream.CodecType == "video").Index, runDirectory);
            checks.Add("Decoded worker output matched the animated blue subtitle rather than the red starting color.");
            using var cancellation = new CancellationTokenSource();
            var cancelPath = Path.Combine(runDirectory, "取消 成片.mp4");
            try
            {
                await exporter.ExportAsync(request with { OutputPath = cancelPath }, new CancelExportProbeProgress(cancellation), cancellation.Token);
                throw new InvalidDataException("Export completed without observing cancellation.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (File.Exists(cancelPath) || Directory.EnumerateDirectories(runDirectory, ".aeginext-export-*").Any())
                {
                    throw new InvalidDataException("Cancelled export retained a partial output or temporary directory.");
                }
            }
            checks.Add("Cancellation terminated the package worker and removed its partial output and temporary directory.");
        }
        catch (Exception error)
        {
            failures.Add(error.ToString());
        }
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            Version = typeof(PackageMediaProbe).Assembly.GetName().Version!.ToString(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), BaseDirectory = AppContext.BaseDirectory,
            Media = mediaPath, RunDirectory = runDirectory, Completed = failures.Count == 0, Checks = checks, Failures = failures
        }, jsonOptions));
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool ContainsOverlay(ReadOnlySpan<byte> composed, ReadOnlySpan<byte> background)
    {
        for (var index = 0; index < composed.Length; index++)
        {
            if (Math.Abs(composed[index] - background[index]) > 64)
            {
                return true;
            }
        }
        return false;
    }

    private static void VerifyExportedColor(ProjectDocument document, string mediaPath, int sourceStream,
        string outputPath, int outputStream, string runDirectory)
    {
        var time = new MediaTime(1);
        using var source = VideoFrameNavigator.Open(mediaPath, sourceStream);
        using var output = VideoFrameNavigator.Open(outputPath, outputStream);
        using var sourceFrame = source.SeekFrame(time) ?? throw new InvalidDataException("No source comparison frame.");
        using var outputFrame = output.SeekFrame(time) ?? throw new InvalidDataException("No worker comparison frame.");
        using var converter = new SdrVideoConverter();
        var background = converter.Convert(sourceFrame.Frame);
        var actual = converter.Convert(outputFrame.Frame);
        if (background.Width != actual.Width || background.Height != actual.Height)
        {
            throw new InvalidDataException("Worker color comparison dimensions do not match.");
        }
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(runDirectory));
        var expected = renderer.ComposePreview(document, time, background.Pixels.Span,
            background.Width, background.Height, background.Width * 4);
        var alternate = document with
        {
            Subtitles = [document.Subtitles[0] with { Style = document.Subtitles[0].Style with { Fill = new(1, 0.02, 0.05, 0.9) } }],
            Layers = [document.Layers[0] with { Tracks = document.Layers[0].Tracks.Where(track => track.Property != AnimationProperty.FILL).ToImmutableArray() }]
        };
        var unchanged = renderer.ComposePreview(alternate, time, background.Pixels.Span,
            background.Width, background.Height, background.Width * 4);
        var pixels = actual.Pixels.Span;
        long expectedDistance = 0;
        long unchangedDistance = 0;
        var compared = 0;
        for (var index = 0; index < expected.Length; index += 4)
        {
            if (Math.Abs(expected[index] - unchanged[index]) + Math.Abs(expected[index + 2] - unchanged[index + 2]) < 64)
            {
                continue;
            }
            compared++;
            for (var channel = 0; channel < 3; channel++)
            {
                expectedDistance += Math.Abs(pixels[index + channel] - expected[index + channel]);
                unchangedDistance += Math.Abs(pixels[index + channel] - unchanged[index + channel]);
            }
        }
        if (compared < 20 || expectedDistance >= unchangedDistance)
        {
            throw new InvalidDataException($"Worker color pixels mismatch: compared={compared}, animated={expectedDistance}, unchanged={unchangedDistance}.");
        }
    }
}
