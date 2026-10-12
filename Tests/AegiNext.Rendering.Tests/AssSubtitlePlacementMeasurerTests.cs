using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests;

public sealed class AssSubtitlePlacementMeasurerTests
{
    [Fact]
    public void AssExportMeasuresProjectedSizesBeyondStorageLimitsUsingTheValidatedSourceProject()
    {
        var line = new SubtitleLine
        {
            Text = "A", End = new(2),
            Style = new() { FontFamily = "sans-serif", FontSize = 3000 }
        };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End, Transform = new() { Position = new(1, 0) }
        };
        var document = new ProjectDocument
        {
            Width = 3840, Height = 2160, Subtitles = [line], Layers = [layer]
        };
        ProjectValidator.Validate(document);
        using var measurer = new AssSubtitlePlacementMeasurer(Path.GetTempPath());

        var written = AssSubtitleFormat.Write(document, placementMeasurer: measurer);

        Assert.Contains(",6000,", written.Text);
        Assert.Contains(@"\pos(", written.Text);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlacementMeasurement");
        Assert.Same(line, document.Subtitles[0]);
        Assert.Equal(3000, line.Style.FontSize);
        Assert.Same(layer, document.Layers[0]);
    }

    [Fact]
    public void ConversionMetricsUseTheSameInkGeometryAsTheRenderedSubtitle()
    {
        var line = new SubtitleLine
        {
            Text = "  Layout\n  ABC  ",
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontFamily = "sans-serif", Position = new()
                {
                    Anchor = new(0.3, 0.7), Pivot = new(0.2, 0.8), Offset = new(12, -9)
                }
            }
        };
        var document = new ProjectDocument
        {
            Width = 640, Height = 360, Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        ProjectValidator.Validate(document);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(Path.GetTempPath()));
        using var measurer = new AssSubtitlePlacementMeasurer(Path.GetTempPath());

        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var metrics = measurer.Measure(document, line);

        Assert.Equal(new ScenePoint(layout.BasePosition.X, layout.BasePosition.Y), metrics.BasePosition);
        Assert.Equal(new ScenePoint(layout.Pivot.X, layout.Pivot.Y), metrics.Pivot);
        Assert.Equal(new ScenePoint(layout.Bounds.Left, layout.Bounds.Top), metrics.BoundsOrigin);
        Assert.Equal(new ScenePoint(layout.Bounds.Width, layout.Bounds.Height), metrics.BoundsSize);
        Assert.True(metrics.BoundsSize.X > 0);
        Assert.True(metrics.BoundsSize.Y > 0);
    }

    [Fact]
    public void InvalidProjectFontDoesNotSilentlyProduceApproximateConversionMetrics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-ass-placement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "broken.ttf"), [1, 2, 3]);
            var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "broken.ttf");
            var line = new SubtitleLine { Text = "ABC", Style = new() { FontAssetId = asset.Id } };
            var document = new ProjectDocument
            {
                Assets = [asset], Subtitles = [line],
                Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
            };
            ProjectValidator.Validate(document);
            using var measurer = new AssSubtitlePlacementMeasurer(directory);

            var error = Assert.Throws<InvalidDataException>(() => measurer.Measure(document, line));
            Assert.Equal("无法打开项目字体。", error.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
