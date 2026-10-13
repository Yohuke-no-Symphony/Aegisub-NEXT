using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectiveRenderingTests
{
    [Fact]
    public void CombinedThreeDimensionalRotationSharesForwardInverseAndHdrRendering()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(40, 20), Pivot = new(50, 40), RotationX = 60, RotationY = 30,
            PerspectiveDistance = 100
        });
        document = document with { Layers = [document.Layers[0] with { Fill = new(4, 2, 0), Opacity = 0.5 }] };
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.True(geometry.TryMapLocalPoint(new(70, 80), out var projected));
        AssertPoint(new(40 + (float)(50 * Math.Sqrt(3) / 3), 20 + 50f / 3), projected);
        Assert.True(geometry.TryUnmapWorldPoint(projected, out var original));
        AssertPoint(new(70, 80), original);
        Assert.True(geometry.ContainsWorldPoint(new(40, 20)));
        var pixels = Pixels(renderer, document);
        var center = (20 * document.Width + 40) * 4;
        Assert.InRange(pixels[center], 1.99f, 2.01f);
        Assert.InRange(pixels[center + 1], 0.99f, 1.01f);
        Assert.InRange(pixels[center + 3], 0.499f, 0.501f);
    }

    [Fact]
    public void DistanceWithoutDepthKeepsTheExactExistingAffinePath()
    {
        var document = ShapeDocument(new(X: 34, Y: 25, ScaleX: 0.75, ScaleY: 1.25, Rotation: 27, AnchorX: 20, AnchorY: 30));
        var changed = document with { Layers = [document.Layers[0] with
        {
            Transform = document.Layers[0].Transform with { PerspectiveDistance = SceneProjection.MIN_DISTANCE }
        }] };
        using var renderer = Renderer();
        var original = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        var updated = renderer.GetLayerGeometry(changed, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.Equal(original.LocalToWorld, updated.LocalToWorld);
        Assert.Equal(original.WorldCorners, updated.WorldCorners);
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, changed));
    }

    [Fact]
    public void PlanarNativeCompensationRemainsVisibleAndMatchesAProjectedPositionOffset()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(40, 30), Pivot = new(50, 40),
            ProjectionTranslation = new(7.5, -6.5, 0), PerspectiveDistance = SceneProjection.MIN_DISTANCE
        });
        var affine = document with { Layers = [document.Layers[0] with
        {
            Transform = document.Layers[0].Transform with
            {
                Position = new(47.5, 23.5), ProjectionTranslation = default
            }
        }] };
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.True(geometry.HasInk);
        AssertPoint(new(47.5f, 23.5f), geometry.WorldPivot);
        Assert.True(geometry.TryUnmapWorldPoint(geometry.WorldPivot, out var pivot));
        AssertPoint(new(50, 40), pivot);
        Assert.Equal(Pixels(renderer, affine), Pixels(renderer, document));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void ZeroScaleWithThreeDimensionalRotationHasNoVisibleOrSelectableArea(double scaleX, double scaleY)
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(80, 60), Pivot = new(50, 40), Scale = new(scaleX, scaleY), RotationX = 35, RotationY = 25
        });
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.False(geometry.HasInk);
        Assert.False(geometry.ContainsWorldPoint(new(80, 60)));
        Assert.False(geometry.TryUnmapWorldPoint(new(80, 60), out _));
        Assert.All(Pixels(renderer, document), value => Assert.Equal(0, value));
    }

    [Fact]
    public void ChangingPivotWithNativeDepthCompensationPreservesTheEntireRenderedPlane()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(60, 50), Pivot = new(50, 40), RotationX = 60, RotationY = 30,
            PerspectiveDistance = 100
        });
        var changed = document with { Layers = [document.Layers[0] with
        {
            Transform = document.Layers[0].Transform with
            {
                Pivot = new(60, 28), ProjectionTranslation = new(2 * Math.Sqrt(3), -6, -14)
            }
        }] };
        using var renderer = Renderer();
        var before = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        var after = renderer.GetLayerGeometry(changed, MediaTime.Zero, document.Layers[0].Id)!;

        foreach (var point in new SKPoint[] { new(0, 0), new(35, 20), new(100, 80), new(77, 66) })
        {
            Assert.True(before.TryMapLocalPoint(point, out var first));
            Assert.True(after.TryMapLocalPoint(point, out var second));
            AssertPoint(first, second);
        }
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, changed));
    }

    [Fact]
    public void PlaneCrossingNearClipKeepsFiniteBoundsAndRejectsBehindCameraHits()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(20, 80), RotationX = -60, PerspectiveDistance = 10
        });
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.NotEmpty(geometry.VisiblePolygon);
        Assert.All(geometry.VisiblePolygon, point => Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y)));
        Assert.False(geometry.TryMapLocalPoint(new(10, 20), out _));
        Assert.False(geometry.ContainsWorldPoint(new(30, 75)));
        Assert.True(geometry.ContainsWorldPoint(new(30, 90)));
        var pixels = Pixels(renderer, document);
        Assert.InRange(pixels[(90 * document.Width + 30) * 4 + 3], 0.99f, 1);
        Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
        Assert.Equal(0, pixels[(75 * document.Width + 30) * 4 + 3]);
        Assert.InRange(renderer.RenderSurfaceStatistics.RetainedBytes, 0, 160 * 120 * 8);
    }

    [Fact]
    public void ConstantNegativeDepthIsHiddenEvenWhenSkiaCanNormalizeTheMatrix()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(80, 60), Pivot = new(50, 40), PerspectiveDistance = 100,
            ProjectionTranslation = new(0, 0, -200)
        });
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.False(geometry.HasInk);
        Assert.Empty(geometry.VisiblePolygon);
        Assert.False(geometry.TryMapLocalPoint(new(50, 40), out _));
        Assert.False(geometry.ContainsWorldPoint(new(80, 60)));
        Assert.All(Pixels(renderer, document), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void ExactEdgeOnPlaneIsNotRenderedOrSelectable(double rotation)
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(80, 60), Pivot = new(50, 40), RotationX = rotation
        });
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.False(geometry.HasInk);
        Assert.False(geometry.TryUnmapWorldPoint(new(80, 60), out _));
        Assert.Null(renderer.HitTestLayer(document, MediaTime.Zero, new(80, 60)));
        Assert.All(Pixels(renderer, document), value => Assert.Equal(0, value));
    }

    [Fact]
    public void BackSideRotationIsVisibleWithoutFaceCulling()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(80, 60), Pivot = new(50, 40), RotationX = 180
        });
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;

        Assert.True(geometry.HasInk);
        Assert.True(geometry.TryMapLocalPoint(new(20, 70), out var world));
        AssertPoint(new(50, 30), world);
        Assert.True(geometry.ContainsWorldPoint(world));
        Assert.Equal(1, Pixels(renderer, document)[(30 * document.Width + 50) * 4 + 3]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoNegativeProjectionStagesDoNotBecomeVisibleAfterMatrixComposition(bool outerIsLayer)
    {
        var document = SubtitleDocument("ffi");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 3)
        {
            PerspectiveDistance = 100, ProjectionTranslation = new(0, 0, -200)
        };
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = outerIsLayer ? [range] :
                [range, range with { Id = Guid.NewGuid() }] }],
            Layers = [document.Layers[0] with { Transform = outerIsLayer ? new()
            {
                PerspectiveDistance = 100, ProjectionTranslation = new(0, 0, -200)
            } : new() }]
        };
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        var target = renderer.GetAnimationRangeGeometry(document, MediaTime.Zero, document.Layers[0].Id, range.Id)!;

        Assert.False(geometry.HasInk);
        Assert.Empty(geometry.VisiblePolygon);
        Assert.Empty(target.VisiblePolygon);
        Assert.False(target.TryMapLocalPoint(target.LocalPivot, out _));
        Assert.All(Pixels(renderer, document), value => Assert.Equal(0, value));
    }

    [Fact]
    public void IndividuallyValidSmallDepthStagesRemainVisibleAfterTheirDenominatorsMultiply()
    {
        var document = SubtitleDocument("AB");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            Scale = new(0.001, 0.001), PerspectiveDistance = 100, ProjectionTranslation = new(0, 0, -99.9)
        };
        var projected = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }],
            Layers = [document.Layers[0] with { Transform = new()
            {
                Scale = new(0.001, 0.001), PerspectiveDistance = 100, ProjectionTranslation = new(0, 0, -99.9)
            } }]
        };
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(projected, MediaTime.Zero, document.Layers[0].Id)!;
        var before = Pixels(renderer, document);
        var after = Pixels(renderer, projected);

        Assert.True(geometry.HasInk);
        Assert.NotEmpty(geometry.VisiblePolygon);
        Assert.True(renderer.GetAnimationRangeGeometry(projected, MediaTime.Zero, document.Layers[0].Id, range.Id)!
            .TryMapLocalPoint(geometry.LocalPivot, out _));
        Assert.Contains(after.Where((_, index) => index % 4 == 3), value => value > 0.9);
        Assert.InRange(before.Zip(after, (first, second) => Math.Abs(first - second)).Max(), 0, 0.001f);
    }

    [Fact]
    public void RangeGeometryUsesItsOwnPivotAndPreservesUnownedLigatureShaping()
    {
        var document = SubtitleDocument("ffiX");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 3) { Offset = new(20, 10), Rotation = 25 };
        var target = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1)
        {
            Offset = new(40, 30), RotationX = 40, RotationY = -25, PerspectiveDistance = 80
        };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [first, target] }] };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var visible = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, MediaTime.Zero)));
        var geometry = renderer.GetAnimationRangeGeometry(document, MediaTime.Zero, document.Layers[0].Id, target.Id)!;
        var selected = Assert.Single(plain.GetSelectionRects(1, 1));

        AssertPoint(new(selected.MidX, selected.MidY), geometry.LocalPivot);
        Assert.Equal(geometry.LocalPivot, geometry.BasePosition);
        Assert.Equal(target, geometry.AnimationRange);
        Assert.True(geometry.TryUnmapParentPoint(geometry.WorldPivot, out var ownCenter));
        AssertPoint(new(geometry.BasePosition.X + 40, geometry.BasePosition.Y + 30), ownCenter);
        Assert.Equal(plain.Runs, visible.Runs);
        Assert.Equal(plain.Graphemes[3].Bounds, visible.Graphemes[3].Bounds);
        Assert.NotEqual(plain.Graphemes[1].Bounds, visible.Graphemes[1].Bounds);
        var original = plain.Graphemes[1].Bounds;
        var member = visible.Graphemes[1].LocalToVisible.MapPoint(original.MidX, original.MidY);
        var whole = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        Assert.True(geometry.ContainsWorldPoint(whole.LocalToWorld.MapPoint(member)));
        Assert.NotEmpty(geometry.VisiblePolygon);
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
        Assert.Null(renderer.GetAnimationRangeGeometry(document, document.Layers[0].End, document.Layers[0].Id, target.Id));
    }

    [Fact]
    public void HiddenRangeDoesNotCreatePhantomTextHitsOrSelectionRectangles()
    {
        var document = SubtitleDocument("AB");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            PerspectiveDistance = 100, ProjectionTranslation = new(0, 0, -200)
        };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, MediaTime.Zero)));

        Assert.Empty(layout.GetSelectionRects(0, 1));
        Assert.Equal(1, layout.HitTest(new(0, 0)).GraphemeStart);
        Assert.Single(layout.GetSelectionRects(1, 1));
    }

    [Fact]
    public void FocalDistanceAnimationUpdatesPreviewAndExportWithoutReshapingText()
    {
        var document = SubtitleDocument("ABCD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4) { RotationY = 55, Offset = new(90, 45) };
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }],
            Layers = [document.Layers[0] with { Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id),
                    [new(new(0), 35), new(new(2), 500)])
            ] }]
        };
        using var renderer = Renderer();
        var background = new byte[document.Width * document.Height * 4];
        var before = renderer.ComposePreview(document, MediaTime.Zero, background, document.Width, document.Height, document.Width * 4);
        var after = renderer.ComposePreview(document, new(2), background, document.Width, document.Height, document.Width * 4);
        var pixels = new float[document.Width * document.Height * 4];

        Assert.NotEqual(before, after);
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        var first = pixels.ToArray();
        Assert.True(renderer.CopyCachedFramePixels(document, new(2), pixels));
        Assert.NotEqual(first, pixels);
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
        Assert.Equal(500, renderer.GetAnimationRangeGeometry(document, new(2), document.Layers[0].Id, range.Id)!.AnimationRange!.PerspectiveDistance);
    }

    [Fact]
    public void ImagePlaneUsesTheSamePerspectiveAsItsGeometry()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "plane.png");
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Image = new(asset.Id, 100, 80, ImageFit.STRETCH),
            Transform = new() { Position = new(60, 50), Pivot = new(50, 40), RotationX = 35, RotationY = 20 }
        };
        var document = new ProjectDocument { Width = 160, Height = 120, Assets = [asset], Layers = [layer] };
        using var renderer = new ProjectSceneRenderer(new MemoryImageAssetResolver(ImageTestData.Png(SKColors.Red, SKColors.Red)));
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;

        Assert.True(geometry.ContainsWorldPoint(new(60, 50)));
        var pixels = Pixels(renderer, document);
        Assert.InRange(pixels[(50 * document.Width + 60) * 4], 0.99f, 1);
        Assert.Equal(0, pixels[(5 * document.Width + 5) * 4 + 3]);
    }

    [Fact]
    public void NearClippedPreviewAtReducedResolutionKeepsTheExportFootprint()
    {
        var document = ShapeDocument(new LayerTransform
        {
            Position = new(20, 80), RotationX = -60, PerspectiveDistance = 10
        });
        using var renderer = Renderer();
        var preview = renderer.ComposePreview(document, MediaTime.Zero, new byte[160 * 120 * 4], 160, 120, 160 * 4, 80, 60);
        var exported = Pixels(renderer, document);

        Assert.Equal(255, preview[(45 * 80 + 15) * 4 + 2]);
        Assert.Equal(0, preview[(35 * 80 + 15) * 4 + 2]);
        Assert.Equal(1, exported[(90 * 160 + 30) * 4 + 3]);
        Assert.Equal(0, exported[(70 * 160 + 30) * 4 + 3]);
    }

    private static ProjectDocument ShapeDocument(LayerTransform transform) => new()
    {
        Width = 160, Height = 120, Layers = [new()
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Transform = transform,
            Fill = new(1, 0, 0), End = new(3)
        }]
    };

    private static ProjectDocument SubtitleDocument(string text)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = text, End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 160 }, FontAssetId = font.Id,
                FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                WrapMode = SubtitleWrapMode.NO_WRAP, Fill = SceneColor.White, StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent
            }
        };
        return new() { Width = 384, Height = 160, Assets = [font], Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static float[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new float[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static void AssertPoint(SKPoint expected, SKPoint actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 0.001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 0.001f);
    }
}
