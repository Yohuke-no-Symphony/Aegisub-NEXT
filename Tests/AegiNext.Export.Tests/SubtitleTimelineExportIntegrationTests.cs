using AegiNext.Media.Processes;
using System.Text.Json;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

[Collection(nameof(NativeExportTestGroup))]
public sealed class SubtitleTimelineExportIntegrationTests
{
    private const int SAMPLE_RATE = 48000;
    private static readonly MediaTime[] impulseTimes = [new(33, 10), new(44, 10)];

    [ExportTheory]
    [InlineData(AudioExportMode.Copy, ".mkv")]
    [InlineData(AudioExportMode.Aac, ".mp4")]
    public async Task VfrDifferentStreamOriginsKeepSubtitleFramesAndAudioImpulseTimesAligned(AudioExportMode audioMode, string extension)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-subtitle-timing-").FullName;
        try
        {
            var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
            var ffprobe = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!;
            var sourcePath = Path.Combine(directory, "source.mkv");
            var sourceResult = await MediaToolProcess.RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=24000/1001:duration=1",
                    "-f", "lavfi", "-i", "aevalsrc='if(eq(n,14400)+eq(n,67200),0.8,0)':s=48000:d=2",
                    "-vf", "settb=1/1000,setpts=PTS+floor(N/3)*42+3100,format=yuv420p10le,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                    "-af", "asetpts=PTS+3/TB", "-fps_mode", "vfr", "-enc_time_base:v", "1/1000",
                    "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "pools=none:frame-threads=1:lossless=1:log-level=error",
                    "-c:a", "pcm_s16le", "-y", sourcePath],
                TimeSpan.FromSeconds(30), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(sourceResult.ExitCode == 0, sourceResult.StandardError);
            var probe = new FfprobeMediaProbe(new(ffprobe));
            var source = (await probe.ProbeAsync(sourcePath)).Asset;
            var video = source.Streams.Single(stream => stream.CodecType == "video");
            var audio = source.Streams.Single(stream => stream.CodecType == "audio");
            var videoStart = video.Timing.StartTimestamp!.ToMediaTime();
            Assert.Equal(new MediaTime(31, 10), videoStart);
            Assert.Equal(new MediaTime(3), source.ReportedStart);
            var binding = new ProjectMediaBinding(Guid.NewGuid(), video.Index, audio.Index, videoStart)
            {
                PlaybackOrigin = source.ReportedStart
            };
            var mapping = Assert.IsType<MediaTimelineMapping>(SubtitleTimelineExchange.GetMapping(binding));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), Path.Combine(directory, "font.ttf"));
            var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "font.ttf");
            var external = new SubtitleLine
            {
                Start = new(3, 10), End = new(1, 2), Text = "T",
                Style = new()
                {
                    FontFamily = "Noto Sans", FontAssetId = font.Id, FontSize = 18, Margins = new(6, 6, 6),
                    StrokeWidth = 0, ShadowBlur = 0, ShadowColor = SceneColor.Transparent
                }
            };
            var line = Assert.Single(SubtitleTimelineExchange.ToProjectTime([external], mapping));
            Assert.Equal(new MediaTime(1, 5), line.Start);
            var media = new ProjectAsset(binding.AssetId, ProjectAssetKind.MEDIA, string.Empty, ExternalPath: sourcePath);
            var project = new ProjectDocument
            {
                Width = 64, Height = 48, Assets = [media, font], Media = binding, Subtitles = [line],
                Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
            };
            var outputPath = Path.Combine(directory, "output" + extension);
            var result = await new VideoExporter().ExportAsync(new(project, directory, outputPath)
            {
                Codec = VideoCodec.Hevc, Crf = 0, Preset = "ultrafast", AudioMode = audioMode,
                WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"), FfmpegPath = ffmpeg,
                DecodeMode = VideoDecodeMode.Software
            });
            var output = (await probe.ProbeAsync(outputPath)).Asset;
            var encodedVideo = output.Streams.Single(stream => stream.CodecType == "video");
            var encodedAudio = output.Streams.Single(stream => stream.CodecType == "audio");
            using var before = FfmpegVideoDecoder.Open(sourcePath, video.Index, options: new() { Mode = VideoDecodeMode.Software });
            using var after = FfmpegVideoDecoder.Open(outputPath, encodedVideo.Index, options: new() { Mode = VideoDecodeMode.Software });
            var gaps = new HashSet<MediaTime>();
            MediaTime? previous = null;
            var changedFrames = 0;
            var frames = 0;
            while (before.ReadFrame() is { } original)
            {
                using (original)
                using (var encoded = Assert.IsType<DecodedVideoFrame>(after.ReadFrame()))
                {
                    var time = original.Info.PresentationTimestamp!.ToMediaTime();
                    Assert.Equal(time, encoded.Info.PresentationTimestamp!.ToMediaTime());
                    if (previous is { } previousTime)
                    {
                        gaps.Add(time - previousTime);
                    }
                    previous = time;
                    var expectedVisible = time >= source.ReportedStart!.Value + external.Start && time < source.ReportedStart.Value + external.End;
                    var difference = MaximumLumaDifference(original, encoded);
                    if (expectedVisible)
                    {
                        Assert.True(difference > 5, $"Subtitle is missing at source PTS {time}.");
                        changedFrames++;
                    }
                    else
                    {
                        Assert.InRange(difference, 0, 1);
                    }
                    frames++;
                }
            }
            Assert.Null(after.ReadFrame());
            Assert.Equal((ulong)frames, result.Frames);
            Assert.True(gaps.Count > 1, "Fixture must contain variable presentation intervals.");
            Assert.True(changedFrames > 0);
            var originalImpulses = ReadImpulseTimes(sourcePath, audio.Index);
            var encodedImpulses = ReadImpulseTimes(outputPath, encodedAudio.Index);
            for (var index = 0; index < impulseTimes.Length; index++)
            {
                Assert.Equal(impulseTimes[index], originalImpulses[index]);
                var error = encodedImpulses[index] - originalImpulses[index];
                Assert.True(error >= new MediaTime(-1, 1000) && error <= new MediaTime(1, 1000),
                    $"Audio impulse {index} shifted by {error}; AAC priming must not add a codec-frame delay.");
            }
            if (audioMode == AudioExportMode.Copy)
            {
                Assert.Equal(await AudioPacketTimelineAsync(ffprobe, sourcePath, audio.Timing.TimeBase!),
                    await AudioPacketTimelineAsync(ffprobe, outputPath, encodedAudio.Timing.TimeBase!));
            }
            else
            {
                Assert.Equal("aac", encodedAudio.CodecName);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static int MaximumLumaDifference(DecodedVideoFrame original, DecodedVideoFrame encoded)
    {
        Assert.Equal("yuv420p10le", original.Info.PixelFormat);
        Assert.Equal("yuv420p10le", encoded.Info.PixelFormat);
        var source = original.CopyPlane(0);
        var output = encoded.CopyPlane(0);
        var sourceStride = original.GetPlaneInfo(0).RowBytes;
        var outputStride = encoded.GetPlaneInfo(0).RowBytes;
        var maximum = 0;
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                maximum = Math.Max(maximum, Math.Abs(BitConverter.ToUInt16(source, y * sourceStride + x * 2) -
                    BitConverter.ToUInt16(output, y * outputStride + x * 2)));
            }
        }
        return maximum;
    }

    private static MediaTime[] ReadImpulseTimes(string path, int streamIndex)
    {
        using var decoder = FfmpegAudioDecoder.Open(path, streamIndex, new(SAMPLE_RATE, 1));
        var peaks = new float[impulseTimes.Length];
        var positions = new MediaTime[impulseTimes.Length];
        while (decoder.Read() is { } block)
        {
            for (var sample = 0; sample < block.FrameCount; sample++)
            {
                var time = block.Start + new MediaTime(sample, SAMPLE_RATE);
                for (var index = 0; index < impulseTimes.Length; index++)
                {
                    var distance = time - impulseTimes[index];
                    var amplitude = Math.Abs(block.Samples.Span[sample]);
                    if (distance >= new MediaTime(-1, 20) && distance <= new MediaTime(1, 20) && amplitude > peaks[index])
                    {
                        peaks[index] = amplitude;
                        positions[index] = time;
                    }
                }
            }
        }
        Assert.All(peaks, amplitude => Assert.True(amplitude > 0.1f, "Impulse must survive the audio codec."));
        return positions;
    }

    private static async Task<string[]> AudioPacketTimelineAsync(string ffprobe, string path, MediaTimeBase timeBase)
    {
        var result = await MediaToolProcess.RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=pts,dts,duration,data_hash", "-of", "json", path],
            TimeSpan.FromSeconds(15), 1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
        using var json = JsonDocument.Parse(result.StandardOutput);
        return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet =>
            $"{new MediaTimestamp(packet.GetProperty("pts").GetInt64(), timeBase).ToMediaTime()}|" +
            $"{new MediaTimestamp(packet.GetProperty("dts").GetInt64(), timeBase).ToMediaTime()}|" +
            $"{new MediaTimestamp(packet.GetProperty("duration").GetInt64(), timeBase).ToMediaTime()}|" +
            packet.GetProperty("data_hash").GetString()).ToArray();
    }
}
