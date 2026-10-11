using AegiNext.Testing.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Export.Tests;

/// <summary>用共享排版测量及独立导出 worker 的实际解码帧验证系统命名字重。</summary>
[Collection(nameof(NativeExportTestGroup))]
public sealed class SystemFontVariantExportTests
{
    /// <summary>验证独立及可变字体的命名实例度量、缓存及实际压制帧均区别于旧二值样式。</summary>
    [MacNotoVariantExportTheory]
    [InlineData("Noto Sans SC", false)]
    [InlineData("Noto Serif SC", true)]
    [Trait("Category", "SystemFontVariantExport")]
    public async Task WorkerPreservesSemiBoldAndBlackBeyondLegacyBoldFlags(string family, bool variable)
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, [family]);
        var semiBold = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == "SemiBold");
        var black = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == "Black");
        Assert.Equal(variable, semiBold.IsVariable);
        Assert.Equal(variable, black.IsVariable);
        Assert.Equal(600, semiBold.Variant.Weight);
        Assert.Equal(900, black.Variant.Weight);
        if (variable)
        {
            Assert.True(semiBold.NamedInstanceIndex > 0);
            Assert.True(black.NamedInstanceIndex > 0);
            Assert.NotEqual(semiBold.NamedInstanceIndex, black.NamedInstanceIndex);
        }

        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var first = Project(fixture.MediaPath, semiBold);
        var second = WithStyle(first, first.Subtitles[0].Style with
        {
            FontVariant = black.Variant, Bold = true, Italic = black.Variant.Italic
        });
        var regular = WithStyle(first, first.Subtitles[0].Style with { FontVariant = null, Bold = false });
        var bold = WithStyle(first, first.Subtitles[0].Style with { FontVariant = null, Bold = true });

        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(directory));
        var firstLayout = renderer.MeasureSubtitleTextLayout(first, first.Subtitles[0]);
        var secondLayout = renderer.MeasureSubtitleTextLayout(second, second.Subtitles[0]);
        Assert.True(firstLayout.HasInk);
        Assert.True(secondLayout.HasInk);
        Assert.Equal(first.Subtitles[0].Text.Length, firstLayout.Graphemes.Length);
        Assert.Equal(second.Subtitles[0].Text.Length, secondLayout.Graphemes.Length);
        Assert.All(firstLayout.Runs, run =>
        {
            Assert.Equal(semiBold.Variant, run.Style.FontVariant);
            Assert.Equal(semiBold.Variant, run.ResolvedFontVariant);
            Assert.True(semiBold.Aliases.Contains(run.ResolvedFontFamily, StringComparer.OrdinalIgnoreCase));
        });
        Assert.All(secondLayout.Runs, run =>
        {
            Assert.Equal(black.Variant, run.Style.FontVariant);
            Assert.Equal(black.Variant, run.ResolvedFontVariant);
            Assert.True(black.Aliases.Contains(run.ResolvedFontFamily, StringComparer.OrdinalIgnoreCase));
        });
        Assert.True(firstLayout.Bounds != secondLayout.Bounds || !firstLayout.Graphemes.SequenceEqual(secondLayout.Graphemes),
            "命名字重切换必须刷新实际字形边界或字素测量。");

        var semiBoldPixels = RenderPixels(renderer, first);
        var blackPixels = RenderPixels(renderer, second);
        AssertDifferent(semiBoldPixels, blackPixels, "SemiBold 与 Black 的共享渲染像素必须不同。");
        Assert.True(AlphaCoverage(blackPixels) > AlphaCoverage(semiBoldPixels),
            "Black 必须使用更重的真实字形，而非复用 SemiBold 的轮廓。");
        AssertDifferent(semiBoldPixels, RenderPixels(renderer, regular), "SemiBold 不得退化成二值普通字体。");
        AssertDifferent(blackPixels, RenderPixels(renderer, bold), "Black 不得退化成二值 Bold 字体。");
        var restoredLayout = renderer.MeasureSubtitleTextLayout(first, first.Subtitles[0]);
        Assert.Equal(firstLayout.Bounds, restoredLayout.Bounds);
        Assert.Equal(firstLayout.Graphemes.ToArray(), restoredLayout.Graphemes.ToArray());
        Assert.Equal(semiBoldPixels, RenderPixels(renderer, first));

        var semiBoldFrames = await ExportFramesAsync(first, directory, "semibold.mkv");
        var blackFrames = await ExportFramesAsync(second, directory, "black.mkv");
        var regularFrames = await ExportFramesAsync(regular, directory, "legacy-regular.mkv");
        var boldFrames = await ExportFramesAsync(bold, directory, "legacy-bold.mkv");
        for (var index = 0; index < semiBoldFrames.Count; index++)
        {
            var time = new MediaTime(index, 25);
            Assert.Equal(time, semiBoldFrames[index].Time);
            Assert.Equal(time, blackFrames[index].Time);
            Assert.Equal(time, regularFrames[index].Time);
            Assert.Equal(time, boldFrames[index].Time);
            AssertDifferent(semiBoldFrames[index].Luma, blackFrames[index].Luma,
                "对应时间的命名字重视频帧必须不同。");
            AssertDifferent(semiBoldFrames[index].Luma, regularFrames[index].Luma,
                "导出 worker 必须保留 SemiBold，而非只读取 Bold=false。");
            AssertDifferent(blackFrames[index].Luma, boldFrames[index].Luma,
                "导出 worker 必须保留 Black，而非只读取 Bold=true。");
        }

        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    private static ProjectDocument Project(string mediaPath, SystemFontFace face)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: mediaPath);
        var line = new SubtitleLine
        {
            Text = "A你8", End = new(3, 25),
            Style = new()
            {
                FontFamily = face.FamilyName, FontVariant = face.Variant,
                Bold = face.Variant.Weight >= 700, Italic = face.Variant.Italic,
                FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                StrokeWidth = 0, ShadowColor = SceneColor.Transparent, ShadowBlur = 0
            }
        };
        return new()
        {
            Width = SdrPreviewFixture.WIDTH, Height = SdrPreviewFixture.HEIGHT,
            Assets = [asset], Media = new(asset.Id, SdrPreviewFixture.VIDEO_STREAM_INDEX, null, MediaTime.Zero),
            Subtitles = [line],
            Layers = [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }]
        };
    }

    private static ProjectDocument WithStyle(ProjectDocument project, SubtitleStyle style)
    {
        return project with { Subtitles = [project.Subtitles[0] with { Style = style }] };
    }

    private static byte[] RenderPixels(ProjectSceneRenderer renderer, ProjectDocument project)
    {
        using var surface = renderer.Render(project, MediaTime.Zero);
        return surface.CopySrgbBgra();
    }

    private static long AlphaCoverage(byte[] pixels)
    {
        var sum = 0L;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            sum += pixels[index];
        }

        return sum;
    }

    private static void AssertDifferent(byte[] first, byte[] second, string reason)
    {
        Assert.Equal(first.Length, second.Length);
        Assert.False(first.AsSpan().SequenceEqual(second), reason);
    }

    private static async Task<IReadOnlyList<(MediaTime Time, byte[] Luma)>> ExportFramesAsync(
        ProjectDocument project, string directory, string fileName)
    {
        var output = Path.Combine(directory, fileName);
        var request = new VideoExportRequest(project, directory, output)
        {
            Codec = VideoCodec.H264, EncodingMode = VideoEncodingMode.SOFTWARE, DecodeMode = VideoDecodeMode.Software,
            AudioMode = AudioExportMode.None, Quality = 0, Preset = "ultrafast",
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"),
            FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")
        };
        var result = await new VideoExporter().ExportAsync(request);
        Assert.Equal(3UL, result.Frames);
        Assert.Equal("libx264", result.Encoder);
        var frames = new List<(MediaTime Time, byte[] Luma)>();
        using var decoder = FfmpegVideoDecoder.Open(output, 0);
        for (var index = 0; index < 3; index++)
        {
            using var frame = decoder.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(project.Width, frame.Info.Width - frame.Info.CropLeft - frame.Info.CropRight);
            Assert.Equal(project.Height, frame.Info.Height - frame.Info.CropTop - frame.Info.CropBottom);
            Assert.Equal(8, frame.Info.ComponentDepths[0]);
            Assert.NotNull(frame.Info.PresentationTimestamp);
            var source = frame.CopyPlane(0);
            var luma = new byte[project.Width * project.Height];
            var rowBytes = frame.GetPlaneInfo(0).RowBytes;
            for (var y = 0; y < project.Height; y++)
            {
                source.AsSpan((y + frame.Info.CropTop) * rowBytes + frame.Info.CropLeft, project.Width)
                    .CopyTo(luma.AsSpan(y * project.Width, project.Width));
            }
            frames.Add((frame.Info.PresentationTimestamp.ToMediaTime(), luma));
        }
        using var unexpected = decoder.ReadFrame();
        Assert.Null(unexpected);
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
        return frames;
    }
}
