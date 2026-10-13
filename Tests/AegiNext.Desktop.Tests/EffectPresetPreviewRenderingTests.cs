using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

/// <summary>使用真实离屏工程渲染验证动画像素、DPI 和有界编译缓存。</summary>
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

    /// <summary>源版本按 LRU 淘汰，淘汰后重新访问才会再次编译。</summary>
    [Fact]
    public void CompiledScenesAreBoundedAndRefreshedByUse()
    {
        var options = new EffectPresetPreviewOptions { MaximumCompiledScenes = 2 };
        using var renderer = new EffectPresetPreviewRenderer(options);
        var a = EffectPresetPreviewTestSources.Request("a", source: BuiltinEffectScripts.Get("fade-in").Source);
        var b = EffectPresetPreviewTestSources.Request("b", source: BuiltinEffectScripts.Get("fade-out").Source);
        var c = EffectPresetPreviewTestSources.Request("c", source: BuiltinEffectScripts.Get("pop-in").Source);
        _ = renderer.Render(a, options.PosterTime, CancellationToken.None);
        _ = renderer.Render(b, options.PosterTime, CancellationToken.None);
        _ = renderer.Render(a, options.PosterTime, CancellationToken.None);
        _ = renderer.Render(c, options.PosterTime, CancellationToken.None);

        Assert.Equal(3, renderer.CompilationCount);
        Assert.Equal(2, renderer.CachedSceneCount);
        _ = renderer.Render(a, options.PosterTime, CancellationToken.None);
        Assert.Equal(3, renderer.CompilationCount);
        _ = renderer.Render(b, options.PosterTime, CancellationToken.None);
        Assert.Equal(4, renderer.CompilationCount);
        Assert.Equal(2, renderer.CachedSceneCount);
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
}
