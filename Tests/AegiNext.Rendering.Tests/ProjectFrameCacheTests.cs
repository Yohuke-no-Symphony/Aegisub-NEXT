using System.Numerics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectFrameCacheTests
{
    [Fact]
    public void RetainedRevisionSkipsCopiesButANewDestinationReceivesCompletePixels()
    {
        var document = ShapeDocument();
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        var first = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, 0);
        Assert.True(first.Updated);
        Assert.False(first.Empty);
        Assert.NotEqual(0UL, first.Revision);
        Assert.Equal(UncachedPixels(document, MediaTime.Zero), pixels);

        pixels.AsSpan().Fill(float.NaN);
        var hit = renderer.UpdateCachedFramePixels(document, new(1), pixels, first.Revision);
        Assert.False(hit.Updated);
        Assert.Equal(first.Revision, hit.Revision);
        Assert.All(pixels, value => Assert.True(float.IsNaN(value)));

        var copied = renderer.UpdateCachedFramePixels(document, new(1), pixels, 0);
        Assert.True(copied.Updated);
        Assert.Equal(first.Revision, copied.Revision);
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        Assert.Equal(1UL, renderer.FrameCacheStatistics.Redraws);
        Assert.Equal(2UL, renderer.FrameCacheStatistics.Copies);
    }

    [Fact]
    public void EmptyTransitionsPublishRevisionsWithoutWritingStaleBuffers()
    {
        var document = ShapeDocument();
        document = document with { Layers = [document.Layers[0] with { Start = new(1), End = new(2) }] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        pixels.AsSpan().Fill(42);
        var empty = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, 0);
        Assert.True(empty.Updated);
        Assert.True(empty.Empty);
        Assert.All(pixels, value => Assert.Equal(42, value));
        var appeared = renderer.UpdateCachedFramePixels(document, new(1), pixels, empty.Revision);
        Assert.True(appeared.Updated);
        Assert.False(appeared.Empty);
        Assert.True(appeared.Revision > empty.Revision);
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        var disappeared = renderer.UpdateCachedFramePixels(document, new(2), pixels, appeared.Revision);
        Assert.True(disappeared.Updated);
        Assert.True(disappeared.Empty);
        Assert.True(disappeared.Revision > appeared.Revision);
        var hit = renderer.UpdateCachedFramePixels(document, new(3), pixels, disappeared.Revision);
        Assert.False(hit.Updated);
        Assert.True(hit.Empty);
        Assert.Equal(1UL, renderer.FrameCacheStatistics.Copies);
    }

    [Fact]
    public void SnapshotReplacementAndFailedDrawCannotReuseAPublishedRevision()
    {
        var document = ResourceTransitionDocument();
        var resolver = new FailOnceProjectAssetResolver(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var renderer = new ProjectSceneRenderer(resolver);
        var pixels = new float[document.Width * document.Height * 4];
        var first = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, 0);
        resolver.FailNextOpen = true;
        Assert.Throws<IOException>(() => renderer.UpdateCachedFramePixels(document, new(1), pixels, first.Revision));
        var recovered = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, first.Revision);
        Assert.True(recovered.Updated);
        Assert.True(recovered.Revision > first.Revision);
        Assert.Equal(UncachedPixels(document, MediaTime.Zero), pixels);
        var snapshot = document with { ReferenceWhiteNits = 100 };
        var replaced = renderer.UpdateCachedFramePixels(snapshot, MediaTime.Zero, pixels, recovered.Revision);
        Assert.True(replaced.Updated);
        Assert.True(replaced.Revision > recovered.Revision);
    }

    [Theory]
    [InlineData(AnimationProperty.POSITION)]
    [InlineData(AnimationProperty.OPACITY)]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.BLUR)]
    public void ChangedRasterPublishesNewRevisionsAndRepeatedTimesReuseThem(AnimationProperty property)
    {
        var document = ShapeDocument();
        var first = property switch
        {
            AnimationProperty.POSITION => AnimationValue.FromVector(new(0, 0)),
            AnimationProperty.FILL => AnimationValue.FromColor(new(4, 0, 0, 0.5)),
            AnimationProperty.OPACITY => AnimationValue.FromScalar(1),
            _ => AnimationValue.FromScalar(0)
        };
        var last = property switch
        {
            AnimationProperty.POSITION => AnimationValue.FromVector(new(12, 0)),
            AnimationProperty.FILL => AnimationValue.FromColor(new(0, 0, 4, 0.75)),
            AnimationProperty.OPACITY => AnimationValue.FromScalar(0.25),
            _ => AnimationValue.FromScalar(2)
        };
        document = document with
        {
            Layers = [document.Layers[0] with { Tracks = [new(property, [new(MediaTime.Zero, first), new(new(2), last)])] }]
        };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        var initial = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, 0);
        var animated = renderer.UpdateCachedFramePixels(document, new(1), pixels, initial.Revision);
        Assert.True(animated.Updated);
        Assert.True(animated.Revision > initial.Revision);
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        var repeated = renderer.UpdateCachedFramePixels(document, new(1), pixels, animated.Revision);
        Assert.False(repeated.Updated);
        Assert.Equal(animated.Revision, repeated.Revision);
    }

    [Fact]
    public void StaticFrameDrawsOnceButCopiesEveryOutputBuffer()
    {
        var document = ShapeDocument();
        using var renderer = Renderer();
        var expected = UncachedPixels(document, MediaTime.Zero);
        var pixels = new float[expected.Length];
        var redraws = 0;

        for (var frame = 0; frame < 120; frame++)
        {
            pixels.AsSpan().Fill(float.NaN);
            if (renderer.CopyCachedFramePixels(document, new(frame, 30), pixels))
            {
                redraws++;
            }

            Assert.Equal(expected, pixels);
        }

        Assert.Equal(1, redraws);
        Assert.Equal(2, pixels[(8 * document.Width + 8) * 4]);
        Assert.Equal(0.5f, pixels[(8 * document.Width + 8) * 4 + 3]);
    }

    [Fact]
    public void HalfOpenVisibilityAndBackwardSeekClearExpiredPixels()
    {
        var document = ShapeDocument();
        document = document with { Layers = [document.Layers[0] with { Start = new(1), End = new(2) }] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        var times = new MediaTime[] { new(0), new(1), new(3, 2), new(2), new(3), new(3, 2) };
        var redraws = new[] { true, true, false, true, false, true };

        for (var index = 0; index < times.Length; index++)
        {
            Assert.Equal(redraws[index], renderer.CopyCachedFramePixels(document, times[index], pixels));
            Assert.Equal(UncachedPixels(document, times[index]), pixels);
        }
    }

    [Theory]
    [InlineData(AnimationProperty.POSITION)]
    [InlineData(AnimationProperty.OPACITY)]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.STROKE_WIDTH)]
    [InlineData(AnimationProperty.BLUR)]
    [InlineData(AnimationProperty.MASK_POSITION)]
    [InlineData(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)]
    public void AnimatedClipResultsInvalidateTheFrame(AnimationProperty property)
    {
        var isMask = property is AnimationProperty.MASK_POSITION or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT;
        var document = isMask ? SubtitleDocument() : ShapeDocument();
        var first = property switch
        {
            AnimationProperty.POSITION or AnimationProperty.MASK_POSITION => AnimationValue.FromVector(new(0, 0)),
            AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT => AnimationValue.FromVector(new(document.Width, document.Height)),
            AnimationProperty.FILL or AnimationProperty.STROKE => AnimationValue.FromColor(new(4, 0, 0, 0.5)),
            AnimationProperty.OPACITY => AnimationValue.FromScalar(1),
            _ => AnimationValue.FromScalar(0)
        };
        var last = property switch
        {
            AnimationProperty.POSITION => AnimationValue.FromVector(new(12, 0)),
            AnimationProperty.MASK_POSITION => AnimationValue.FromVector(new(document.Width, 0)),
            AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT => AnimationValue.FromVector(new(4, 4)),
            AnimationProperty.FILL or AnimationProperty.STROKE => AnimationValue.FromColor(new(0, 0, 4, 0.75)),
            AnimationProperty.OPACITY => AnimationValue.FromScalar(0.25),
            _ => AnimationValue.FromScalar(2)
        };
        var child = document.Layers[0] with
        {
            Mask = isMask ? new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(document.Width, document.Height) } : null,
            Tracks = [new(property, [new(MediaTime.Zero, first), new(new(2), last)])]
        };
        document = document with { Layers = [child] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        var initial = pixels.ToArray();

        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        Assert.False(initial.AsSpan().SequenceEqual(pixels));
        Assert.False(renderer.CopyCachedFramePixels(document, new(1), pixels));
    }

    [Fact]
    public void MotionPathWithoutKeyframesRefreshesUntilThePathStops()
    {
        var document = ShapeDocument();
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                MotionPath = new(new(new(0, 0), [new(new(0, 4), new(8, 4), new(12, 0))]), new(2), true)
            }]
        };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];

        foreach (var time in new MediaTime[] { new(0), new(1), new(2) })
        {
            Assert.True(renderer.CopyCachedFramePixels(document, time, pixels));
            Assert.Equal(UncachedPixels(document, time), pixels);
        }

        Assert.False(renderer.CopyCachedFramePixels(document, new(5, 2), pixels));
        Assert.Equal(UncachedPixels(document, new(5, 2)), pixels);
    }

    [Theory]
    [InlineData(AnimationProperty.MASK_NODE_POSITION)]
    [InlineData(AnimationProperty.MASK_NODE_IN_HANDLE)]
    [InlineData(AnimationProperty.MASK_NODE_OUT_HANDLE)]
    public void AnimatedVectorMaskGeometryRefreshesPixels(AnimationProperty property)
    {
        var document = SubtitleDocument();
        var node = new MaskNode { Position = new(0, 0) };
        var lastHandle = property == AnimationProperty.MASK_NODE_OUT_HANDLE ? new ScenePoint(64, 256) : new ScenePoint(64, 32);
        var mask = new VectorClipMask
        {
            Contours = [new()
            {
                Nodes = [node, new() { Position = new(128, 0) }, new() { Position = new(128, 64) }, new() { Position = new(0, 64) }]
            }]
        };
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Mask = mask,
                Tracks = [new(new AnimationTrackTarget(property, node.Id),
                    [new(MediaTime.Zero, new ScenePoint(0, 0)), new(new(2), lastHandle)])]
            }]
        };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        var initial = pixels.ToArray();

        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        Assert.False(initial.AsSpan().SequenceEqual(pixels));
        Assert.False(renderer.CopyCachedFramePixels(document, new(1), pixels));
    }

    [Theory]
    [InlineData(AnimationProperty.FILL)]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.STROKE_WIDTH)]
    public void RichTextAnimationOverridesAreIncludedInCachedPixels(AnimationProperty property)
    {
        var document = SubtitleDocument();
        var subtitle = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { StrokeWidth = 2 },
            InlineSpans = [new(0, 2, new() { Fill = new(1, 0, 0), Stroke = new(0, 1, 0), StrokeWidth = 0 })]
        };
        var first = property == AnimationProperty.STROKE_WIDTH ? AnimationValue.FromScalar(0) : AnimationValue.FromColor(SceneColor.White);
        var last = property == AnimationProperty.STROKE_WIDTH ? AnimationValue.FromScalar(4) : AnimationValue.FromColor(new(0, 0, 4));
        document = document with
        {
            Subtitles = [subtitle],
            Layers = [document.Layers[0] with { Tracks = [new(property, [new(MediaTime.Zero, first), new(new(2), last)])] }]
        };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];

        foreach (var time in new MediaTime[] { new(0), new(1), new(2) })
        {
            Assert.True(renderer.CopyCachedFramePixels(document, time, pixels));
            Assert.Equal(UncachedPixels(document, time), pixels);
            Assert.False(renderer.CopyCachedFramePixels(document, time, pixels));
        }
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    public void KaraokeTimeRefreshesHighlightPixels(KaraokeHighlightKind kind)
    {
        var document = SubtitleDocument();
        var subtitle = document.Subtitles[0] with
        {
            Karaoke = [new(0, 4, new(1, 2), new(2), new(1, 0, 0)) { HighlightKind = kind }]
        };
        document = document with { Subtitles = [subtitle] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        float[]? initial = null;

        foreach (var time in new MediaTime[] { new(0), new(1), new(2) })
        {
            Assert.True(renderer.CopyCachedFramePixels(document, time, pixels));
            Assert.Equal(UncachedPixels(document, time), pixels);
            Assert.False(renderer.CopyCachedFramePixels(document, time, pixels));
            initial ??= pixels.ToArray();
        }

        Assert.False(initial.AsSpan().SequenceEqual(pixels));
    }

    [Fact]
    public void NewSubtitleSnapshotInvalidatesEvenWhenLayerIdentityIsReused()
    {
        var document = SubtitleDocument();
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        var initial = pixels.ToArray();
        var updated = document with
        {
            Subtitles = [document.Subtitles[0] with { Text = "W", Style = document.Subtitles[0].Style with { Fill = new(0, 1, 0) } }]
        };

        Assert.True(renderer.CopyCachedFramePixels(updated, MediaTime.Zero, pixels));
        Assert.Equal(UncachedPixels(updated, MediaTime.Zero), pixels);
        Assert.False(initial.AsSpan().SequenceEqual(pixels));
        Assert.False(renderer.CopyCachedFramePixels(updated, new(1), pixels));
    }

    [Fact]
    public void SnapshotSizeAndReferenceWhiteReplaceTheOwnedSurface()
    {
        var document = ShapeDocument();
        using var renderer = Renderer();

        foreach (var snapshot in new[]
        {
            document,
            document with { Width = 48, Height = 40 },
            document with { ReferenceWhiteNits = 100 },
            document
        })
        {
            var pixels = new float[snapshot.Width * snapshot.Height * 4];
            Assert.True(renderer.CopyCachedFramePixels(snapshot, MediaTime.Zero, pixels));
            Assert.Equal(UncachedPixels(snapshot, MediaTime.Zero), pixels);
            Assert.False(renderer.CopyCachedFramePixels(snapshot, new(1), pixels));
        }
    }

    [Fact]
    public void ScaledPreviewAndExternalSurfaceCannotModifyTheCachedFrame()
    {
        var document = ShapeDocument();
        using var renderer = Renderer();
        var expected = UncachedPixels(document, MediaTime.Zero);
        var pixels = new float[expected.Length];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        renderer.ComposePreview(document, MediaTime.Zero, new byte[] { 0, 0, 0, 255 }, 1, 1, 4, 16, 16);
        renderer.ComposePreview(document, MediaTime.Zero, new byte[] { 0, 0, 0, 255 }, 1, 1, 4, 8, 8);
        using var external = renderer.Render(document, MediaTime.Zero);
        external.FillRectangle(Vector2.Zero, new(document.Width, document.Height), new(0, 4, 0, 1));

        renderer.RenderInto(document, MediaTime.Zero, external);
        external.CopyPixels(pixels);
        Assert.Equal(expected, pixels);
        pixels.AsSpan().Fill(float.NaN);
        Assert.False(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(expected, pixels);
    }

    [Fact]
    public void InvalidBufferAndCancelledHitLeaveSuccessfulCacheAvailable()
    {
        var document = ShapeDocument();
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<ArgumentException>(() => renderer.CopyCachedFramePixels(document, new(1), new float[3]));
        Assert.Throws<OperationCanceledException>(() => renderer.CopyCachedFramePixels(document, new(1), pixels, cancellation.Token));
        Assert.False(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        renderer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
    }

    [Fact]
    public void ResourceCancellationCannotPublishPartialFrameOrPoisonEarlierTime()
    {
        var document = ResourceTransitionDocument();
        using var cancellation = new CancellationTokenSource();
        var resolver = new CancellingProjectAssetResolver(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), cancellation);
        using var renderer = new ProjectSceneRenderer(resolver);
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));

        Assert.Throws<OperationCanceledException>(() => renderer.CopyCachedFramePixels(document, new(1), pixels, cancellation.Token));
        Assert.Equal(1, resolver.OpenCount);
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        Assert.Equal(UncachedPixels(document, MediaTime.Zero), pixels);
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
    }

    [Fact]
    public void FailedResourceDrawMustBeRetriedBeforePublishingACacheHit()
    {
        var document = ResourceTransitionDocument();
        var resolver = new FailOnceProjectAssetResolver(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var renderer = new ProjectSceneRenderer(resolver);
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, MediaTime.Zero, pixels));
        resolver.FailNextOpen = true;

        Assert.Throws<IOException>(() => renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(UncachedPixels(document, new(1)), pixels);
        Assert.False(renderer.CopyCachedFramePixels(document, new(3, 2), pixels));
    }

    private static ProjectDocument ShapeDocument()
    {
        return new()
        {
            Width = 32, Height = 32,
            Layers = [new()
            {
                Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 16, 16),
                Transform = new(X: 4, Y: 4), Fill = new(4, 2, 0, 0.5), StrokeWidth = 1
            }]
        };
    }

    private static ProjectDocument SubtitleDocument()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var subtitle = new SubtitleLine
        {
            Text = "MMMM", End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 128, ReferenceHeight = 64 },
                FontAssetId = font.Id, FontSize = 22, Margins = new(2, 2, 2), Alignment = TextAlignment.TOP_LEFT,
                StrokeWidth = 0, ShadowBlur = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = 128, Height = 64, Assets = [font], Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };
    }

    private static ProjectDocument ResourceTransitionDocument()
    {
        var document = SubtitleDocument();
        var subtitle = document.Subtitles[0] with { Start = new(1), End = new(2) };
        var frontTrack = new ProjectTrack();
        return document with
        {
            Subtitles = [subtitle],
            Tracks = [frontTrack, ProjectTrack.Default],
            Layers =
            [
                new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 128, 64), Fill = new(1, 0, 0) },
                document.Layers[0] with { TrackId = frontTrack.Id, Start = subtitle.Start, End = subtitle.End }
            ]
        };
    }

    private static float[] UncachedPixels(ProjectDocument document, MediaTime time)
    {
        using var renderer = Renderer();
        using var surface = renderer.Render(document, time);
        var pixels = new float[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
}
