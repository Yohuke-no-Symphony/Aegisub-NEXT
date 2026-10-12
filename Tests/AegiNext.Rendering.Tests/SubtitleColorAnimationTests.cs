using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleColorAnimationTests
{
    [Theory]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    public void ActualSubtitlePixelsUseWholeLinearColorAndAlphaAtEachAnimationTime(AnimationProperty property)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var subtitle = new SubtitleLine
        {
            Text = "MMMM", End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 256, ReferenceHeight = 96 },
                FontAssetId = font.Id, FontSize = 40, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                Fill = property == AnimationProperty.FILL ? new(0, 1, 0) : SceneColor.Transparent,
                Stroke = property == AnimationProperty.STROKE ? new(0, 1, 0) : SceneColor.Transparent,
                StrokeWidth = 4, ShadowColor = SceneColor.Transparent
            }
        };
        var document = new ProjectDocument
        {
            Width = 256, Height = 96, Assets = [font], Subtitles = [subtitle],
            Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End,
                Tracks = [new(property, [new(new(0), new SceneColor(2, 0, 0, 0.5)), new(new(2), new SceneColor(0, 0, 4, 0.75))])]
            }]
        };
        ProjectValidator.Validate(document);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = new byte[document.Width * document.Height * 4];
        for (var index = 3; index < background.Length; index += 4)
        {
            background[index] = 255;
        }
        for (var second = 0; second <= 2; second++)
        {
            var time = new MediaTime(second);
            using var surface = renderer.Render(document, time);
            var pixels = new Half[surface.Info.ChannelCount];
            surface.CopyPixels(pixels);
            var alpha = 0.5 + second * 0.125;
            var sample = 0;
            for (var index = 4; index < pixels.Length; index += 4)
            {
                if (Math.Abs((float)pixels[index + 3] - alpha) < Math.Abs((float)pixels[sample + 3] - alpha))
                {
                    sample = index;
                }
            }
            Assert.InRange((float)pixels[sample + 3], alpha - 0.005, alpha + 0.005);
            Assert.InRange((float)pixels[sample], (2 - second) * alpha - 0.01, (2 - second) * alpha + 0.01);
            Assert.InRange((float)pixels[sample + 2], second * 2 * alpha - 0.01, second * 2 * alpha + 0.01);
            Assert.Equal((Half)0, pixels[sample + 1]);
            var preview = renderer.ComposePreview(document, time, background, document.Width, document.Height, document.Width * 4);
            if (second == 0)
            {
                Assert.True(preview[sample + 2] > preview[sample] + 100);
            }
            else if (second == 2)
            {
                Assert.True(preview[sample] > preview[sample + 2] + 100);
            }
        }
    }
}
