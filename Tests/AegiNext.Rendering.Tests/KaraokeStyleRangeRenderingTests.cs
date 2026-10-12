using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class KaraokeStyleRangeRenderingTests
{
    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void IndependentCharacterVisualsActivateTogetherWithoutSplittingTheTimingGroup(KaraokeHighlightKind kind)
    {
        var document = Document("Wi");
        var original = document.Subtitles[0];
        var segment = new KaraokeSegment(0, 2, new(1), new(2), SceneColor.White) { HighlightKind = kind };
        var line = original with
        {
            Karaoke = [segment],
            KaraokeStyleSpans = [new(0, 1, new() { Fill = new(1, 0, 0) }, new() { Fill = new(0, 0, 1) }),
                new(1, 1, new() { Fill = new(0, 1, 0) }, new() { Fill = new(0, 0, 1) })]
        };
        using var renderer = Renderer();
        Assert.Equal(Preview(renderer, document, original), Preview(renderer, document, line));
        var activated = Preview(renderer, document, line, new(1));
        Assert.True(HasColor(activated, 0));
        Assert.True(HasColor(activated, 1));
        Assert.False(HasColor(activated, 2));
        Assert.Equal(activated, Preview(renderer, document, line, new(3, 2)));
        Assert.Same(segment, Assert.Single(line.Karaoke));
        Assert.Equal(renderer.MeasureSubtitleTextLayout(document, original).Graphemes.ToArray(),
            renderer.MeasureSubtitleTextLayout(document, line).Graphemes.ToArray());
    }

    [Theory]
    [InlineData("Wi")]
    [InlineData("WW\nii")]
    public void DifferentVisualRangesShareTheWholeGroupAdvanceAcrossCharactersAndLines(string text)
    {
        var document = Document(text);
        var firstSmall = text.IndexOf('i');
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(0, text.Length, MediaTime.Zero, new(1), SceneColor.White)],
            KaraokeStyleSpans = [new(0, firstSmall, new() { Fill = new(1, 0, 0) }),
                new(firstSmall, text.Length - firstSmall, new() { Fill = new(0, 1, 0) })]
        };
        using var renderer = Renderer();
        var beforeSmall = Preview(renderer, document, line, new(3, 5));
        Assert.True(HasColor(beforeSmall, 0));
        Assert.False(HasColor(beforeSmall, 1));
        Assert.True(HasColor(beforeSmall, 2));
        Assert.True(HasColor(Preview(renderer, document, line, new(19, 20)), 1));
    }

    [Theory]
    [InlineData(SubtitlePreviewMode.HIGHLIGHTED)]
    [InlineData(SubtitlePreviewMode.INACTIVE)]
    public void UntimedRangesPreviewBothStatesWhileTimedPlaybackKeepsThemDormant(SubtitlePreviewMode mode)
    {
        var document = Document("ffi");
        var original = document.Subtitles[0];
        var active = new KaraokeVisualStyleOverride { Fill = new(1, 0, 0), StrokeWidth = 3, Stroke = new(0, 1, 0) };
        var inactive = new KaraokeVisualStyleOverride { Fill = new(0, 1, 0), FillBlur = 2 };
        var line = original with { KaraokeStyleSpans = [new(0, 3, active, inactive)] };
        using var renderer = Renderer();
        Assert.Equal(Preview(renderer, document, original), Preview(renderer, document, line, new(100)));
        Assert.Equal(Preview(renderer, document, original), Preview(renderer, document, line, new(100), SubtitlePreviewMode.NORMAL));
        var expected = original with { Style = (mode == SubtitlePreviewMode.HIGHLIGHTED ? active : inactive).ApplyTo(original.Style) };
        Assert.Equal(Preview(renderer, document, expected), Preview(renderer, document, line, MediaTime.Zero, mode));
        Assert.Empty(line.Karaoke);
        Assert.Empty(line.InactiveKaraoke);
        Assert.Equal(renderer.MeasureSubtitleTextLayout(document, original).Graphemes.ToArray(),
            renderer.MeasureSubtitleTextLayout(document, line).Graphemes.ToArray());
    }

    [Fact]
    public void UntimedTextWithoutAStoredPresetUsesTheSharedDefaultHighlightOnlyInVisualPreview()
    {
        var document = Document("ffi");
        var line = document.Subtitles[0];
        var expected = line with { Style = line.Style with { Fill = KaraokeVisualStyleResolver.DefaultHighlightColor } };
        using var renderer = Renderer();
        Assert.Equal(Preview(renderer, document, expected),
            Preview(renderer, document, line, new(100), SubtitlePreviewMode.HIGHLIGHTED));
        Assert.Equal(Preview(renderer, document, line),
            Preview(renderer, document, line, new(100), SubtitlePreviewMode.TIMED));
        Assert.Empty(line.Karaoke);
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void DisabledLegacyTimingSuppliesPreviewDefaultsWhileItsClockStaysDormant(KaraokeHighlightKind kind)
    {
        var document = Document("ffi");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { StrokeWidth = 4, Stroke = new(0, 1, 0) },
            InactiveKaraoke = [new(0, 3, new(50), new(100), new(1, 0, 0)) { HighlightKind = kind }] };
        using var renderer = Renderer();
        var activeReference = line with { Style = line.Style with { Fill = new(1, 0, 0) } };
        Assert.Equal(Preview(renderer, document, activeReference),
            Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
        Assert.Equal(Preview(renderer, document, activeReference),
            Preview(renderer, document, line, new(75), SubtitlePreviewMode.HIGHLIGHTED));
        var inactiveReference = kind == KaraokeHighlightKind.OUTLINE_STEP
            ? line with { Style = line.Style with { StrokeWidth = 0 } } : line;
        Assert.Equal(Preview(renderer, document, inactiveReference),
            Preview(renderer, document, line, new(75), SubtitlePreviewMode.INACTIVE));
        Assert.Equal(Preview(renderer, document, line), Preview(renderer, document, line, new(75)));
        Assert.Empty(line.Karaoke);
        Assert.Single(line.InactiveKaraoke);
    }

    [Fact]
    public void InactiveOutlineStepHidesAnExplicitRangeStrokeAfterResolvingTheOverride()
    {
        var document = Document("ffi");
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(0, 3, new(1), new(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP }],
            KaraokeStyleSpans = [new(0, 3, new() { Fill = new(1, 0, 0) },
                new() { Stroke = new(0, 1, 0), StrokeWidth = 12 })]
        };
        using var renderer = Renderer();
        var normal = Preview(renderer, document, document.Subtitles[0]);
        Assert.Equal(normal, Preview(renderer, document, line));
        Assert.Equal(normal, Preview(renderer, document, line, new(10), SubtitlePreviewMode.INACTIVE));
    }

    [Theory]
    [InlineData("ffi", "NotoSans.ttf", 0)]
    [InlineData("ffi", "NotoSans.ttf", -20)]
    [InlineData("لا", "NotoSansArabic.ttf", 0)]
    public void PerCharacterColorsKeepTheOriginalLigatureInkAndCaretGeometry(string text, string font, double spacing)
    {
        var document = Document(text, font);
        var original = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { LetterSpacing = spacing } };
        var colors = new SceneColor[] { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
        var line = original with { KaraokeStyleSpans = [.. Enumerable.Range(0, text.Length)
            .Select(index => new SubtitleKaraokeStyleSpan(index, 1, new() { Fill = colors[index % colors.Length] }))] };
        using var renderer = Renderer();
        var expected = Preview(renderer, document, original);
        var actual = Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
        Assert.Equal(Alpha(expected), Alpha(actual));
        Assert.True(HasColor(actual, 0));
        Assert.True(HasColor(actual, 1));
        var before = renderer.MeasureSubtitleTextLayout(document, original);
        var after = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(before.Runs.ToArray(), after.Runs.ToArray());
        Assert.Equal(before.Graphemes.ToArray(), after.Graphemes.ToArray());
    }

    [Fact]
    public void SharedLigatureUsesOriginalOutlineWithoutStrokingTheInternalStyleCuts()
    {
        var document = Document("ffi");
        var original = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { Fill = SceneColor.Transparent, StrokeWidth = 4, Stroke = new(1, 0, 0) }
        };
        var line = original with { KaraokeStyleSpans =
            [new(0, 1, new() { Fill = SceneColor.Transparent, Stroke = new(1, 0, 0) }),
                new(1, 1, new() { Fill = SceneColor.Transparent, Stroke = new(0, 1, 0) }),
                new(2, 1, new() { Fill = SceneColor.Transparent, Stroke = new(0, 0, 1) })] };
        using var renderer = Renderer();
        Assert.Equal(Alpha(Preview(renderer, document, original)),
            Alpha(Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DecorationCutsDoNotAddOutlinesAtInternalVisualBoundaries(bool underline, bool strike)
    {
        var document = Document("ffi");
        var original = document.Subtitles[0] with { Style = document.Subtitles[0].Style with
            { Underline = underline, Strikethrough = strike, StrokeWidth = 3, Fill = SceneColor.Transparent, Stroke = new(1, 0, 0) } };
        var line = original with { KaraokeStyleSpans =
            [new(0, 1, new() { Fill = SceneColor.Transparent, Stroke = new(1, 0, 0) }),
                new(1, 1, new() { Fill = SceneColor.Transparent, Stroke = new(0, 1, 0) }),
                new(2, 1, new() { Fill = SceneColor.Transparent, Stroke = new(0, 0, 1) })] };
        using var renderer = Renderer();
        Assert.Equal(Alpha(Preview(renderer, document, original)),
            Alpha(Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED)));
    }

    [Fact]
    public void DifferentLigatureFillColorsDoNotDuplicateTheirSharedTranslucentShadow()
    {
        var document = Document("ffi");
        var original = document.Subtitles[0] with { Style = document.Subtitles[0].Style with
            { Fill = new(1, 1, 1, 0.4), ShadowColor = new(1, 1, 1, 0.4), ShadowOffset = new(25, 0), ShadowBlur = 3 } };
        var line = original with { KaraokeStyleSpans =
            [new(0, 1, new() { Fill = new(1, 0, 0, 0.4) }), new(1, 1, new() { Fill = new(0, 1, 0, 0.4) }),
                new(2, 1, new() { Fill = new(0, 0, 1, 0.4) })] };
        using var renderer = Renderer();
        Assert.Equal(Alpha(Preview(renderer, document, original)),
            Alpha(Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED)));
    }

    [Fact]
    public void OverlappingClustersKeepTheOriginalGlyphOrderWhenAVisualStyleRepeats()
    {
        var document = Document("ABA");
        var original = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { LetterSpacing = -44 } };
        var red = new SceneColor(1, 0, 0, 0.6);
        var green = new SceneColor(0, 1, 0, 0.6);
        var line = original with { KaraokeStyleSpans =
            [new(0, 1, new() { Fill = red }), new(1, 1, new() { Fill = green }), new(2, 1, new() { Fill = red })] };
        using var renderer = Renderer();
        var baseline = Assert.Single(renderer.MeasureSubtitleTextLayout(document, original).Runs).Baseline;
        using var shaper = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        using var shape = shaper.Shape("ABA", 64, TextDirection.LEFT_TO_RIGHT, "und", -44);
        using var expected = new LinearRenderSurface(new(document.Width, document.Height, (float)document.ReferenceWhiteNits));
        foreach (var cluster in shape.Clusters)
        {
            var color = cluster.Utf16Start == 1 ? green : red;
            using var paint = new SKPaint { IsAntialias = true };
            paint.SetColor(new((float)color.Red, (float)color.Green, (float)color.Blue, (float)color.Alpha), expected.ColorSpace);
            expected.Canvas.DrawText(shape.GetClusterBlob(cluster.Utf16Start), baseline.X, baseline.Y, paint);
        }
        var pixels = new Half[expected.Info.ChannelCount];
        expected.CopyPixels(pixels);
        Assert.Equal(pixels, Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
    }

    [Fact]
    public void AWholeGlyphKeepsStrokeBlurAndShadowBeyondTheNeighboringCharacterDomain()
    {
        var document = Document("HH");
        var active = new KaraokeVisualStyleOverride
        {
            Fill = new(1, 0, 0, 0.6), FillBlur = 1, Stroke = new(0, 1, 0, 0.6), StrokeWidth = 12,
            StrokeBlur = 2, ShadowColor = new(0, 0, 1, 0.6), ShadowOffset = new(40, 5), ShadowBlur = 3
        };
        var hidden = new KaraokeVisualStyleOverride
        {
            Fill = SceneColor.Transparent, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        };
        var line = document.Subtitles[0] with { KaraokeStyleSpans = [new(0, 1, active), new(1, 1, hidden)] };
        var reference = document.Subtitles[0] with { Text = "H", Style = active.ApplyTo(document.Subtitles[0].Style) };
        using var renderer = Renderer();
        Assert.Equal(Preview(renderer, document, reference), Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
    }

    [Fact]
    public void SharedClusterBlurAndShadowAreGeneratedAfterTheCharacterSourceIsOwned()
    {
        var document = Document("ffi");
        var hidden = new KaraokeVisualStyleOverride { Fill = SceneColor.Transparent, StrokeWidth = 0, ShadowColor = SceneColor.Transparent };
        var line = document.Subtitles[0] with { KaraokeStyleSpans =
            [new(0, 1, new() { Fill = new(1, 0, 0), FillBlur = 4, ShadowColor = new(0, 0, 1), ShadowOffset = new(35, 0), ShadowBlur = 3 }),
                new(1, 2, hidden)] };
        using var renderer = Renderer();
        var caret = renderer.MeasureSubtitleTextLayout(document, line).GetCaretBounds(1).Left;
        var pixels = Preview(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
        var redBeyondSource = false;
        var shadowBeyondSource = false;
        for (var pixel = 0; pixel < pixels.Length / 4; pixel++)
        {
            var x = pixel % document.Width;
            redBeyondSource |= x > caret + 1 && (float)pixels[pixel * 4] > 0.001f;
            shadowBeyondSource |= x > caret + 36 && (float)pixels[pixel * 4 + 2] > 0.001f;
        }
        Assert.True(redBeyondSource);
        Assert.True(shadowBeyondSource);
    }

    [Fact]
    public void OnlyChangingVisualRangesInvalidatesCachedFramesAtTheSamePlaybackTime()
    {
        var document = Document("Wi");
        document = document with { Subtitles = [document.Subtitles[0] with
            { Karaoke = [new(0, 2, MediaTime.Zero, new(1), SceneColor.White)] }] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        var before = pixels.ToArray();
        var changed = document with { Subtitles = [document.Subtitles[0] with
            { KaraokeStyleSpans = [new(1, 1, new() { Fill = new(0, 1, 0) })] }] };
        Assert.True(renderer.CopyCachedFramePixels(changed, new(1), pixels));
        Assert.False(before.AsSpan().SequenceEqual(pixels));
        using var surface = renderer.Render(changed, new(1));
        var uncached = new float[pixels.Length];
        surface.CopyPixels(uncached);
        Assert.Equal(uncached, pixels);
        Assert.False(renderer.CopyCachedFramePixels(changed, new(1), pixels));
    }

    private static Half[] Preview(ProjectSceneRenderer renderer, ProjectDocument document, SubtitleLine line,
        MediaTime time = default, SubtitlePreviewMode mode = SubtitlePreviewMode.TIMED)
    {
        using var surface = renderer.RenderSubtitlePreview(document, line, time,
            new(0, 0, document.Width, document.Height), mode);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static Half[] Alpha(Half[] pixels)
    {
        return pixels.Where((_, index) => index % 4 == 3).ToArray();
    }

    private static bool HasColor(Half[] pixels, int channel)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if ((float)pixels[index + channel] > 0.5f && (float)pixels[index + (channel + 1) % 3] < 0.01f &&
                (float)pixels[index + (channel + 2) % 3] < 0.01f)
            {
                return true;
            }
        }
        return false;
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static ProjectDocument Document(string text, string fontName = "NotoSans.ttf")
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/" + fontName);
        var line = new SubtitleLine { Text = text, End = new(10), Style = new()
        {
            Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 256 },
            FontAssetId = font.Id, FontSize = 64, Fill = new(0, 0, 1), StrokeWidth = 0,
            ShadowColor = SceneColor.Transparent, Alignment = TextAlignment.TOP_LEFT, Margins = new(24, 24, 24)
        } };
        return new() { Width = 384, Height = 256, Assets = [font], Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
