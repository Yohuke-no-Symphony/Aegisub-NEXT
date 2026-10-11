using AegiNext.Media.Processes;
using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

[Collection(nameof(NativeExportTestGroup))]
public sealed class ClipMaskExportTests
{
    private const int WIDTH = 96;
    private const int HEIGHT = 64;
    private const int FRAME_COUNT = 3;
    private const int LUMA_TOLERANCE = 2;

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentWorkerUsesTopTrackOrderAfterReorderRegardlessOfClipStorageOrder(bool reverseClips)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-track-export-").FullName;
        try
        {
            var sourcePath = Path.Combine(directory, "background.mkv");
            await CreateStaticSourceAsync(sourcePath);
            var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: sourcePath);
            var whiteTrack = new ProjectTrack();
            var blackTrack = new ProjectTrack();
            var white = new ProjectLayer
            {
                TrackId = whiteTrack.Id,
                Kind = LayerKind.SHAPE,
                Shape = new(ShapeKind.RECTANGLE, WIDTH, HEIGHT),
                Fill = SceneColor.White
            };
            var black = white with { Id = Guid.NewGuid(), TrackId = blackTrack.Id, Fill = SceneColor.Black };
            var project = new ProjectDocument
            {
                Width = WIDTH,
                Height = HEIGHT,
                Assets = [media],
                Media = new(media.Id, 0, null, MediaTime.Zero),
                Tracks = [whiteTrack, blackTrack],
                Layers = reverseClips ? [black, white] : [white, black]
            };
            for (var pass = 0; pass < 2; pass++)
            {
                var document = pass == 0 ? project : project with { Tracks = [blackTrack, whiteTrack] };
                var outputPath = Path.Combine(directory, $"track-order-{pass}.mkv");
                var result = await new VideoExporter().ExportAsync(Request(document, directory, outputPath));
                Assert.Equal((ulong)FRAME_COUNT, result.Frames);
                using var decoder = FfmpegVideoDecoder.Open(outputPath, 0, options: new() { Mode = VideoDecodeMode.Software });
                for (var frameIndex = 0; frameIndex < FRAME_COUNT; frameIndex++)
                {
                    using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
                    Assert.Equal("yuv420p", frame.Info.PixelFormat);
                    var luma = Luma(frame, frame.CopyPlane(0), frame.GetPlaneInfo(0).RowBytes, WIDTH / 2, HEIGHT / 2);
                    Assert.InRange(luma, pass == 0 ? 230 : 14, pass == 0 ? 255 : 18);
                }
                Assert.Null(decoder.ReadFrame());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentWorkerBurnsNodeMaskAnimationAndPreservesSiblingAndBackground(bool inverted)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-mask-export-").FullName;
        try
        {
            var sourcePath = Path.Combine(directory, "静态背景.mkv");
            await CreateStaticSourceAsync(sourcePath);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), Path.Combine(directory, "mask-font.ttf"));
            var project = CreateProject(sourcePath);
            var target = project.Layers[1];
            var mask = new VectorClipMask
            {
                Inverted = inverted,
                Transform = new() { Pivot = new(40, 32) },
                Contours = [new() { Nodes =
                [
                    new() { Position = new(24, -4) }, new() { Position = new(56, -4) },
                    new() { Position = new(56, 68) }, new() { Position = new(24, 68) }
                ] }]
            };
            var nodeTracks = mask.Contours[0].Nodes.Select((node, index) => new AnimationTrack(
                new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id),
                [new(new(0), node.Position), new(new(2, 25), new ScenePoint(index is 0 or 3 ? 48 : 88, node.Position.Y))]))
                .ToImmutableArray();
            var masked = project with
            {
                Layers = project.Layers.SetItem(1, target with { Mask = mask, Tracks = target.Tracks.AddRange(nodeTracks) })
            };
            var withoutTarget = project with { Subtitles = [project.Subtitles[1]], Layers = [project.Layers[0]] };
            var fullPath = Path.Combine(directory, "完整字幕.mkv");
            var siblingPath = Path.Combine(directory, "兄弟字幕.mkv");
            var maskedPath = Path.Combine(directory, inverted ? "反相蒙版.mkv" : "正向蒙版.mkv");
            var exporter = new VideoExporter();
            foreach (var request in new VideoExportRequest[]
            {
                Request(project, directory, fullPath), Request(withoutTarget, directory, siblingPath), Request(masked, directory, maskedPath)
            })
            {
                var result = await exporter.ExportAsync(request);
                Assert.Equal((ulong)FRAME_COUNT, result.Frames);
                Assert.Equal("libx264", result.Encoder);
            }

            var options = new VideoDecoderOptions { Mode = VideoDecodeMode.Software };
            using var original = FfmpegVideoDecoder.Open(sourcePath, 0, options);
            using var full = FfmpegVideoDecoder.Open(fullPath, 0, options);
            using var sibling = FfmpegVideoDecoder.Open(siblingPath, 0, options);
            using var clipped = FfmpegVideoDecoder.Open(maskedPath, 0, options);
            byte[]? firstTargetPlane = null;
            byte[]? firstMaskedPlane = null;
            for (var frameIndex = 0; frameIndex < FRAME_COUNT; frameIndex++)
            {
                using var sourceFrame = original.ReadFrame();
                using var fullFrame = full.ReadFrame();
                using var siblingFrame = sibling.ReadFrame();
                using var clippedFrame = clipped.ReadFrame();
                Assert.NotNull(sourceFrame);
                Assert.NotNull(fullFrame);
                Assert.NotNull(siblingFrame);
                Assert.NotNull(clippedFrame);
                var expectedTime = new MediaTime(frameIndex, 25);
                Assert.Equal(expectedTime, sourceFrame.Info.PresentationTimestamp!.ToMediaTime());
                Assert.Equal(expectedTime, fullFrame.Info.PresentationTimestamp!.ToMediaTime());
                Assert.Equal(expectedTime, siblingFrame.Info.PresentationTimestamp!.ToMediaTime());
                Assert.Equal(expectedTime, clippedFrame.Info.PresentationTimestamp!.ToMediaTime());
                Assert.Equal(WIDTH, clippedFrame.Info.Width);
                Assert.Equal(HEIGHT, clippedFrame.Info.Height);
                var sourcePixels = sourceFrame.CopyPlane(0);
                var fullPixels = fullFrame.CopyPlane(0);
                var siblingPixels = siblingFrame.CopyPlane(0);
                var clippedPixels = clippedFrame.CopyPlane(0);
                var sourceStride = sourceFrame.GetPlaneInfo(0).RowBytes;
                var fullStride = fullFrame.GetPlaneInfo(0).RowBytes;
                var siblingStride = siblingFrame.GetPlaneInfo(0).RowBytes;
                var clippedStride = clippedFrame.GetPlaneInfo(0).RowBytes;
                Assert.Equal(8, sourceFrame.Info.ComponentDepths[0]);
                Assert.Equal(8, fullFrame.Info.ComponentDepths[0]);
                Assert.Equal(8, siblingFrame.Info.ComponentDepths[0]);
                Assert.Equal(8, clippedFrame.Info.ComponentDepths[0]);
                var left = 24 + frameIndex * 12;
                var right = 56 + frameIndex * 16;
                var retainedTargetInk = 0;
                var removedTargetInk = 0;
                var siblingInkOutsideMask = 0;
                var backgroundPixelsOutsideMask = 0;
                for (var y = 0; y < HEIGHT; y++)
                {
                    for (var x = 0; x < WIDTH; x++)
                    {
                        if (Math.Abs(x - left) <= 2 || Math.Abs(x - right) <= 2)
                        {
                            continue;
                        }

                        var insidePath = x >= left && x < right;
                        var keepTarget = inverted ? !insidePath : insidePath;
                        var sourceValue = Luma(sourceFrame, sourcePixels, sourceStride, x, y);
                        var fullValue = Luma(fullFrame, fullPixels, fullStride, x, y);
                        var siblingValue = Luma(siblingFrame, siblingPixels, siblingStride, x, y);
                        var clippedValue = Luma(clippedFrame, clippedPixels, clippedStride, x, y);
                        var expected = keepTarget ? fullValue : siblingValue;
                        Assert.InRange(Math.Abs(clippedValue - expected), 0, LUMA_TOLERANCE);
                        if (Math.Abs(fullValue - siblingValue) > 20)
                        {
                            retainedTargetInk += keepTarget ? 1 : 0;
                            removedTargetInk += keepTarget ? 0 : 1;
                        }

                        if (!keepTarget)
                        {
                            siblingInkOutsideMask += Math.Abs(siblingValue - sourceValue) > 20 ? 1 : 0;
                            backgroundPixelsOutsideMask += Math.Abs(siblingValue - sourceValue) <= LUMA_TOLERANCE ? 1 : 0;
                        }
                    }
                }

                Assert.True(retainedTargetInk > 0);
                Assert.True(removedTargetInk > 0);
                Assert.True(siblingInkOutsideMask > 0);
                Assert.True(backgroundPixelsOutsideMask > 0);
                if (frameIndex == 0)
                {
                    firstTargetPlane = fullPixels;
                    firstMaskedPlane = clippedPixels;
                }
                else if (frameIndex == FRAME_COUNT - 1)
                {
                    Assert.False(firstTargetPlane!.AsSpan().SequenceEqual(fullPixels));
                    Assert.False(firstMaskedPlane!.AsSpan().SequenceEqual(clippedPixels));
                }
            }

            Assert.Null(original.ReadFrame());
            Assert.Null(full.ReadFrame());
            Assert.Null(sibling.ReadFrame());
            Assert.Null(clipped.ReadFrame());
            Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProjectDocument CreateProject(string sourcePath)
    {
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: sourcePath);
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "mask-font.ttf");
        var target = new SubtitleLine
        {
            Text = "MMMMMMMM", Style = new()
            {
                FontAssetId = font.Id, FontFamily = "Noto Sans", FontSize = 18, Alignment = TextAlignment.TOP_LEFT,
                Margins = new(4, 4, 4), Fill = SceneColor.White, Stroke = new(0.4, 0.4, 0.4), StrokeWidth = 1,
                ShadowColor = SceneColor.Black, ShadowOffset = new(3, 3), ShadowBlur = 1
            }
        };
        var siblingTrack = new ProjectTrack { Name = "Sibling" };
        var sibling = new SubtitleLine
        {
            Text = "KEEP KEEP", Style = target.Style with
            {
                FontSize = 14, StrokeWidth = 0, ShadowColor = SceneColor.Transparent, ShadowBlur = 0
            }
        };
        return new()
        {
            Width = WIDTH, Height = HEIGHT, FrameRate = new(25, 1), Assets = [media, font],
            Media = new(media.Id, 0, null, MediaTime.Zero), Tracks = [ProjectTrack.Default, siblingTrack],
            Subtitles = [target, sibling],
            Layers =
            [
                new()
                {
                    Id = sibling.Id, TrackId = siblingTrack.Id, Kind = LayerKind.SUBTITLE, SubtitleId = sibling.Id, Start = sibling.Start, End = sibling.End,
                    Transform = new(X: 0, Y: 36)
                },
                new()
                {
                    Id = target.Id, Kind = LayerKind.SUBTITLE, SubtitleId = target.Id, Start = target.Start, End = target.End, Blur = 1,
                    Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(2, 25), new ScenePoint(4, 0))])]
                }
            ]
        };
    }

    private static VideoExportRequest Request(ProjectDocument document, string directory, string output) => new(document, directory, output)
    {
        Codec = VideoCodec.H264, EncodingMode = VideoEncodingMode.SOFTWARE, DecodeMode = VideoDecodeMode.Software,
        AudioMode = AudioExportMode.None, Quality = 0, Preset = "ultrafast",
        WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"),
        FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")
    };

    private static async Task CreateStaticSourceAsync(string output)
    {
        var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
            ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=gray:size=96x64:rate=25",
                "-frames:v", "3", "-c:v", "ffv1", "-level", "3", "-threads", "1", "-pix_fmt", "yuv420p",
                "-vf", "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
                "-chroma_sample_location", "left", "-y", output],
            TimeSpan.FromSeconds(60), 1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"静态压制素材生成失败：{result.StandardError}");
    }

    private static int Luma(DecodedVideoFrame frame, byte[] plane, int rowBytes, int x, int y)
    {
        return plane[(y + frame.Info.CropTop) * rowBytes + x + frame.Info.CropLeft];
    }
}
