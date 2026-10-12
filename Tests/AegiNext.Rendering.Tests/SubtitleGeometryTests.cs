using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleGeometryTests
{
    [Theory]
    [InlineData("Ajg", TextAlignment.BOTTOM_CENTER, 0)]
    [InlineData("  ABC  \n  Ajg  ", TextAlignment.MIDDLE_CENTER, 0)]
    [InlineData("Ajg", TextAlignment.TOP_LEFT, 8)]
    [InlineData("Ajg", TextAlignment.TOP_RIGHT, -8)]
    public void AutomaticHorizontalAlignmentUsesInkAndProducesExactAnchorOffset(string text,
        TextAlignment alignment, double expectedOffset)
    {
        var (document, layer) = CreateDocument(text);
        document = document with
        {
            Subtitles = [document.Subtitles[0] with
            {
                Style = document.Subtitles[0].Style with { Alignment = alignment }
            }]
        };
        using var renderer = CreateRenderer();
        var measurement = renderer.MeasureSubtitlePlacement(document, document.Subtitles[0]);
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        var horizontal = (int)alignment % 3;
        var actualAnchor = measurement.Bounds.Left + (float)measurement.Position.Pivot.X * measurement.Bounds.Width;
        var expectedAnchor = horizontal switch
        {
            0 => 8,
            1 => document.Width / 2f,
            _ => document.Width - 8
        };

        Assert.Equal(expectedOffset, measurement.Position.Offset.X);
        Assert.InRange(Math.Abs(actualAnchor - expectedAnchor), 0, 0.001f);
        Assert.Equal(expectedAnchor, geometry.BasePosition.X);

        var explicitDocument = document with
        {
            Subtitles = [document.Subtitles[0] with
            {
                Style = document.Subtitles[0].Style with { Position = measurement.Position }
            }]
        };
        using var automaticSurface = renderer.Render(document, MediaTime.Zero);
        using var explicitSurface = renderer.Render(explicitDocument, MediaTime.Zero);
        var automaticPixels = new Half[automaticSurface.Info.ChannelCount];
        var explicitPixels = new Half[explicitSurface.Info.ChannelCount];
        automaticSurface.CopyPixels(automaticPixels);
        explicitSurface.CopyPixels(explicitPixels);
        Assert.Equal(automaticPixels, explicitPixels);
    }

    [Fact]
    public void ExplicitAnchorUsesActualGlyphPivotUnderClipRotationAndScale()
    {
        var (document, layer) = CreateDocument("ABC\nDEF");
        var subtitle = document.Subtitles[0];
        document = document with
        {
            Subtitles = [subtitle with { Style = subtitle.Style with
            {
                Position = new() { Anchor = new(0.25, 0.5), Pivot = new(0.5, 0.5), Offset = new(13, -7) }
            } }],
            Layers = [layer with { Transform = new(X: 9, Y: 5, Rotation: 90, ScaleX: 1.5, ScaleY: 0.75) }]
        };
        using var renderer = CreateRenderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;

        Assert.True(geometry.HasInk);
        Assert.InRange(geometry.LocalBounds.Width, 20, 100);
        Assert.InRange(geometry.LocalBounds.Height, 20, 80);
        Assert.Equal(new SKPoint(77, 73), geometry.BasePosition);
        Assert.InRange(geometry.WorldPivot.X, 85.999f, 86.001f);
        Assert.InRange(geometry.WorldPivot.Y, 77.999f, 78.001f);
        Assert.Equal(4, geometry.WorldCorners.Count);
        Assert.Null(renderer.GetLayerGeometry(document, layer.End, layer.Id));
    }

    [Fact]
    public void AutomaticAlignmentCanBecomeExplicitWithoutMovingAnyRenderedPixel()
    {
        var (document, _) = CreateDocument("  Ajg\nDEF  ");
        using var renderer = CreateRenderer();
        using var before = renderer.Render(document, MediaTime.Zero);
        var position = renderer.ResolveSubtitlePosition(document, document.Subtitles[0]);
        var converted = document with
        {
            Subtitles = [document.Subtitles[0] with { Style = document.Subtitles[0].Style with { Position = position } }]
        };
        using var after = renderer.Render(converted, MediaTime.Zero);
        var first = new Half[before.Info.ChannelCount];
        var second = new Half[after.Info.ChannelCount];
        before.CopyPixels(first);
        after.CopyPixels(second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GeometryBoundsTrackVisiblePixelsAndExcludeWhitespaceAdvance()
    {
        var (document, layer) = CreateDocument("  ABC  ");
        var subtitle = document.Subtitles[0];
        document = document with
        {
            Subtitles = [subtitle with { Style = subtitle.Style with
            {
                Position = new() { Anchor = new(0, 0), Pivot = new(0, 0), Offset = new(40, 30) }
            } }]
        };
        using var renderer = CreateRenderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        var occupied = new List<SKPoint>();
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                if ((float)pixels[(y * document.Width + x) * 4 + 3] > 0.01f)
                {
                    occupied.Add(new(x, y));
                }
            }
        }

        Assert.NotEmpty(occupied);
        Assert.InRange(occupied.Min(point => point.X), 39, 41);
        Assert.InRange(occupied.Min(point => point.Y), 29, 31);
        Assert.InRange(Math.Abs(occupied.Max(point => point.X) + 1 - (40 + geometry.LocalBounds.Width)), 0, 1.5f);
        Assert.InRange(Math.Abs(occupied.Max(point => point.Y) + 1 - (30 + geometry.LocalBounds.Height)), 0, 1.5f);
        var withoutSpaces = document with { Subtitles = [document.Subtitles[0] with { Text = "ABC" }] };
        var plainGeometry = renderer.GetLayerGeometry(withoutSpaces, MediaTime.Zero, layer.Id)!;
        Assert.InRange(Math.Abs(plainGeometry.LocalBounds.Width - geometry.LocalBounds.Width), 0, 0.01f);
    }

    [Fact]
    public void WhitespaceRemainsEditableWithAnExplicitLogicalFallback()
    {
        var (document, layer) = CreateDocument("   \n ");
        using var renderer = CreateRenderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;

        Assert.False(geometry.HasInk);
        Assert.True(geometry.LocalBounds.Width > 0);
        Assert.True(geometry.LocalBounds.Height > 0);
    }

    [Fact]
    public void PlacementMeasurementMatchesRenderGeometryAndRemainsAvailableOutsideTheClip()
    {
        var (document, layer) = CreateDocument("  ABC\nDEF  ");
        using var renderer = CreateRenderer();
        var automatic = renderer.MeasureSubtitlePlacement(document, document.Subtitles[0]);
        var automaticGeometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        Assert.Equal(automaticGeometry.LocalBounds, automatic.Bounds);
        Assert.Equal(automaticGeometry.HasInk, automatic.HasInk);
        var explicitDocument = document with
        {
            Subtitles = [document.Subtitles[0] with { Style = document.Subtitles[0].Style with { Position = automatic.Position } }]
        };

        var explicitMeasurement = renderer.MeasureSubtitlePlacement(explicitDocument, explicitDocument.Subtitles[0]);

        Assert.Equal(automatic, explicitMeasurement);
        Assert.Null(renderer.GetLayerGeometry(explicitDocument, layer.End, layer.Id));
        Assert.Equal(explicitMeasurement, renderer.MeasureSubtitlePlacement(explicitDocument, explicitDocument.Subtitles[0]));
    }

    private static ProjectSceneRenderer CreateRenderer()
    {
        return new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
    }

    private static (ProjectDocument Document, ProjectLayer Layer) CreateDocument(string text)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var subtitle = new SubtitleLine
        {
            Text = text,
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 256, ReferenceHeight = 160 },
                FontAssetId = font.Id, FontSize = 22, Margins = new(8, 8, 8),
                StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End
        };
        return (new() { Width = 256, Height = 160, Assets = [font], Subtitles = [subtitle], Layers = [layer] }, layer);
    }
}
