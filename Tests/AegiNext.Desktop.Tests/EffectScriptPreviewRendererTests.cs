using System.Security.Cryptography;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

/// <summary>验证独立脚本样例的真实像素、便携字体和单场景资源边界。</summary>
public sealed class EffectScriptPreviewRendererTests
{
    /// <summary>保持工作台逻辑样例，并以独立输出尺寸验证真实淡入像素。</summary>
    [Fact]
    public void DefaultSceneKeepsItsLogicalSampleWhileTheLargePreviewUsesItsOwnOutputSize()
    {
        var request = Request();
        var scene = EffectPresetPreviewScene.Create(request.Input.Options, request.Input.Source, request.Input.StylePreset, CancellationToken.None);
        Assert.Equal((320, 180), (scene.Document.Width, scene.Document.Height));
        Assert.Equal("YOSYMPH666", Assert.Single(scene.Document.Subtitles).Text);
        Assert.Equal(new MediaTime(1), Assert.Single(scene.Document.Layers).End);
        Assert.Equal(32, scene.Document.Subtitles[0].Style.FontSize);
        Assert.Equal(320, scene.Document.Subtitles[0].Style.Scaling.ReferenceWidth);
        using var renderer = new EffectScriptPreviewRenderer();
        var start = renderer.Render(request, MediaTime.Zero, CancellationToken.None);
        var poster = renderer.Render(request, new(1, 2), CancellationToken.None);

        Assert.Equal((640, 360), (poster.Width, poster.Height));
        Assert.All(start.Pixels.ToArray().Chunk(4), pixel => Assert.Equal(new byte[] { 64, 64, 64, 255 }, pixel));
        Assert.NotEqual(start.Pixels.ToArray(), poster.Pixels.ToArray());
        Assert.Equal(1, renderer.CompilationCount);
    }

    /// <summary>便携字体进入完整分组编译和原生渲染，源样式和字体保持不变。</summary>
    [Fact]
    public void PortableStyleCarriesItsRealFontThroughCompleteGroupedEffectCompilationWithoutChangingTheSource()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Portable", new()
        {
            FontFamily = "Embedded preview fixture", FontSize = 44, StrokeWidth = 0, ShadowColor = SceneColor.Transparent,
            Margins = new(12, 12, 12), Alignment = TextAlignment.MIDDLE_CENTER,
            Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 }
        }, font);
        var input = new EffectScriptPreviewInput(BuiltinEffectScripts.Get("letter-bounce").Source, new() { Text = "ABCD" }, preset);
        var request = Request(input);
        var scene = EffectPresetPreviewScene.Create(input.Options, input.Source, preset, CancellationToken.None);
        var subtitle = Assert.Single(scene.Document.Subtitles);
        Assert.Empty(subtitle.AnimationRanges);
        var instance = Assert.Single(scene.Document.Layers[0].Effects);
        var plan = EffectScriptCompiler.CompileInstance(EffectScriptParser.Parse(instance.Source), instance,
            scene.Document.Layers[0], subtitle);
        Assert.Equal(4, plan.Ranges.Length);
        var asset = Assert.Single(scene.Document.Assets);
        Assert.Equal(asset.Id, subtitle.Style.FontAssetId);
        using var stream = scene.Assets.Open(asset);
        using var copied = new MemoryStream();
        stream.CopyTo(copied);
        Assert.Equal(bytes, copied.ToArray());
        using var renderer = new EffectScriptPreviewRenderer();
        var moving = renderer.Render(request, new(3, 20), CancellationToken.None);
        var resting = renderer.Render(request, new(4, 5), CancellationToken.None);
        var redInput = input with { StylePreset = preset with { Style = preset.Style with { Fill = new(1, 0, 0) } } };
        var red = renderer.Render(Request(redInput), new(4, 5), CancellationToken.None);

        Assert.NotEqual(moving.Pixels.ToArray(), resting.Pixels.ToArray());
        Assert.NotEqual(resting.Pixels.ToArray(), red.Pixels.ToArray());
        Assert.Null(preset.Style.FontAssetId);
        Assert.Same(font, preset.Font);
        Assert.Equal(SceneColor.White, preset.Style.Fill);
        Assert.Equal(2, renderer.CompilationCount);
        Assert.Equal(1, renderer.CachedSceneCount);
    }

    /// <summary>视觉输入替换场景，元数据和显示参数复用编译结果。</summary>
    [Fact]
    public void TextDurationAndActualStyleChangesReplaceTheSceneWhileMetadataDpiAndOutputReuseIt()
    {
        using var renderer = new EffectScriptPreviewRenderer();
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Original", new()
        {
            FontFamily = "sans-serif", FontSize = 32, Alignment = TextAlignment.MIDDLE_CENTER,
            Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 }
        });
        var input = new EffectScriptPreviewInput(BuiltinEffectScripts.Get("fade-in").Source, new(), preset);
        var request = Request(input);
        var first = renderer.Render(request, new(1, 2), CancellationToken.None);
        var renamed = input with
        {
            StylePreset = preset with { Id = Guid.NewGuid(), Name = "Renamed", TimingPostProcessor = new() { LeadInMilliseconds = 777 } }
        };
        var dpi = renderer.Render(new(request.DraftId, 2, renamed, 2, SystemFontCatalog.Empty), new(1, 2), CancellationToken.None);
        var changedOutput = renderer.Render(Request(renamed with { OutputWidth = 320, OutputHeight = 180 }), new(1, 2), CancellationToken.None);
        Assert.Equal((1280, 720), (dpi.Width, dpi.Height));
        Assert.Equal((320, 180), (changedOutput.Width, changedOutput.Height));
        Assert.Equal(1, renderer.CompilationCount);

        var newText = input with { Options = input.Options with { Text = "Z" } };
        var textPixels = renderer.Render(Request(newText), new(1, 2), CancellationToken.None);
        Assert.NotEqual(first.Pixels.ToArray(), textPixels.Pixels.ToArray());
        var shortInput = input with { Options = input.Options with { Duration = new(1, 5) } };
        var shortPixels = renderer.Render(Request(shortInput), new(3, 20), CancellationToken.None);
        var longPixels = renderer.Render(request, new(3, 20), CancellationToken.None);
        Assert.NotEqual(shortPixels.Pixels.ToArray(), longPixels.Pixels.ToArray());
        Assert.Equal(4, renderer.CompilationCount);
        Assert.Equal(1, renderer.CachedSceneCount);
    }

    /// <summary>草稿修订和错误均只缓存当前版本，修复后可继续渲染。</summary>
    [Fact]
    public void RepeatedDraftVersionsKeepOnlyTheCurrentSuccessOrFailureAndRepairResumesRendering()
    {
        using var renderer = new EffectScriptPreviewRenderer();
        var source = BuiltinEffectScripts.Get("fade-in").Source;
        for (var index = 0; index < 70; index++)
        {
            renderer.Render(Request(new(source + "\n# draft " + index, new())), new(1, 2), CancellationToken.None);
            Assert.Equal(1, renderer.CachedSceneCount);
        }
        var invalid = Request(new(EffectPresetPreviewTestSources.RANGE, new()));
        var error = Assert.Throws<EffectScriptException>(() => renderer.Render(invalid, MediaTime.Zero, CancellationToken.None));
        var repeated = Assert.Throws<EffectScriptException>(() => renderer.Render(invalid, new(1, 2), CancellationToken.None));
        Assert.Same(error, repeated);
        Assert.Equal(71, renderer.CompilationCount);
        renderer.Render(Request(), new(1, 2), CancellationToken.None);
        Assert.Equal(72, renderer.CompilationCount);
        Assert.Equal(1, renderer.CachedSceneCount);
    }

    /// <summary>像素预算在排程前拒绝过大输出，取消不留下编译缓存。</summary>
    [Fact]
    public void OutputBudgetRejectsExcessiveDpiBeforeRenderingAndCancelledRequestsCreateNoScene()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectScriptPreviewRequest(Guid.NewGuid(), 1,
            new(BuiltinEffectScripts.Get("fade-in").Source, new()), 100));
        var options = new EffectPresetPreviewOptions { MaximumOutputPixels = 100 };
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(new(BuiltinEffectScripts.Get("fade-in").Source, options)));
        using var renderer = new EffectScriptPreviewRenderer();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => renderer.Render(Request(), MediaTime.Zero, canceled.Token));
        Assert.Equal(0, renderer.CompilationCount);
        Assert.Equal(0, renderer.CachedSceneCount);
    }

    private static EffectScriptPreviewRequest Request(EffectScriptPreviewInput? input = null)
    {
        return new(Guid.NewGuid(), 1, input ?? new(BuiltinEffectScripts.Get("fade-in").Source, new()),
            fontCatalog: SystemFontCatalog.Empty);
    }
}
