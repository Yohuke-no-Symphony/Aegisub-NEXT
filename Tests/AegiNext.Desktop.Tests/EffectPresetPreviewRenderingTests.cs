using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

/// <summary>使用真实离屏工程渲染验证动画像素、DPI 和源版本编译复用。</summary>
public sealed class EffectPresetPreviewRenderingTests
{
    /// <summary>淡入在不同精确时刻产生真实像素变化，背景保持不透明灰色。</summary>
    [Fact]
    public void WholeSubtitleAnimationChangesRealPixelsAgainstTheConfiguredBackground()
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var request = EffectPresetPreviewTestSources.Request();
        var start = renderer.Render(request, MediaTime.Zero, CancellationToken.None);
        var middle = renderer.Render(request, new(3, 20), CancellationToken.None);
        var poster = renderer.Render(request, options.PosterTime, CancellationToken.None);

        Assert.All(start.Pixels.ToArray().Chunk(4), pixel => Assert.Equal(new byte[] { 64, 64, 64, 255 }, pixel));
        Assert.NotEqual(start.Pixels.ToArray(), middle.Pixels.ToArray());
        Assert.NotEqual(middle.Pixels.ToArray(), poster.Pixels.ToArray());
        Assert.Contains(poster.Pixels.ToArray().Chunk(4), pixel => pixel[0] > 100 && pixel[1] > 100 && pixel[2] > 100);
        Assert.All(poster.Pixels.ToArray().Chunk(4), pixel => Assert.Equal(255, pixel[3]));
        Assert.Equal(1, renderer.CompilationCount);
    }

    /// <summary>一秒样例的退场区间使用真实退出轨道，返回零时刻后不遗留末帧。</summary>
    [Theory]
    [InlineData("fade-out")]
    [InlineData("pop-out")]
    [InlineData("slide-out")]
    [InlineData("fade-in-out")]
    public void WholeSubtitleOutrosChangeRealPixelsNearTheEndAndResetOnLoop(string id)
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var request = EffectPresetPreviewTestSources.Request(source: BuiltinEffectScripts.Get(id).Source);
        var start = renderer.Render(request, MediaTime.Zero, CancellationToken.None);
        var poster = renderer.Render(request, options.PosterTime, CancellationToken.None);
        var outro = renderer.Render(request, new(9, 10), CancellationToken.None);
        var last = renderer.Render(request, new(23, 24), CancellationToken.None);
        var ended = renderer.Render(request, options.Duration, CancellationToken.None);
        var loop = renderer.Render(request, MediaTime.Zero, CancellationToken.None);

        CaptureOutroFrame(id, "0", start);
        CaptureOutroFrame(id, "0.5", poster);
        CaptureOutroFrame(id, "0.9", outro);
        CaptureOutroFrame(id, "0.958", last);
        Assert.NotEqual(poster.Pixels.ToArray(), outro.Pixels.ToArray());
        Assert.NotEqual(outro.Pixels.ToArray(), last.Pixels.ToArray());
        Assert.True(InkSignal(last) < InkSignal(poster));
        Assert.Equal(0, InkSignal(ended));
        Assert.Equal(start.Pixels.ToArray(), loop.Pixels.ToArray());
        Assert.Equal(1, renderer.CompilationCount);
    }

    /// <summary>逐字结果的生成范围参与真实绘制，不能只保留整句轨道。</summary>
    [Theory]
    [InlineData("letter-bounce")]
    [InlineData("letter-pulse")]
    public void GroupedSubtitleAnimationRendersItsGeneratedRanges(string id)
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var request = EffectPresetPreviewTestSources.Request(source: BuiltinEffectScripts.Get(id).Source);
        var resting = renderer.Render(request, options.PosterTime, CancellationToken.None);
        var animated = renderer.Render(request, new(3, 20), CancellationToken.None);

        Assert.NotEqual(resting.Pixels.ToArray(), animated.Pixels.ToArray());
        Assert.Equal(1, renderer.CompilationCount);
    }

    /// <summary>宿主 DPI 只放大输出像素，并复用相同源版本的隔离编译结果。</summary>
    [Fact]
    public void DpiAndFontCatalogChangesReuseTheFrozenSourceCompilation()
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var first = renderer.Render(EffectPresetPreviewTestSources.Request(), options.PosterTime, CancellationToken.None);
        var second = renderer.Render(EffectPresetPreviewTestSources.Request(scaling: 2), options.PosterTime, CancellationToken.None);
        var otherCatalog = new SystemFontCatalog(SKFontManager.Default, []);
        var third = renderer.Render(EffectPresetPreviewTestSources.Request(revision: 2, catalog: otherCatalog), options.PosterTime, CancellationToken.None);

        Assert.Equal((320, 180), (first.Width, first.Height));
        Assert.Equal((640, 360), (second.Width, second.Height));
        Assert.Equal((320, 180), (third.Width, third.Height));
        Assert.InRange(NonBackgroundPixels(second) / (double)NonBackgroundPixels(first), 3.5, 4.5);
        Assert.Equal(1, renderer.CompilationCount);
    }

    /// <summary>超过六十四个源版本往返时，成功场景和失败诊断在工作者生命周期内均只编译一次。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySourceVersionCompilesOnlyOnceAcrossLargeCatalogRevisits(bool invalid)
    {
        const int SOURCE_VERSIONS = 70;
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var source = invalid ? EffectPresetPreviewTestSources.RANGE : BuiltinEffectScripts.Get("fade-in").Source;
        var versions = Enumerable.Range(0, SOURCE_VERSIONS).Select(index => EffectPresetPreviewTestSources.Request(
            "version-" + index, revision: index, source: source + "\n# source version " + index)).ToArray();
        foreach (var request in versions)
        {
            RenderVersion(renderer, request, options.PosterTime, invalid);
        }

        Assert.Equal(SOURCE_VERSIONS, renderer.CompilationCount);
        var otherCatalog = new SystemFontCatalog(SKFontManager.Default, []);
        foreach (var request in versions)
        {
            var revisited = EffectPresetPreviewTestSources.Request("revisited-" + request.PresetId,
                revision: request.Revision + SOURCE_VERSIONS, scaling: 2, source: request.Source, catalog: otherCatalog);
            RenderVersion(renderer, revisited, new(1), invalid);
        }

        Assert.Equal(SOURCE_VERSIONS, renderer.CompilationCount);
        Assert.Equal(SOURCE_VERSIONS, renderer.CachedSceneCount);
        var changed = EffectPresetPreviewTestSources.Request(source: source + "\n# new source version");
        RenderVersion(renderer, changed, options.PosterTime, invalid);
        Assert.Equal(SOURCE_VERSIONS + 1, renderer.CompilationCount);
    }

    /// <summary>同源失败不会重复编译，修订后的有效源取得独立缓存项。</summary>
    [Fact]
    public void InvalidSourcesCacheTheirSpecificFailureWithoutPoisoningNewVersions()
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        var invalid = EffectPresetPreviewTestSources.Request(source: EffectPresetPreviewTestSources.RANGE);
        var first = Assert.Throws<EffectScriptException>(() => renderer.Render(invalid, options.PosterTime, CancellationToken.None));
        var second = Assert.Throws<EffectScriptException>(() => renderer.Render(invalid, new(1), CancellationToken.None));

        Assert.Equal(first.Message, second.Message);
        Assert.Contains("完整字素范围", second.Message, StringComparison.Ordinal);
        Assert.Equal(1, renderer.CompilationCount);
        var valid = renderer.Render(EffectPresetPreviewTestSources.Request(revision: 2), options.PosterTime, CancellationToken.None);
        Assert.Equal(320, valid.Width);
        Assert.Equal(2, renderer.CompilationCount);
    }

    /// <summary>取消请求不建立编译缓存，后续有效请求仍可正常输出。</summary>
    [Fact]
    public void CancelledRequestsDoNotCreateScenesOrFrames()
    {
        var options = new EffectPresetPreviewOptions();
        using var renderer = new EffectPresetPreviewRenderer(options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => renderer.Render(EffectPresetPreviewTestSources.Request(), options.PosterTime, cancellation.Token));
        Assert.Equal(0, renderer.CompilationCount);
        Assert.Equal(0, renderer.CachedSceneCount);
    }

    /// <summary>背景色属于样例选项，并在真实不透明帧中使用。</summary>
    [Fact]
    public void ExplicitBackgroundOptionsAreUsedByTheRealRenderer()
    {
        using var renderer = new EffectPresetPreviewRenderer(new() { BackgroundBgra = 0xFF332211 });
        var frame = renderer.Render(EffectPresetPreviewTestSources.Request(), MediaTime.Zero, CancellationToken.None);

        Assert.All(frame.Pixels.ToArray().Chunk(4), pixel => Assert.Equal(new byte[] { 17, 34, 51, 255 }, pixel));
    }

    private static int NonBackgroundPixels(SdrVideoFrame frame)
    {
        return frame.Pixels.ToArray().Chunk(4).Count(pixel => pixel[0] != 64 || pixel[1] != 64 || pixel[2] != 64);
    }

    private static long InkSignal(SdrVideoFrame frame)
    {
        var pixels = frame.Pixels.Span;
        var signal = 0L;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            signal += Math.Abs(pixels[index] - 64) + Math.Abs(pixels[index + 1] - 64) + Math.Abs(pixels[index + 2] - 64);
        }
        return signal;
    }

    private static void CaptureOutroFrame(string id, string time, SdrVideoFrame frame)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_PREVIEW_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        using var srgb = SKColorSpace.CreateSrgb();
        using var image = SKImage.FromPixelCopy(new(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb),
            frame.Pixels.Span, frame.Width * 4);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, id + "-" + time + ".png"));
        data.SaveTo(file);
    }

    private static void RenderVersion(EffectPresetPreviewRenderer renderer, EffectPresetPreviewRequest request,
        MediaTime position, bool invalid)
    {
        if (invalid)
        {
            Assert.Throws<EffectScriptException>(() => renderer.Render(request, position, CancellationToken.None));
        }
        else
        {
            _ = renderer.Render(request, position, CancellationToken.None);
        }
    }
}
