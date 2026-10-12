using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleRichTextRenderingTests
{
    [Fact]
    public void MixedStylesShareBaselineAndPreserveNativeFreeSnapshotAfterDisposal()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(1, 2, new() { FontSize = 44, Fill = new(1, 0, 0), Bold = true, Italic = true })]
        };
        document = document with { Subtitles = [line] };
        SubtitleTextLayout layout;
        using (var renderer = Renderer())
        {
            layout = renderer.MeasureSubtitleTextLayout(document, line);
            Assert.Equal(3, layout.Runs.Length);
            Assert.Single(layout.Runs.Select(run => run.Baseline.Y).Distinct());
            Assert.Equal(0, layout.Runs[0].Utf16Start);
            Assert.Equal(1, layout.Runs[1].Utf16Start);
            Assert.Equal(3, layout.Runs[2].Utf16Start);
            Assert.Equal(44, layout.Runs[1].Style.FontSize);
            Assert.True(layout.Runs[1].Style.Bold);
            Assert.True(layout.Runs[1].Style.Italic);
            Assert.Equal(line.Style.FontAssetId, layout.Runs[1].Style.FontAssetId);
            var pixels = Pixels(renderer, document, MediaTime.Zero);
            Assert.True(HasColor(pixels, 0));
            Assert.True(HasColor(pixels, 2));
        }
        var caret = layout.GetCaretBounds(2);
        var hit = layout.HitTest(new(caret.Left, caret.MidY));
        Assert.Equal(2, hit.Utf16Offset);
        Assert.Single(layout.GetSelectionRects(1, 2));
    }

    [Fact]
    public void RichTextUsesActualWholeLineAnimationOnlyForTheAnimatedField()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(1, 2, new() { Fill = new(1, 0, 0), Stroke = new(1, 0, 0) })]
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var unanimated = Pixels(renderer, document, MediaTime.Zero);
        Assert.True(HasColor(unanimated, 0));
        Assert.True(HasColor(unanimated, 2));
        var layer = document.Layers[0] with
        {
            Tracks = [new(AnimationProperty.FILL, [new(MediaTime.Zero, new SceneColor(0, 1, 0))])]
        };
        var animated = document with { Layers = [layer] };
        var pixels = Pixels(renderer, animated, MediaTime.Zero);
        Assert.True(HasColor(pixels, 1));
        Assert.False(HasColor(pixels, 0));
        Assert.False(HasColor(pixels, 2));
        var preservedStroke = animated with { Subtitles = [line with { InlineSpans =
            [new(1, 2, new() { Fill = new(1, 0, 0), Stroke = new(1, 0, 0), StrokeWidth = 3 })] }] };
        Assert.True(HasColor(Pixels(renderer, preservedStroke, MediaTime.Zero), 0));
    }

    [Fact]
    public void GraphemeAndLigatureGeometryKeepsSafeCaretBoundariesAcrossHardAndSoftWrapping()
    {
        var document = Document("A\u0301ffi\nXY\n");
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(2, layout.Graphemes[0].Utf16Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetCaretBounds(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSelectionRects(0, 1));
        Assert.True(layout.GetCaretBounds(2).Left < layout.GetCaretBounds(3).Left);
        Assert.Equal(2, layout.GetSelectionRects(0, line.Text.Length).Count(rect => rect.Width > 1));
        Assert.True(layout.GetCaretBounds(line.Text.Length).Top > layout.GetCaretBounds(6).Top);
        var wrapped = document with
        {
            Width = 42,
            Subtitles = [line with
            {
                Text = "WWWW", Style = line.Style with
                {
                    Scaling = line.Style.Scaling with { ReferenceWidth = 42 }, Margins = new(4, 4, 4)
                }
            }]
        };
        var wrapLayout = renderer.MeasureSubtitleTextLayout(wrapped, wrapped.Subtitles[0]);
        Assert.True(wrapLayout.Runs.Length > 1);
        var boundary = wrapLayout.Runs[1].Utf16Start;
        Assert.Equal(wrapLayout.Graphemes.First(glyph => glyph.Utf16Start == boundary).LeadingCaret,
            wrapLayout.GetCaretBounds(boundary));
    }

    [Fact]
    public void EmptyFinalLineHitTestsToItsTrailingNewlineBoundary()
    {
        var document = Document("\n");
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var caret = layout.GetCaretBounds(1);
        Assert.Equal(1, layout.HitTest(new(caret.Left, caret.MidY)).Utf16Offset);
        Assert.Single(layout.GetSelectionRects(0, 1));
    }

    [Fact]
    public void LongWrappedTextRetainsEveryGraphemeAndSparseKaraokeAtTheEnd()
    {
        var text = new string('W', 2048);
        var document = Document(text, width: 96);
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(text.Length - 1, 1, MediaTime.Zero, new(1), new(1, 0, 0))]
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(text.Length, layout.Graphemes.Length);
        Assert.True(layout.Runs.Length > 100);
        Assert.Equal(text.Length, layout.HitTest(new(layout.GetCaretBounds(text.Length).Left,
            layout.GetCaretBounds(text.Length).MidY)).Utf16Offset);
    }

    [Fact]
    public void MixedFontReferencesResolveEachRunAndWrappingUsesTheirActualAdvance()
    {
        var document = Document("ABبب");
        var arabic = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSansArabic.ttf");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(2, 2, new() { FontAssetId = arabic.Id, FontSize = 36 })]
        };
        document = document with { Assets = [document.Assets[0], arabic], Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(2, layout.Runs.Length);
        Assert.Equal(arabic.Id, layout.Runs[1].Style.FontAssetId);
        Assert.Single(layout.Runs.Select(run => run.Baseline.Y).Distinct());
        Assert.Contains(Pixels(renderer, document, MediaTime.Zero), value => value > (Half)0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnderlineAndStrikeUseResolvedRunMetricsAndChangeOnlyStyledRange(bool underline, bool strike)
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(1, 2, new() { Underline = underline, Strikethrough = strike })]
        };
        using var renderer = Renderer();
        var before = Pixels(renderer, document, MediaTime.Zero);
        var decorated = document with { Subtitles = [line] };
        var after = Pixels(renderer, decorated, MediaTime.Zero);
        Assert.NotEqual(before, after);
        var layout = renderer.MeasureSubtitleTextLayout(decorated, line);
        Assert.False(layout.Runs[0].Style.Underline);
        Assert.False(layout.Runs[0].Style.Strikethrough);
        Assert.Equal(underline, layout.Runs[1].Style.Underline);
        Assert.Equal(strike, layout.Runs[1].Style.Strikethrough);
        Assert.True(after.Count(value => value > (Half)0) > before.Count(value => value > (Half)0));
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void StepModesActivateAtExactStartAndPreserveInactiveStyle(KaraokeHighlightKind kind)
    {
        var document = Document("ABC");
        var line = document.Subtitles[0];
        var segment = new KaraokeSegment(0, 3, new(1), new(2), new(1, 0, 0))
        {
            HighlightKind = kind
        };
        document = document with { Subtitles = [line with { Karaoke = [segment], KaraokeStyleSpans =
            [new(0, 3, new() { Fill = new(1, 0, 0) }, new() { Fill = new(0, 1, 0) })] }] };
        using var renderer = Renderer();
        var before = Pixels(renderer, document, MediaTime.Zero);
        Assert.True(HasColor(before, 1));
        Assert.False(HasColor(before, 0));
        var started = Pixels(renderer, document, new(1));
        Assert.True(HasColor(started, 0));
        Assert.False(HasColor(started, 1));
        Assert.Equal(started, Pixels(renderer, document, new(3, 2)));
    }

    [Fact]
    public void OutlineStepSuppressesOnlyInactiveOutlineAndRestoresItAtStart()
    {
        var document = Document("ABC");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { StrokeWidth = 3, Stroke = new(0, 1, 0) } };
        var segment = new KaraokeSegment(0, 3, new(1), new(2), line.Style.Fill) { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP };
        document = document with { Subtitles = [line with { Karaoke = [segment] }] };
        using var renderer = Renderer();
        var before = Pixels(renderer, document, MediaTime.Zero);
        Assert.False(HasColor(before, 1));
        Assert.True(HasColor(Pixels(renderer, document, new(1)), 1));
        var noOutline = document with { Subtitles = [line with { Style = line.Style with { StrokeWidth = 0 } }] };
        Assert.Equal(Pixels(renderer, noOutline, MediaTime.Zero), before);
    }

    [Fact]
    public void SweepTraversesMixedSizeRunsAndExplicitHighlightWinsAnimatedFill()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(2, 2, new() { FontSize = 44 })],
            Karaoke = [new(0, 4, new(1), new(2), new(1, 0, 0))],
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) })]
        };
        document = document with { Subtitles = [line], Layers = [document.Layers[0] with
        {
            Tracks = [new(AnimationProperty.FILL, [new(MediaTime.Zero, new SceneColor(0, 1, 0))])]
        }] };
        using var renderer = Renderer();
        Assert.Equal(Pixels(renderer, document, MediaTime.Zero), Pixels(renderer, document, new(1)));
        var partial = Pixels(renderer, document, new(3, 2));
        Assert.True(HasColor(partial, 0));
        Assert.True(HasColor(partial, 1));
        var complete = Pixels(renderer, document, new(2));
        Assert.True(HasColor(complete, 0));
        Assert.False(HasColor(complete, 1));
        var reference = document with
        {
            Subtitles = [line with { Karaoke = [], Style = line.Style with { Fill = new(1, 0, 0) } }],
            Layers = [document.Layers[0] with { Tracks = [] }]
        };
        Assert.Equal(Pixels(renderer, reference, new(2)), complete);
    }

    [Fact]
    public void InlineChangesInvalidatePreviewAndBgraExportPreservesTransparency()
    {
        var document = Document("ABC");
        using var renderer = Renderer();
        var background = new byte[document.Width * document.Height * 4];
        var before = renderer.ComposePreview(document, MediaTime.Zero, background, document.Width, document.Height, document.Width * 4);
        var changed = document with { Subtitles = [document.Subtitles[0] with
        {
            InlineSpans = [new(0, 3, new() { Fill = new(1, 0, 0), FontSize = 36 })]
        }] };
        var after = renderer.ComposePreview(changed, MediaTime.Zero, background, document.Width, document.Height, document.Width * 4);
        Assert.NotEqual(before, after);
        using var transparent = renderer.Render(changed, MediaTime.Zero);
        var pixels = transparent.CopySrgbBgra();
        Assert.Equal(0, pixels[3]);
        Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [Fact]
    public void OverflowingGraphemeTimesKeepLayoutAndStopDrawingAtExclusiveCueEnd()
    {
        var document = Document("AB");
        var line = document.Subtitles[0] with
        {
            End = new(1),
            InlineSpans = [new(1, 1, new() { Bold = true })],
            Karaoke = [new(0, 1, new(0), new(10), new(1, 0, 0)), new(1, 1, new(10), new(20), new(1, 0, 0))]
        };
        document = document with { Subtitles = [line], Layers = [document.Layers[0] with { End = line.End }] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(2, layout.Graphemes.Length);
        Assert.True(HasColor(Pixels(renderer, document, new(999, 1000)), 2));
        Assert.DoesNotContain(Pixels(renderer, document, line.End), value => value != Half.Zero);
        Assert.DoesNotContain(Pixels(renderer, document, new(15)), value => value != Half.Zero);
        var background = new byte[document.Width * document.Height * 4];
        var preview = renderer.ComposePreview(document, line.End, background, document.Width, document.Height, document.Width * 4);
        var empty = renderer.ComposePreview(document with { Layers = [], Subtitles = [] }, line.End, background, document.Width, document.Height, document.Width * 4);
        Assert.Equal(empty, preview);
        Assert.Equal(layout.Graphemes.ToArray(), renderer.MeasureSubtitleTextLayout(document, line).Graphemes.ToArray());
    }

    [Fact]
    public void AdjacentSegmentsDoNotChangeBasePixelsBeforeTheirFirstStart()
    {
        var document = Document("ABCDE");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { StrokeWidth = 2 } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var expected = Pixels(renderer, document, MediaTime.Zero);
        var segmented = document with { Subtitles = [line with
        {
            Karaoke = [new(0, 1, new(1), new(2), new(1, 0, 0)), new(1, 1, new(1), new(2), new(1, 0, 0)),
                new(2, 3, new(1), new(2), new(1, 0, 0))]
        }] };
        Assert.Equal(expected, Pixels(renderer, segmented, MediaTime.Zero));
    }

    [Fact]
    public void CompletedAdjacentExplicitHighlightsMatchAUniformReferenceWithoutClippingSeams()
    {
        var document = Document("ABCDE");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { StrokeWidth = 2 } };
        var highlight = new KaraokeVisualStyleOverride { Fill = new(1, 0, 0) };
        document = document with { Subtitles = [line with
        {
            Karaoke = [new(0, 1, MediaTime.Zero, new(1), new(1, 0, 0)),
                new(1, 1, MediaTime.Zero, new(1), new(1, 0, 0)),
                new(2, 3, MediaTime.Zero, new(1), new(1, 0, 0))],
            KaraokeStyleSpans = [new(0, 1, highlight), new(1, 1, highlight), new(2, 3, highlight)]
        }] };
        using var renderer = Renderer();
        var expected = document with { Subtitles = [line with { Style = line.Style with { Fill = new(1, 0, 0) } }] };
        Assert.Equal(Pixels(renderer, expected, new(1)), Pixels(renderer, document, new(1)));
    }

    [Fact]
    public void LocalPreviewMatchesFullCanvasPixelsAndIgnoresLayerEffectsAndPlacement()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with { InlineSpans = [new(1, 2, new() { Fill = new(1, 0, 0), Underline = true })] };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var bounds = layout.Bounds;
        var crop = new SKRect((float)Math.Floor(bounds.Left) - 2, (float)Math.Floor(bounds.Top) - 2,
            (float)Math.Ceiling(bounds.Right) + 2, (float)Math.Ceiling(bounds.Bottom) + 2);
        using var local = renderer.RenderSubtitlePreview(document, line, MediaTime.Zero, crop);
        using var whole = renderer.Render(document, MediaTime.Zero);
        var localPixels = local.CopySrgbBgra();
        var wholePixels = whole.CopySrgbBgra();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        var origin = geometry.LocalToWorld.MapPoint(crop.Left, crop.Top);
        for (var row = 0; row < local.Info.Height; row++)
        {
            var sourceOffset = ((int)origin.Y + row) * document.Width * 4 + (int)origin.X * 4;
            Assert.Equal(wholePixels.AsSpan(sourceOffset, local.Info.Width * 4).ToArray(),
                localPixels.AsSpan(row * local.Info.Width * 4, local.Info.Width * 4).ToArray());
        }
        var transformed = document with { Layers = [document.Layers[0] with { Transform = new() { X = 100, Rotation = 25 }, Blur = 5 }] };
        using var neutral = renderer.RenderSubtitlePreview(transformed, line, MediaTime.Zero, crop);
        Assert.Equal(localPixels, neutral.CopySrgbBgra());
        Assert.True(local.Info.Width < document.Width);
        Assert.True(local.Info.Height < document.Height);
    }

    [Fact]
    public void ArabicSweepUsesRightToLeftGeometryAndStartsAtTheRightEdge()
    {
        var document = Document("بببب");
        var font = document.Assets[0] with { RelativePath = "Fixtures/NotoSansArabic.ttf" };
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(0, 4, MediaTime.Zero, new(2), new(1, 0, 0))],
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) })]
        };
        document = document with { Assets = [font], Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.True(layout.Graphemes[0].LeadingCaret.Left > layout.Graphemes[0].TrailingCaret.Left);
        var pixels = Pixels(renderer, document, new(1));
        var red = new List<int>();
        var blue = new List<int>();
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if ((float)pixels[index] > 0.5f && (float)pixels[index + 2] < 0.01f)
            {
                red.Add(index / 4 % document.Width);
            }
            if ((float)pixels[index + 2] > 0.5f && (float)pixels[index] < 0.01f)
            {
                blue.Add(index / 4 % document.Width);
            }
        }
        Assert.NotEmpty(red);
        Assert.NotEmpty(blue);
        Assert.True(red.Average() > blue.Average());
    }

    private static bool HasColor(Half[] pixels, int channel)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if ((float)pixels[index + channel] > 0.5f &&
                (float)pixels[index + (channel + 1) % 3] < 0.01f &&
                (float)pixels[index + (channel + 2) % 3] < 0.01f)
            {
                return true;
            }
        }
        return false;
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static ProjectDocument Document(string text, int width = 256)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = text, End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = width, ReferenceHeight = 160 },
                FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                Fill = new(0, 0, 1), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = width, Height = 160, Assets = [font], Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
