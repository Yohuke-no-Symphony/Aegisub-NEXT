using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ClipMaskRenderingTests
{
    [Fact]
    public void MaskClipsOnlyItsSubtitleAndPreservesSiblingSubtitleAndBackgroundPixels()
    {
        var document = Document();
        var siblingTrack = new ProjectTrack { Name = "Sibling" };
        var sibling = document.Subtitles[0] with
        {
            Id = Guid.NewGuid(), Text = "OTHER",
            Style = document.Subtitles[0].Style with { FontSize = 24, Fill = new(0.1, 2, 0.8) }
        };
        var siblingLayer = document.Layers[0] with
        {
            Id = sibling.Id, TrackId = siblingTrack.Id, SubtitleId = sibling.Id, Transform = new(X: 12, Y: 45)
        };
        var backgroundTrack = new ProjectTrack { Name = "Background" };
        var background = new ProjectLayer
        {
            TrackId = backgroundTrack.Id,
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, document.Width, document.Height),
            Fill = new(0.1, 0.2, 0.3)
        };
        document = document with
        {
            Tracks = document.Tracks.Add(siblingTrack).Add(backgroundTrack),
            Subtitles = document.Subtitles.Add(sibling),
            Layers = [background, siblingLayer, document.Layers[0] with { Opacity = 0.7 }]
        };
        var withoutTarget = document with { Subtitles = [sibling], Layers = [background, siblingLayer] };
        var backgroundOnly = document with { Subtitles = [], Layers = [background] };
        var clipped = document with
        {
            Layers = document.Layers.SetItem(2, document.Layers[2] with
            {
                Mask = new RectangleClipMask { TopLeft = new(32, 0), BottomRight = new(80, 96) }
            })
        };
        using var renderer = Renderer();
        var originalPixels = Pixels(renderer, document);
        var siblingPixels = Pixels(renderer, withoutTarget);
        var backgroundPixels = Pixels(renderer, backgroundOnly);
        var clippedPixels = Pixels(renderer, clipped);
        var siblingInkOutsideMask = 0;
        var targetInkRemoved = 0;
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var offset = (y * document.Width + x) * 4;
                var keepTarget = x >= 32 && x < 80;
                for (var channel = 0; channel < 4; channel++)
                {
                    Assert.Equal(keepTarget ? originalPixels[offset + channel] : siblingPixels[offset + channel],
                        clippedPixels[offset + channel]);
                }

                if (!keepTarget)
                {
                    siblingInkOutsideMask += siblingPixels.AsSpan(offset, 4).SequenceEqual(backgroundPixels.AsSpan(offset, 4)) ? 0 : 1;
                    targetInkRemoved += originalPixels.AsSpan(offset, 4).SequenceEqual(siblingPixels.AsSpan(offset, 4)) ? 0 : 1;
                }
            }
        }

        Assert.True(siblingInkOutsideMask > 0);
        Assert.True(targetInkRemoved > 0);
    }

    [Fact]
    public void PartialAndCompletedKaraokeFillStrokeShadowAndBlurStayInsideTheClipMask()
    {
        var document = Document();
        var line = document.Subtitles[0];
        document = document with
        {
            Subtitles = [line with
            {
                Karaoke = [new(0, line.Text.Length, new(0), new(1), SceneColor.White)],
                KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "HDR mask highlight", line.Style with
                {
                    Fill = new(0.25, 4, 1), Stroke = new(0.25, 2, 0.5), StrokeWidth = 3,
                    ShadowColor = new(0.1, 0.25, 3), ShadowOffset = new(-3, 5), ShadowBlur = 3
                })
            }]
        };
        var masked = WithMask(document, new RectangleClipMask { TopLeft = new(32, 0), BottomRight = new(80, 96) });
        using var renderer = Renderer();
        var initial = Pixels(renderer, document, new(0));
        var partial = Pixels(renderer, document, new(1, 2));
        var completed = Pixels(renderer, document, new(1));
        Assert.False(initial.SequenceEqual(partial));
        Assert.False(partial.SequenceEqual(completed));
        foreach (var time in new MediaTime[] { new(0), new(1, 2), new(1) })
        {
            AssertCrop(Pixels(renderer, document, time), Pixels(renderer, masked, time), document.Width, document.Height,
                (x, _) => x >= 32 && x < 80);
        }

        var clippedHighlight = Pixels(renderer, masked, new(1));
        Assert.Contains(clippedHighlight.Where((_, index) => index % 4 == 1), value => (float)value > 1);
    }

    [Fact]
    public void SubtitlePositionScaleAndRotationAnimationCannotMoveTheEngineeringMaskOrVideoBackground()
    {
        var document = Document();
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks =
                [
                    new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(12, 8))]),
                    new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(1, 1)), new(new(1), new ScenePoint(0.75, 1.1))]),
                    new(AnimationProperty.ROTATION, [new(new(0), 0), new(new(1), 12)])
                ]
            }]
        };
        var masked = WithMask(document, new RectangleClipMask { TopLeft = new(32, 0), BottomRight = new(80, 96) });
        var backgroundOnly = document with { Subtitles = [], Layers = [] };
        var background = Enumerable.Range(0, 12)
            .SelectMany(index => new byte[] { (byte)(16 + index * 3), (byte)(32 + index * 2), (byte)(48 + index), 255 }).ToArray();
        using var renderer = Renderer();
        var backgroundPixels = renderer.ComposePreview(backgroundOnly, new(0), background, 4, 3, 16, 64, 48);
        Assert.False(Pixels(renderer, document, new(0)).SequenceEqual(Pixels(renderer, document, new(1))));
        foreach (var time in new MediaTime[] { new(0), new(1, 2), new(1) })
        {
            AssertCrop(Pixels(renderer, document, time), Pixels(renderer, masked, time), document.Width, document.Height,
                (x, _) => x >= 32 && x < 80);
            var original = renderer.ComposePreview(document, time, background, 4, 3, 16, 64, 48);
            var clipped = renderer.ComposePreview(masked, time, background, 4, 3, 16, 64, 48);
            var retainedInk = 0;
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var offset = (y * 64 + x) * 4;
                    var keep = x >= 16 && x < 40;
                    Assert.Equal((keep ? original : backgroundPixels).AsSpan(offset, 4).ToArray(), clipped.AsSpan(offset, 4).ToArray());
                    retainedInk += keep && !original.AsSpan(offset, 4).SequenceEqual(backgroundPixels.AsSpan(offset, 4)) ? 1 : 0;
                }
            }

            Assert.True(retainedInk > 0);
        }
    }

    [Fact]
    public void ReversedBezierContourMakesAHoleAndItsInversePreservesTheCurvedInterior()
    {
        var document = Document();
        var handle = 24 * 0.5522847498307936;
        var curvedHole = new MaskContour
        {
            Nodes =
            [
                new() { Position = new(80, 26), InHandle = new(0, handle), OutHandle = new(0, -handle) },
                new() { Position = new(56, 2), InHandle = new(handle, 0), OutHandle = new(-handle, 0) },
                new() { Position = new(32, 26), InHandle = new(0, -handle), OutHandle = new(0, handle) },
                new() { Position = new(56, 50), InHandle = new(-handle, 0), OutHandle = new(handle, 0) }
            ]
        };
        var mask = new VectorClipMask
        {
            Contours = [Contour(new(-40, -30), new(168, -30), new(168, 126), new(-40, 126)), curvedHole]
        };
        using var renderer = Renderer();
        var original = Pixels(renderer, document);
        var clipped = Pixels(renderer, WithMask(document, mask));
        var inverse = Pixels(renderer, WithMask(document, mask with { Inverted = true }));
        var interiorInk = 0;
        var exteriorInk = 0;
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var distanceSquared = Math.Pow(x + 0.5 - 56, 2) + Math.Pow(y + 0.5 - 26, 2);
                if (distanceSquared > 22 * 22 && distanceSquared < 26 * 26)
                {
                    continue;
                }

                var insideHole = distanceSquared <= 22 * 22;
                var offset = (y * document.Width + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    Assert.Equal(insideHole ? (Half)0 : original[offset + channel], clipped[offset + channel]);
                    Assert.Equal(insideHole ? original[offset + channel] : (Half)0, inverse[offset + channel]);
                }

                if (original[offset + 3] > (Half)0)
                {
                    interiorInk += insideHole ? 1 : 0;
                    exteriorInk += insideHole ? 0 : 1;
                }
            }
        }

        Assert.True(interiorInk > 0);
        Assert.True(exteriorInk > 0);
    }

    [Fact]
    public void OffscreenAndZeroAreaMasksRespectEngineeringBoundsAndInverseClipping()
    {
        var document = Document();
        using var renderer = Renderer();
        var original = Pixels(renderer, document);
        foreach (var mask in new RectangleClipMask[]
        {
            new() { TopLeft = new(200, 200), BottomRight = new(240, 260) },
            new() { TopLeft = new(64, 0), BottomRight = new(64, 96) }
        })
        {
            Assert.All(Pixels(renderer, WithMask(document, mask)), value => Assert.Equal((Half)0, value));
            Assert.Equal(original, Pixels(renderer, WithMask(document, mask with { Inverted = true })));
        }

        var partial = new RectangleClipMask { TopLeft = new(-20, -20), BottomRight = new(64, 120) };
        AssertCrop(original, Pixels(renderer, WithMask(document, partial)), document.Width, document.Height, (x, _) => x < 64);
        AssertCrop(original, Pixels(renderer, WithMask(document, partial with { Inverted = true })), document.Width, document.Height, (x, _) => x >= 64);
    }

    [Fact]
    public void AnimatedMaskRefreshesSharedPreviewAndMatchesStaticMaskRendering()
    {
        var document = WithMask(Document(), new RectangleClipMask { BottomRight = new(48, 96) });
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks = [new(AnimationProperty.MASK_POSITION,
                    [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(64, 0))])]
            }]
        };
        using var renderer = Renderer();
        var background = Enumerable.Range(0, 12).SelectMany(_ => new byte[] { 16, 32, 48, 255 }).ToArray();
        var first = renderer.ComposePreview(document, new(0), background, 4, 3, 16, 64, 48);
        var second = renderer.ComposePreview(document, new(1), background, 4, 3, 16, 64, 48);
        Assert.False(first.SequenceEqual(second));
        var staticMask = WithMask(document with { Layers = [document.Layers[0] with { Tracks = [] }] },
            new RectangleClipMask { BottomRight = new(48, 96), Transform = new() { Position = new(32, 0) } });
        var expected = renderer.ComposePreview(staticMask, new(1), background, 4, 3, 16, 64, 48);
        Assert.Equal(expected, second);
        Assert.Equal(Pixels(renderer, staticMask), Pixels(renderer, document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleClipsTheCompleteBlurredHdrSubtitleContribution(bool inverted)
    {
        var document = Document();
        using var renderer = Renderer();
        var original = Pixels(renderer, document);
        var mask = new RectangleClipMask
        {
            TopLeft = new(32, 0), BottomRight = new(80, 96), Inverted = inverted
        };
        var clipped = Pixels(renderer, WithMask(document, mask));

        AssertCrop(original, clipped, document.Width, document.Height,
            (x, _) => inverted ? x < 32 || x >= 80 : x >= 32 && x < 80);
        Assert.Contains(clipped, value => (float)value > 1);
    }

    [Fact]
    public void OppositeContourDirectionsCreateAHoleUsingNonzeroWinding()
    {
        var document = Document();
        using var renderer = Renderer();
        var original = Pixels(renderer, document);
        var mask = new VectorClipMask
        {
            Contours =
            [
                Contour(new(0, 0), new(128, 0), new(128, 96), new(0, 96)),
                Contour(new(32, 8), new(32, 80), new(80, 80), new(80, 8))
            ]
        };
        var clipped = Pixels(renderer, WithMask(document, mask));

        AssertCrop(original, clipped, document.Width, document.Height,
            (x, y) => x < 32 || x >= 80 || y < 8 || y >= 80);
    }

    [Fact]
    public void MaskUsesItsOwnTransformAndIgnoresTheSubtitleParentTransform()
    {
        var document = Document();
        document = document with
        {
            Layers = [document.Layers[0] with { Transform = new(X: 16, Y: 4, Rotation: 5) }]
        };
        using var renderer = Renderer();
        var original = Pixels(renderer, document);
        var mask = new RectangleClipMask
        {
            TopLeft = new(24, 0), BottomRight = new(72, 96),
            Transform = new() { Position = new(16, 0) }
        };
        var clip = document.Layers[0];
        var masked = document with
        {
            Layers = [clip with { Mask = mask }]
        };

        AssertCrop(original, Pixels(renderer, masked), document.Width, document.Height,
            (x, _) => x >= 40 && x < 88);
    }

    [Fact]
    public void ReducedPreviewProjectsTheMaskAndRefreshesAtTheSameTime()
    {
        var document = Document();
        using var renderer = Renderer();
        var background = Enumerable.Range(0, 12).SelectMany(_ => new byte[] { 16, 32, 48, 255 }).ToArray();
        var left = new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(64, 96) };
        var right = left with { TopLeft = new(64, 0), BottomRight = new(128, 96) };
        var backgroundOnly = renderer.ComposePreview(document with { Subtitles = [], Layers = [] }, new(1), background, 4, 3, 16, 64, 48);
        var original = renderer.ComposePreview(document, new(1), background, 4, 3, 16, 64, 48);
        var first = renderer.ComposePreview(WithMask(document, left), new(1), background, 4, 3, 16, 64, 48);
        var second = renderer.ComposePreview(WithMask(document, right), new(1), background, 4, 3, 16, 64, 48);
        var retainedInk = 0;
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var offset = (y * 64 + x) * 4;
                var originalPixel = original.AsSpan(offset, 4).ToArray();
                var backgroundPixel = backgroundOnly.AsSpan(offset, 4).ToArray();
                Assert.Equal(x < 32 ? originalPixel : backgroundPixel, first.AsSpan(offset, 4).ToArray());
                Assert.Equal(x >= 32 ? originalPixel : backgroundPixel, second.AsSpan(offset, 4).ToArray());
                if (!originalPixel.SequenceEqual(backgroundPixel))
                {
                    retainedInk++;
                }
            }
        }

        Assert.True(retainedInk > 0);
    }

    [Fact]
    public void NativePlacementResolvesEngineeringCanvasBeforePreviewScaling()
    {
        var mask = new RectangleClipMask
        {
            TopLeft = new(10, 20), BottomRight = new(50, 80),
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(40, 60) },
            Placement = new() { Anchor = new(1, 0), Pivot = new(0.5, 0.5), Offset = new(-40, 50) }
        };
        using var path = ClipMaskGeometry.Create(mask, new(128, 96), 0.5f, 0.5f);

        Assert.Equal(34, path.Bounds.Left);
        Assert.Equal(10, path.Bounds.Top);
        Assert.Equal(54, path.Bounds.Right);
        Assert.Equal(40, path.Bounds.Bottom);
        Assert.True(path.Contains(40, 20));
        Assert.False(path.Contains(20, 20));
        Assert.False(path.Contains(50, 45));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementAndFixedReferenceFrameChangesRefreshCachedPreview(bool changeReferenceFrame)
    {
        var document = Document();
        var mask = new RectangleClipMask
        {
            BottomRight = new(64, 96), ReferenceFrame = new() { Size = new(64, 96) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(-32, 0) }
        };
        var moved = changeReferenceFrame
            ? mask with { ReferenceFrame = mask.ReferenceFrame with { Origin = new(-64, 0) } }
            : mask with { Placement = mask.Placement with { Offset = new(32, 0) } };
        using var renderer = Renderer();
        var background = Enumerable.Range(0, 12).SelectMany(_ => new byte[] { 16, 32, 48, 255 }).ToArray();
        var withoutTarget = renderer.ComposePreview(document with { Subtitles = [], Layers = [] }, new(1), background, 4, 3, 16, 64, 48);
        var original = renderer.ComposePreview(document, new(1), background, 4, 3, 16, 64, 48);
        var first = renderer.ComposePreview(WithMask(document, mask), new(1), background, 4, 3, 16, 64, 48);
        var second = renderer.ComposePreview(WithMask(document, moved), new(1), background, 4, 3, 16, 64, 48);

        Assert.False(first.SequenceEqual(second));
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var offset = (y * 64 + x) * 4;
                Assert.Equal((x < 32 ? original : withoutTarget).AsSpan(offset, 4).ToArray(), first.AsSpan(offset, 4).ToArray());
                Assert.Equal((x >= 32 ? original : withoutTarget).AsSpan(offset, 4).ToArray(), second.AsSpan(offset, 4).ToArray());
            }
        }
    }

    private static ProjectDocument Document()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var subtitle = new SubtitleLine
        {
            Text = "MMMMMMMM",
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 128, ReferenceHeight = 96 },
                FontAssetId = font.Id, FontSize = 32, Alignment = TextAlignment.TOP_LEFT,
                Margins = new(8, 8, 8), Fill = new(4, 0.5, 0.25), Stroke = new(2, 0, 0),
                StrokeWidth = 2, ShadowColor = new(1, 0.25, 0), ShadowOffset = new(4, 4), ShadowBlur = 2
            }
        };
        return new()
        {
            Width = 128, Height = 96, Assets = [font], Subtitles = [subtitle],
            Layers =
            [
                new ProjectLayer
                {
                    Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id,
                    Start = subtitle.Start, End = subtitle.End, Blur = 1
                }
            ]
        };
    }

    private static MaskContour Contour(params ScenePoint[] positions) => new()
    {
        Nodes = positions.Select(position => new MaskNode { Position = position }).ToImmutableArray()
    };

    private static ProjectDocument WithMask(ProjectDocument document, ClipMask mask) => document with
    {
        Layers = [document.Layers[0] with { Mask = mask }]
    };

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime? time = null)
    {
        using var surface = renderer.Render(document, time ?? new(1));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static void AssertCrop(Half[] original, Half[] clipped, int width, int height, Func<int, int, bool> keep)
    {
        var retainedInk = 0;
        var removedInk = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var retained = keep(x, y);
                for (var channel = 0; channel < 4; channel++)
                {
                    Assert.Equal(retained ? original[offset + channel] : (Half)0, clipped[offset + channel]);
                }

                if (original[offset + 3] > (Half)0)
                {
                    if (retained)
                    {
                        retainedInk++;
                    }
                    else
                    {
                        removedInk++;
                    }
                }
            }
        }

        Assert.True(retainedInk > 0);
        Assert.True(removedInk > 0);
    }
}
