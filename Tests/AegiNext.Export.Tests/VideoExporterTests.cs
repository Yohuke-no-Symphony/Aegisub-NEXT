using AegiNext.Testing.Media;
using AegiNext.Media.Processes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

[Collection(nameof(NativeExportTestGroup))]
public sealed class VideoExporterTests
{
    [MacSystemFontExportTheory]
    [InlineData(VideoCodec.H264)]
    public async Task WorkerShapesMixedChineseAndZwjEmojiWithSystemFontFallback(VideoCodec codec)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var subtitle = new SubtitleLine
        {
            Text = "A你👩‍💻Z",
            Style = new()
            {
                FontFamily = "Arial", FontSize = 18, Margins = new(6, 6, 6), Underline = true,
                StrokeWidth = 0, ShadowColor = SceneColor.Transparent, ShadowBlur = 0
            },
            InlineSpans = [new(1, 6, new() { Fill = new(1, 0, 0), Strikethrough = true })]
        };
        var project = CreateProject(fixture.MediaPath, 1, 96, 64) with
        {
            Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };
        var output = Path.Combine(directory, "mixed-fonts.mkv");
        var result = await new VideoExporter().ExportAsync(Request(project, directory, output) with { Codec = codec });
        Assert.Equal(3UL, result.Frames);
        using var source = FfmpegVideoDecoder.Open(fixture.MediaPath, 1);
        using var rendered = FfmpegVideoDecoder.Open(output, 0);
        using var original = source.ReadFrame();
        using var frame = rendered.ReadFrame();
        Assert.NotNull(original);
        Assert.NotNull(frame);
        Assert.Equal(original.Info.PresentationTimestamp!.ToMediaTime(), frame.Info.PresentationTimestamp!.ToMediaTime());
        Assert.False(original.CopyPlane(0).AsSpan().SequenceEqual(frame.CopyPlane(0)));
        Assert.True(SumRedChroma(frame) > SumRedChroma(original) + 10);
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(VideoCodec.H264)]
    public async Task WorkerLoadsInlineFontAndPreservesRichGeometryUnderStepKaraoke(VideoCodec codec)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), Path.Combine(directory, "inline.ttf"));
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "inline.ttf");
        var subtitle = new SubtitleLine
        {
            Text = "TEST", Style = new()
            {
                FontFamily = "Arial", FontSize = 16, Margins = new(6, 6, 6), StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent, ShadowBlur = 0
            },
            InlineSpans =
            [
                new(0, 2, new() { FontFamily = "Noto Sans", FontAssetId = font.Id, FontSize = 20, Underline = true }),
                new(2, 2, new() { Bold = true, Strikethrough = true })
            ]
        };
        var baseline = CreateProject(fixture.MediaPath, 1, 96, 64) with
        {
            Subtitles = [subtitle],
            Layers = [new() { Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };
        baseline = baseline with { Assets = baseline.Assets.Add(font) };
        var highlighted = baseline with
        {
            Subtitles = [subtitle with
            {
                Karaoke = [new(0, 4, new(1, 1000), new(1), new(1, 0, 0)) { HighlightKind = KaraokeHighlightKind.STEP }]
            }]
        };
        var baselinePath = Path.Combine(directory, "rich.mkv");
        var highlightPath = Path.Combine(directory, "rich-karaoke.mkv");
        var exporter = new VideoExporter();
        var first = await exporter.ExportAsync(Request(baseline, directory, baselinePath) with { Codec = codec });
        var second = await exporter.ExportAsync(Request(highlighted, directory, highlightPath) with { Codec = codec });
        Assert.Equal(3UL, first.Frames);
        Assert.Equal(first.Frames, second.Frames);
        using var normal = FfmpegVideoDecoder.Open(baselinePath, 0);
        using var karaoke = FfmpegVideoDecoder.Open(highlightPath, 0);
        using var initialNormal = normal.ReadFrame();
        using var initialKaraoke = karaoke.ReadFrame();
        Assert.NotNull(initialNormal);
        Assert.NotNull(initialKaraoke);
        Assert.Equal(initialNormal.CopyPlane(0), initialKaraoke.CopyPlane(0));
        using var activeNormal = normal.ReadFrame();
        using var activeKaraoke = karaoke.ReadFrame();
        Assert.NotNull(activeNormal);
        Assert.NotNull(activeKaraoke);
        Assert.True(SumRedChroma(activeKaraoke) > SumRedChroma(activeNormal) + 10);
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(VideoCodec.H264)]
    public async Task WorkerLoadsTrackStyleAndBurnsKaraokeAppearanceSnapshot(VideoCodec codec)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var presetId = Guid.NewGuid();
        var style = new SubtitleStyle
        {
            FontFamily = "Arial", FontSize = 16, Margins = new(6, 6, 6), Fill = SceneColor.White,
            StrokeWidth = 0, ShadowColor = SceneColor.Transparent, ShadowBlur = 0
        };
        var subtitle = new SubtitleLine { Text = "TEST", Style = style };
        var project = CreateProject(fixture.MediaPath, 1, 96, 64) with
        {
            Tracks = [ProjectTrack.Default with { DefaultStyle = style, StylePresetId = presetId, StylePresetName = "Track default" }],
            Subtitles = [subtitle],
            Layers = [new()
            {
                Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id,
                Start = subtitle.Start, End = subtitle.End
            }]
        };
        var highlighted = project with
        {
            Subtitles = [subtitle with
            {
                Karaoke = [new(0, 4, MediaTime.Zero, new(1, 1000), SceneColor.White)],
                KaraokeStyle = KaraokeHighlightStyle.FromStyle(presetId, "Red highlight", style with { Fill = new(1, 0, 0) })
            }]
        };
        var baselinePath = Path.Combine(directory, "plain.mkv");
        var highlightPath = Path.Combine(directory, "高亮样式.mkv");
        var exporter = new VideoExporter();
        var plainResult = await exporter.ExportAsync(Request(project, directory, baselinePath) with { Codec = codec });
        var highlightResult = await exporter.ExportAsync(Request(highlighted, directory, highlightPath) with { Codec = codec });
        Assert.Equal(3UL, plainResult.Frames);
        Assert.Equal(plainResult.Frames, highlightResult.Frames);
        using var plain = FfmpegVideoDecoder.Open(baselinePath, 0);
        using var highlight = FfmpegVideoDecoder.Open(highlightPath, 0);
        using var firstPlain = plain.ReadFrame();
        using var firstHighlight = highlight.ReadFrame();
        Assert.NotNull(firstPlain);
        Assert.NotNull(firstHighlight);
        Assert.Equal(firstPlain.CopyPlane(0), firstHighlight.CopyPlane(0));
        using var nextPlain = plain.ReadFrame();
        using var nextHighlight = highlight.ReadFrame();
        Assert.NotNull(nextPlain);
        Assert.NotNull(nextHighlight);
        Assert.Equal(nextPlain.Info.PresentationTimestamp, nextHighlight.Info.PresentationTimestamp);
        Assert.False(nextPlain.CopyPlane(0).AsSpan().SequenceEqual(nextHighlight.CopyPlane(0)));
        var plainRedChroma = SumRedChroma(nextPlain);
        var highlightRedChroma = SumRedChroma(nextHighlight);
        Assert.True(highlightRedChroma > plainRedChroma + 10, "The independent worker must render the saved red highlight, rather than the segment's white fallback.");
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(VideoCodec.H264, "h264", "yuv420p", VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.H264, "h264", "yuv420p", VideoRateControlMode.CBR)]
    [InlineData(VideoCodec.Hevc, "hevc", "yuv420p10le", VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Hevc, "hevc", "yuv420p10le", VideoRateControlMode.CBR)]
    public async Task HardwareWorkerUsesActualHardwareOrReportsUnavailableWithoutCpuFallback(VideoCodec codec, string codecName,
        string pixelFormat, VideoRateControlMode rateControlMode)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "GPU 颜色成片.mp4");
        var project = CreateProject(fixture.MediaPath, 1, 96, 64);
        var request = Request(project, directory, output) with
        {
            Codec = codec, EncodingMode = VideoEncodingMode.HARDWARE, VideoBitrate = 8000000,
            AudioMode = AudioExportMode.Aac, RateControlMode = rateControlMode
        };
        var progress = new ExportProgressCapture();
        var expectUnavailable = Environment.GetEnvironmentVariable("AEGINEXT_EXPECT_GPU_ENCODER") == "unavailable";
        if (expectUnavailable)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request, progress));
            Assert.Contains("GPU hardware video encoder unavailable", error.Message, StringComparison.Ordinal);
            Assert.Contains("no CPU fallback", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
            Assert.DoesNotContain(progress.Values, value => value.Encoder is "libx264" or "libx265");
            Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
            var cpu = await new VideoExporter().ExportAsync(request with { EncodingMode = VideoEncodingMode.SOFTWARE });
            Assert.Equal(codec == VideoCodec.H264 ? "libx264" : "libx265", cpu.Encoder);
            Assert.Equal(new VideoRateControlInfo(rateControlMode, 8000000, 0), cpu.RateControl);
            Assert.True(File.Exists(output));
            return;
        }

        var result = await new VideoExporter().ExportAsync(request, progress);
        Assert.Equal(3UL, result.Frames);
        Assert.Equal(new VideoRateControlInfo(rateControlMode, 8000000, 0), result.RateControl);
        Assert.NotNull(result.Encoder);
        var prefix = codec == VideoCodec.H264 ? "h264_" : "hevc_";
        Assert.Contains(result.Encoder, new[] { prefix + "videotoolbox", prefix + "nvenc", prefix + "qsv", prefix + "amf" });
        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(prefix + "videotoolbox", result.Encoder);
        }
        Assert.Contains(progress.Values, value => value.Stage == "encoding" && value.Encoder == result.Encoder);
        Assert.Equal(result.Encoder, progress.Values[^1].Encoder);
        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(output);
        var video = report.Asset.Streams.Single(stream => stream.CodecType == "video");
        Assert.Equal(codecName, video.CodecName);
        Assert.Equal(pixelFormat, video.Video!.PixelFormat);
        Assert.Equal("bt709", video.Video.Color.Transfer);
        Assert.Equal("bt709", video.Video.Color.Primaries);
        Assert.Equal("aac", report.Asset.Streams.Single(stream => stream.CodecType == "audio").CodecName);
        using var decoded = FfmpegVideoDecoder.Open(output, video.Index);
        using var frame = decoded.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(96, frame.Info.Width);
        Assert.Equal(64, frame.Info.Height);
        using var originalDecoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 1);
        using var original = originalDecoder.ReadFrame();
        Assert.NotNull(original);
        Assert.Equal(original.Info.PresentationTimestamp!.ToMediaTime(), frame.Info.PresentationTimestamp!.ToMediaTime());
        var originalLuma = ReadLumaCode(original, original.CopyPlane(0), 12, 12);
        var outputLuma = frame.CopyPlane(0);
        var compositedLuma = ReadLumaCode(frame, outputLuma, 12, 12) / (codec == VideoCodec.H264 ? 1 : 4);
        Assert.True(compositedLuma > originalLuma + 15,
            $"The rendered half-white overlay must brighten the hardware-encoded frame: {originalLuma} -> {compositedLuma}.");
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public async Task HardwareHdrReportsPlatformPolicyWithoutCpuFallback(string transfer)
    {
        using var fixture = await DecoderFixture.CreateAsync(transfer);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "gpu-hdr.mkv");
        var request = Request(CreateProject(fixture.MediaPath, fixture.VideoStreamIndex, 64, 48), directory, output)
            with { EncodingMode = VideoEncodingMode.HARDWARE };
        if (!OperatingSystem.IsMacOS())
        {
            try
            {
                var result = await new VideoExporter().ExportAsync(request);
                Assert.StartsWith("hevc_", result.Encoder);
                Assert.Equal(10, result.OutputColor!.BitDepth);
                Assert.Equal(transfer == "smpte2084" ? 16 : 18, result.OutputColor.Transfer);
            }
            catch (InvalidOperationException unavailable)
            {
                Assert.Contains("GPU encoder unavailable", unavailable.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(output));
            }

            Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
            return;
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request));
        Assert.Contains("VideoToolbox HDR", error.Message, StringComparison.Ordinal);
        Assert.Contains("no CPU fallback", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public async Task WorkerBurnsHdrOverlayPreservesPtsAndCopiesAudio(string transfer)
    {
        using var fixture = await DecoderFixture.CreateAsync(transfer, variableFrameRate: true, startTimeMilliseconds: 3000);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "输出成片.mkv");
        var project = CreateProject(fixture.MediaPath, fixture.VideoStreamIndex, 64, 48);
        var request = Request(project, directory, output);
        var progress = new ExportProgressCapture();
        var result = await new VideoExporter().ExportAsync(request, progress);
        Assert.Equal((ulong)DecoderFixture.FRAME_COUNT, result.Frames);
        Assert.Equal(new VideoRateControlInfo(VideoRateControlMode.CRF, 0, 0), result.RateControl);
        Assert.Equal(output, result.OutputPath);
        Assert.Contains(progress.Values, item => item.Stage == "encoding" && item.Position >= new MediaTime(3));
        Assert.Equal("complete", progress.Values[^1].Stage);
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));

        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(output);
        var video = report.Asset.Streams.Single(stream => stream.CodecType == "video");
        Assert.Equal("hevc", video.CodecName);
        Assert.Equal("yuv420p10le", video.Video!.PixelFormat);
        Assert.Equal(transfer, video.Video.Color.Transfer);
        Assert.Equal("bt2020", video.Video.Color.Primaries);
        Assert.Equal("pcm_s16le", report.Asset.Streams.Single(stream => stream.CodecType == "audio").CodecName);

        using var source = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex);
        using var encoded = FfmpegVideoDecoder.Open(output, video.Index);
        var changed = false;
        for (var i = 0; i < DecoderFixture.FRAME_COUNT; i++)
        {
            using var original = source.ReadFrame();
            using var frame = encoded.ReadFrame();
            Assert.NotNull(original);
            Assert.NotNull(frame);
            Assert.Equal(original.Info.PresentationTimestamp!.ToMediaTime(), frame.Info.PresentationTimestamp!.ToMediaTime());
            if (transfer == "smpte2084")
            {
                Assert.NotNull(original.Info.MasteringDisplay);
                Assert.Equal(original.Info.MasteringDisplay, frame.Info.MasteringDisplay);
                Assert.Equal(original.Info.ContentLight, frame.Info.ContentLight);
            }

            var originalY = original.CopyPlane(0);
            var encodedY = frame.CopyPlane(0);
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var a = ReadLumaCode(original, originalY, x, y);
                    var b = ReadLumaCode(frame, encodedY, x, y);
                    if (x < 8 || x >= 16 || y < 8 || y >= 16)
                    {
                        Assert.InRange(Math.Abs(a - b), 0, 1);
                    }
                    else
                    {
                        changed |= a != b;
                    }
                }
            }
        }

        Assert.True(changed);
        Assert.Null(encoded.ReadFrame());
        var audioBefore = await AudioHash(fixture.MediaPath);
        var audioAfter = await AudioHash(output);
        Assert.Equal(audioBefore, audioAfter);
    }

    [ExportTheory]
    [InlineData(AudioExportMode.Copy, ".mkv", "pcm_s16le")]
    [InlineData(AudioExportMode.Aac, ".mp4", "aac")]
    public async Task WorkerProducesSdrH264AndSelectedAudio(AudioExportMode audio, string extension, string expectedAudio)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "成片" + extension);
        var project = CreateProject(fixture.MediaPath, 1, 96, 64);
        var subtitle = new SubtitleLine { Text = "AegiNext", Style = new SubtitleStyle { FontFamily = "Arial", FontSize = 10, Margins = new(2, 2, 2), ShadowBlur = 0, StrokeWidth = 0 } };
        project = project with { Subtitles = [subtitle], Layers = project.Layers.Add(new ProjectLayer
        {
            Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End
        }) };
        var request = Request(project, directory, output) with { AudioMode = audio };
        var result = await new VideoExporter().ExportAsync(request);
        Assert.Equal(3UL, result.Frames);
        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(output);
        var video = report.Asset.Streams.Single(stream => stream.CodecType == "video");
        Assert.Equal("h264", video.CodecName);
        Assert.Equal("yuv420p", video.Video!.PixelFormat);
        Assert.Equal("bt709", video.Video.Color.Transfer);
        Assert.Equal("bt709", video.Video.Color.Primaries);
        Assert.Equal(expectedAudio, report.Asset.Streams.Single(stream => stream.CodecType == "audio").CodecName);
    }

    [ExportTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingOutputAndCancellationNeverCommitPartialFile(bool existing)
    {
        using var fixture = await DecoderFixture.CreateAsync(frameCount: 120);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "保留成片.mkv");
        var request = Request(CreateProject(fixture.MediaPath, 1, 64, 48), directory, output);
        if (existing)
        {
            await File.WriteAllTextAsync(output, "previous output");
            await Assert.ThrowsAsync<IOException>(() => new VideoExporter().ExportAsync(request));
            Assert.Equal("previous output", await File.ReadAllTextAsync(output));
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            var progress = new ExportProgressCapture(value =>
            {
                if (value.Stage == "encoding")
                {
                    cancellation.Cancel();
                }
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoExporter().ExportAsync(request, progress, cancellation.Token));
            Assert.False(File.Exists(output));
            Assert.NotEmpty(progress.Values);
        }

        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    /// <summary>编码开始后取消并立即复用导出服务与输出路径，不遗留临时文件或阻止后续导出。</summary>
    [ExportTheory]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Auto)]
    public async Task CancelledEncodingCanImmediatelyRestartWithTheSameExporterAndOutputPath(VideoDecodeMode decodeMode)
    {
        const int CANCEL_RESTART_ATTEMPTS = 3;
        const int EXPORT_TIMEOUT_SECONDS = 60;
        using var fixture = await DecoderFixture.CreateAsync(transfer: "bt709", frameCount: 120);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "取消后重新编码.mkv");
        var request = Request(CreateProject(fixture.MediaPath, 1, 64, 48), directory, output) with
        {
            AudioMode = AudioExportMode.None,
            DecodeMode = decodeMode
        };
        var exporter = new VideoExporter();

        for (var attempt = 0; attempt < CANCEL_RESTART_ATTEMPTS; attempt++)
        {
            File.Delete(output);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(EXPORT_TIMEOUT_SECONDS));
            var cancelledFromEncoding = false;
            var progress = new ExportProgressCapture(value =>
            {
                if (value.Stage != "encoding" || value.FrameCount == 0 || cancellation.IsCancellationRequested)
                {
                    return;
                }

                var temporary = Assert.Single(Directory.GetDirectories(directory, ".aeginext-export-*"));
                Assert.True(File.Exists(Path.Combine(temporary, "video.nut")));
                cancelledFromEncoding = true;
                cancellation.Cancel();
            });

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                exporter.ExportAsync(request, progress, cancellation.Token));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(cancelledFromEncoding, "Cancellation must be triggered by active encoding progress.");
            Assert.True(cancellation.IsCancellationRequested);
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));

            using var restartDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(EXPORT_TIMEOUT_SECONDS));
            var result = await exporter.ExportAsync(request, cancellationToken: restartDeadline.Token);

            Assert.Equal(output, result.OutputPath);
            Assert.Equal((ulong)fixture.ExpectedFrames.GetArrayLength(), result.Frames);
            Assert.True(new FileInfo(output).Length > 0);
            Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
        }
    }

    [ExportTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AtomicCommitCallbackRunsBeforePublishingAndCleansRejectedOutput(bool reject)
    {
        using var fixture = await DecoderFixture.CreateAsync(frameCount: 5);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "atomic-commit.mkv");
        var request = Request(CreateProject(fixture.MediaPath, 1, 64, 48), directory, output);
        var callbackInvoked = false;
        var export = new VideoExporter().ExportAsync(request, null, () =>
        {
            callbackInvoked = true;
            Assert.False(File.Exists(output));
            if (reject)
            {
                throw new OperationCanceledException("Commit rejected");
            }
        }, CancellationToken.None);
        if (reject)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        }
        else
        {
            await export;
        }
        Assert.True(callbackInvoked);
        Assert.Equal(!reject, File.Exists(output));
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData("smpte2084", 723, 855, 675)]
    [InlineData("arib-std-b67", 940, 502, 872)]
    public async Task HdrHighlightsStayHdrAndHalfWhiteUses203Nits(string transfer, int left, int right, int mixed)
    {
        using var fixture = await HdrExportRampFixture.CreateAsync(transfer);
        var project = CreateProject(fixture.MediaPath, 0, 64, 48);
        project = project with { Media = project.Media! with { AudioStreamIndex = null } };
        var output = Path.Combine(fixture.Directory, "composited.mkv");
        await new VideoExporter().ExportAsync(Request(project, fixture.Directory, output) with { AudioMode = AudioExportMode.None });
        using var decoder = FfmpegVideoDecoder.Open(output, 0);
        using var frame = decoder.ReadFrame();
        Assert.NotNull(frame);
        var y = frame.CopyPlane(0);
        Assert.Equal(left, ReadLumaCode(frame, y, 20, 24));
        Assert.Equal(right, ReadLumaCode(frame, y, 48, 24));
        Assert.InRange(Math.Abs(ReadLumaCode(frame, y, 12, 12) - mixed), 0, 1);
    }

    [ExportTheory]
    [InlineData(VideoCodec.H264)]
    public async Task UnsupportedHdrCodecDoesNotCommitOutput(VideoCodec codec)
    {
        using var fixture = await DecoderFixture.CreateAsync();
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var output = Path.Combine(directory, "unsupported.mp4");
        var request = Request(CreateProject(fixture.MediaPath, 1, 64, 48), directory, output) with { Codec = codec };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request));
        Assert.Contains("HDR requires HEVC", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    private static ProjectDocument CreateProject(string media, int videoStream, int width, int height)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "", ExternalPath: media);
        var shapeTrack = new ProjectTrack { Name = "Shape overlay" };
        return new()
        {
            Width = width, Height = height, Assets = [asset], Media = new(asset.Id, videoStream, 0, MediaTime.Zero),
            Tracks = [ProjectTrack.Default, shapeTrack],
            Layers = [new ProjectLayer { TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 8, 8),
                Transform = new(X: 8, Y: 8), Fill = new(1, 1, 1, 0.5), End = new(100) }]
        };
    }

    private static VideoExportRequest Request(ProjectDocument project, string directory, string output)
    {
        return new(project, directory, output)
        {
            Quality = 0, Preset = "ultrafast", WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"),
            FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")
        };
    }

    private static long SumRedChroma(DecodedVideoFrame frame)
    {
        var interleaved = frame.Info.PixelFormat == "nv12";
        Assert.True(interleaved || frame.Info.PixelFormat == "yuv420p");
        var planeIndex = interleaved ? 1 : 2;
        var pixels = frame.CopyPlane(planeIndex);
        var layout = frame.GetPlaneInfo(planeIndex);
        var sum = 0L;
        var width = (frame.Info.Width - frame.Info.CropLeft - frame.Info.CropRight) / 2;
        var height = (frame.Info.Height - frame.Info.CropTop - frame.Info.CropBottom) / 2;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y + frame.Info.CropTop / 2) * layout.RowBytes +
                    (x + frame.Info.CropLeft / 2) * (interleaved ? 2 : 1) + (interleaved ? 1 : 0);
                sum += pixels[offset];
            }
        }
        return sum;
    }

    private static int ReadLumaCode(DecodedVideoFrame frame, byte[] plane, int x, int y)
    {
        var tenBit = frame.Info.ComponentDepths[0] == 10;
        var offset = (y + frame.Info.CropTop) * frame.GetPlaneInfo(0).RowBytes +
            (x + frame.Info.CropLeft) * (tenBit ? 2 : 1);
        if (!tenBit)
        {
            Assert.Equal(8, frame.Info.ComponentDepths[0]);
            return plane[offset];
        }

        var value = BitConverter.ToUInt16(plane, offset);
        return frame.Info.PixelFormat == "p010le" ? value >> 6 : value;
    }

    private static async Task<string> AudioHash(string media)
    {
        var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
            ["-v", "error", "-i", media, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"],
            TimeSpan.FromSeconds(15), 65536, 65536, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }
}
