using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleStylePositionMeasurementTests
{
    [Fact]
    public async Task ConcurrentPlacementRequestsShareOneMeasuredResult()
    {
        var subtitle = new SubtitleLine { Text = "Concurrent layout", Style = new() { FontFamily = "sans-serif" } };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End };
        var document = new ProjectDocument { Subtitles = [subtitle], Layers = [layer] };
        using var resolver = new LayerPlacementResolver();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return resolver.Resolve(document, Path.GetTempPath(), layer);
        })).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Null(results[0].Error);
        Assert.NotNull(results[0].Geometry);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public void EmbeddedFontMeasurementsUseActualInkAndCurrentCanvasWithoutWritingAssets()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Example", new()
        {
            Scaling = new() { ReferenceWidth = 256, ReferenceHeight = 160 },
            FontFamily = "Embedded test font", FontSize = 22, Margins = new(8, 8, 8),
            StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        }, font);

        var measured = SubtitleStylePositionMeasurer.Measure(preset, 256, 160, "  ABC  \n  DEF  ");
        var plain = SubtitleStylePositionMeasurer.Measure(preset, 256, 160, "ABC\nDEF");
        var largerCanvas = SubtitleStylePositionMeasurer.Measure(preset, 1920, 1080, "  ABC  \n  DEF  ");

        Assert.Null(measured.Error);
        Assert.NotNull(measured.Position);
        Assert.True(measured.Geometry!.HasInk);
        Assert.Equal(new ScenePoint(256, 160), measured.Geometry.ParentSize);
        Assert.InRange(measured.Geometry.GlyphSize.X, 20, 100);
        Assert.InRange(measured.Geometry.GlyphSize.Y, 20, 80);
        Assert.InRange(Math.Abs(plain.Geometry!.GlyphSize.X - measured.Geometry.GlyphSize.X), 0, 0.001);
        Assert.InRange(Math.Abs(plain.Geometry.GlyphSize.Y - measured.Geometry.GlyphSize.Y), 0, 0.001);
        Assert.Equal(new ScenePoint(1920, 1080), largerCanvas.Geometry!.ParentSize);
        var scale = Math.Sqrt(1920.0 / 256 * (1080.0 / 160));
        var canvasPreset = preset with { Style = preset.Style with
        {
            Scaling = new() { ReferenceWidth = 1920, ReferenceHeight = 1080 },
            FontSize = 22 * scale, Margins = new(8 * scale, 8 * scale, 8 * scale)
        } };
        var expectedLarge = SubtitleStylePositionMeasurer.Measure(canvasPreset, 1920, 1080, "  ABC  \n  DEF  ");
        Assert.Equal(scale, largerCanvas.Geometry.OffsetScale, 10);
        Assert.Equal(expectedLarge.Geometry!.GlyphSize, largerCanvas.Geometry.GlyphSize);
        Assert.True(largerCanvas.Geometry.GlyphSize.X > measured.Geometry.GlyphSize.X * 5);
        Assert.True(largerCanvas.Geometry.GlyphSize.Y > measured.Geometry.GlyphSize.Y * 5);
        Assert.Null(preset.Style.FontAssetId);
        Assert.Null(preset.Style.Position);
        Assert.Same(font, preset.Font);
    }

    [Fact]
    public void PlacementResolverSeparatesReferenceOffsetFromCanvasGeometry()
    {
        var position = new SubtitlePosition { Anchor = new(0.5, 1), Pivot = new(0.5, 1), Offset = new(10, -20) };
        var line = new SubtitleLine { Text = "ABC", Style = new()
        {
            Position = position, FontSize = 24, FontFamily = "sans-serif",
            Scaling = new() { ReferenceWidth = 400, ReferenceHeight = 240 }
        } };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var document = new ProjectDocument { Width = 800, Height = 480, Subtitles = [line], Layers = [layer] };
        using var resolver = new LayerPlacementResolver();

        var result = resolver.Resolve(document, Path.GetTempPath(), layer);

        Assert.Null(result.Error);
        Assert.Equal(position, result.Position);
        Assert.Equal(new ScenePoint(420, 440), result.BasePosition);
        Assert.Equal(new ScenePoint(800, 480), result.Geometry!.ParentSize);
        Assert.Equal(2, result.Geometry.OffsetScale);
    }

    [Fact]
    public void InvalidFontReportsADiagnosticAndFontRepairRestoresPresetSelection()
    {
        var font = new EmbeddedSubtitleFont("Broken.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])), [1, 2, 3]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Broken example", new() { FontFamily = "Broken embedded face" }, font);
        var model = new StyleSettingsViewModel();
        model.SetPositionMeasurement(value => SubtitleStylePositionMeasurer.Measure(value, 320, 180, "Subtitle Preview"));
        model.UpdateStyles([preset]);

        Assert.True(model.HasPositionMeasurementError);
        Assert.False(string.IsNullOrWhiteSpace(model.PositionMeasurementError));
        Assert.False(model.Position.CanSelectPreset);
        Assert.Same(font, model.Draft!.Font);
        Assert.Null(model.Draft.Style.Position);

        model.CommitFont("sans-serif");

        Assert.False(model.HasPositionMeasurementError);
        Assert.Null(model.PositionMeasurementError);
        Assert.Null(model.Draft.Font);
        Assert.True(model.Position.CanSelectPreset);
        Assert.NotNull(model.Position.Geometry);
        Assert.True(model.Position.SelectPreset(new(0.5, 0.5)));
        Assert.Equal(new ScenePoint(0.5, 0.5), model.Draft.Style.Position!.Anchor);
    }

    [Fact]
    public void CorruptProjectFontDisablesMeasuredPlacementAndRepairRestoresItWithoutChangingTheSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aegi-position-font-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "broken.ttf"), [1, 2, 3]);
            var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "broken.ttf");
            var subtitle = new SubtitleLine
            {
                Text = "ABC", Style = new() { FontAssetId = font.Id, FontFamily = "Broken face", Position = new() }
            };
            var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End };
            var document = new ProjectDocument { Assets = [font], Subtitles = [subtitle], Layers = [layer] };
            using var resolver = new LayerPlacementResolver();

            var failed = resolver.Resolve(document, directory, layer);

            Assert.IsType<InvalidDataException>(failed.Error);
            Assert.Null(failed.BasePosition);
            Assert.Null(failed.Geometry);
            Assert.Equal(font.Id, document.Subtitles[0].Style.FontAssetId);
            Assert.Same(subtitle.Style.Position, document.Subtitles[0].Style.Position);
            var repaired = document with
            {
                Subtitles = [subtitle with { Style = subtitle.Style with { FontAssetId = null, FontFamily = "sans-serif" } }]
            };
            var measured = resolver.Resolve(repaired, directory, layer);
            Assert.Null(measured.Error);
            Assert.NotNull(measured.BasePosition);
            Assert.True(measured.Geometry!.HasInk);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
