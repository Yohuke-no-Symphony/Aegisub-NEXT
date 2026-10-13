using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests;

/// <summary>验证统一示例的隔离内容、样式参考和真实脚本前置条件。</summary>
public sealed class EffectPresetPreviewSceneTests
{
    /// <summary>默认场景使用约定文字、三秒时长和独立的逻辑样式参考。</summary>
    [Fact]
    public void DefaultSceneFreezesTheStandardExampleAndSamplesItsMidpoint()
    {
        var options = new EffectPresetPreviewOptions();
        var scene = EffectPresetPreviewScene.Create(options, BuiltinEffectScripts.Get("fade-in").Source, CancellationToken.None);
        var subtitle = Assert.Single(scene.Document.Subtitles);
        var layer = Assert.Single(scene.Document.Layers);

        Assert.Equal("YOSYMPH666", subtitle.Text);
        Assert.Equal(new MediaTime(3), subtitle.End);
        Assert.Equal(new MediaTime(3, 2), options.PosterTime);
        Assert.Equal(320, scene.Document.Width);
        Assert.Equal(180, scene.Document.Height);
        Assert.Equal("sans-serif", subtitle.Style.FontFamily);
        Assert.Equal(32, subtitle.Style.FontSize);
        Assert.Equal(TextAlignment.MIDDLE_CENTER, subtitle.Style.Alignment);
        Assert.Equal(new SubtitleMargins(16, 16, 16), subtitle.Style.Margins);
        Assert.Equal(320, subtitle.Style.Scaling.ReferenceWidth);
        Assert.Equal(180, subtitle.Style.Scaling.ReferenceHeight);
        Assert.Equal(subtitle.Id, layer.SubtitleId);
        Assert.Equal(subtitle.End, layer.End);
        Assert.Null(layer.MotionPath);
        Assert.Null(layer.Mask);
        Assert.NotEmpty(layer.Tracks);
        Assert.Equal(0xFF404040u, options.BackgroundBgra);
        Assert.Equal(24, options.FramesPerSecond);
        Assert.Equal(32L * 1024 * 1024, options.StaticCacheBytes);
    }

    /// <summary>新时长与画布派生静态采样点，同时各次编译保持独立工程身份。</summary>
    [Fact]
    public void CustomizedScenesRemainIndependentAndDeriveTheirPosterTime()
    {
        var options = new EffectPresetPreviewOptions
        {
            Duration = new(5),
            CanvasWidth = 640,
            CanvasHeight = 360,
            FontFamily = "Example font",
            FontSize = 48,
            Margin = 24
        };
        var source = BuiltinEffectScripts.Get("fade-in").Source;
        var first = EffectPresetPreviewScene.Create(options, source, CancellationToken.None);
        var second = EffectPresetPreviewScene.Create(options, source, CancellationToken.None);

        Assert.Equal(new MediaTime(5, 2), options.PosterTime);
        Assert.NotEqual(first.Document.Layers[0].Id, second.Document.Layers[0].Id);
        Assert.NotEqual(first.Document.Subtitles[0].Id, second.Document.Subtitles[0].Id);
        Assert.Equal(640, first.Document.Subtitles[0].Style.Scaling.ReferenceWidth);
        Assert.Equal(360, first.Document.Subtitles[0].Style.Scaling.ReferenceHeight);
        Assert.Equal("Example font", first.Document.Subtitles[0].Style.FontFamily);
        Assert.Equal(new MediaTime(5), first.Document.Layers[0].End);
    }

    /// <summary>整句和逐字内置脚本通过同一业务应用管线生成完整工程。</summary>
    [Theory]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("fade-in-out")]
    [InlineData("pop-in")]
    [InlineData("pop-out")]
    [InlineData("slide-in")]
    [InlineData("slide-out")]
    [InlineData("letter-bounce")]
    [InlineData("letter-pulse")]
    public void BuiltinEffectsCompileTheirCompleteLayerAndSubtitleResults(string id)
    {
        var scene = EffectPresetPreviewScene.Create(new(), BuiltinEffectScripts.Get(id).Source, CancellationToken.None);
        ProjectValidator.Validate(scene.Document);

        Assert.NotEmpty(scene.Document.Layers[0].Tracks);
        if (id.StartsWith("letter-", StringComparison.Ordinal))
        {
            Assert.Equal(10, scene.Document.Subtitles[0].AnimationRanges.Length);
        }
    }

    /// <summary>示例缺少几何、范围或时长时保留真实诊断，不补造依赖。</summary>
    [Theory]
    [InlineData(EffectPresetPreviewTestSources.MASK, "蒙版")]
    [InlineData(EffectPresetPreviewTestSources.RANGE, "完整字素范围")]
    [InlineData(EffectPresetPreviewTestSources.LONG, "时长")]
    public void UnsatisfiedSampleRequirementsReportTheBusinessFailure(string source, string message)
    {
        var error = Record.Exception(() => EffectPresetPreviewScene.Create(new(), source, CancellationToken.None));

        Assert.NotNull(error);
        Assert.True(error is EffectScriptException or InvalidDataException, error.GetType().FullName);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    /// <summary>路径前置失败携带语义类型与脚本位置，不依赖当前界面语言。</summary>
    [Fact]
    public void MissingMotionPathReportsATypedLocalizableRequirement()
    {
        var error = Assert.Throws<EffectPresetPreviewRequirementException>(() =>
            EffectPresetPreviewScene.Create(new(), EffectPresetPreviewTestSources.PATH, CancellationToken.None));

        Assert.Equal(EffectPresetPreviewRequirement.MOTION_PATH, error.Requirement);
        Assert.Equal(4, error.Line);
        Assert.True(error.Column > 0);
    }

    /// <summary>已取消的请求不解析或生成新场景。</summary>
    [Fact]
    public void CancelledSampleCreationDoesNotCompile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => EffectPresetPreviewScene.Create(new(), "invalid source", cancellation.Token));
    }
}
