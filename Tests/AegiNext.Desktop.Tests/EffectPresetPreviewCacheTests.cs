using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

/// <summary>验证静态缩略图缓存的身份边界和字节 LRU 预算。</summary>
public sealed class EffectPresetPreviewCacheTests
{
    /// <summary>源、场景选项、像素尺寸和不可变字体目录都参与静态缓存身份。</summary>
    [Fact]
    public void CacheIdentityIncludesSourceOptionsDpiAndFontCatalogButNotCardIdentity()
    {
        var options = new EffectPresetPreviewOptions();
        var first = EffectPresetPreviewTestSources.Request("a");
        var firstKey = EffectPresetPreviewCacheKey.Create(options, first);
        var equivalent = EffectPresetPreviewTestSources.Request("other", revision: 17);

        Assert.Equal(firstKey, EffectPresetPreviewCacheKey.Create(options, equivalent));
        Assert.Equal(first.SourceHash, equivalent.SourceHash);
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options, EffectPresetPreviewTestSources.Request(source: first.Source + "\n")));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options, EffectPresetPreviewTestSources.Request(scaling: 2)));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options with { Text = "Other" }, first));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options with { Duration = new(5) }, first));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options with { FontSize = 48 }, first));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options with { BackgroundBgra = 0xFF112233 }, first));
        Assert.NotEqual(firstKey, EffectPresetPreviewCacheKey.Create(options,
            EffectPresetPreviewTestSources.Request(catalog: new SystemFontCatalog(SKFontManager.Default, []))));
    }

    /// <summary>命中更新 LRU，淘汰最久未用的缩略图且控制实际像素预算。</summary>
    [Fact]
    public void ByteBudgetEvictsLeastRecentlyUsedFramesAndReplacementsDoNotAccumulate()
    {
        var cache = new EffectPresetPreviewFrameCache(600);
        var a = Key("a");
        var b = Key("b");
        var c = Key("c");
        var frame = new SdrVideoFrame(1, 1, [0, 0, 0, 255]);
        cache.Add(a, frame);
        cache.Add(b, frame);
        Assert.True(cache.TryGet(a, out var cached));
        Assert.Same(frame, cached);
        cache.Add(c, frame);

        Assert.True(cache.TryGet(a, out _));
        Assert.False(cache.TryGet(b, out _));
        Assert.True(cache.TryGet(c, out _));
        Assert.Equal(2, cache.Statistics.Count);
        Assert.InRange(cache.Statistics.Bytes, frame.Pixels.Length * 2, 600);
        var beforeReplacement = cache.Statistics.Bytes;
        cache.Add(a, new(1, 1, [255, 255, 255, 255]));
        Assert.Equal(beforeReplacement, cache.Statistics.Bytes);
        Assert.True(cache.TryGet(a, out var replacement));
        Assert.Equal(255, replacement.Pixels.Span[0]);
        cache.Clear();
        Assert.Equal(0, cache.Statistics.Count);
        Assert.Equal(0, cache.Statistics.Bytes);
    }

    /// <summary>关闭缓存或单张超过预算时不保留图像，避免无界内存回退。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void FramesLargerThanTheBudgetAreNotRetained(long budget)
    {
        var cache = new EffectPresetPreviewFrameCache(budget);
        cache.Add(Key("a"), new(1, 1, [0, 0, 0, 255]));

        Assert.False(cache.TryGet(Key("a"), out _));
        Assert.Equal(0, cache.Statistics.Bytes);
    }

    /// <summary>异常 DPI 在队列开始前明确失败，不分配超上限图像。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void InvalidOrExcessiveOutputScalingFailsBeforeAllocating(double scaling)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectPresetPreviewCacheKey.GetOutputSize(new(), scaling));
    }

    private static EffectPresetPreviewCacheKey Key(string source)
    {
        return EffectPresetPreviewCacheKey.Create(new(), EffectPresetPreviewTestSources.Request(source: source));
    }
}
