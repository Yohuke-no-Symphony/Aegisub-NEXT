using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssKaraokeClockTests
{
    [Fact]
    public void OverlappingAndReverseGroupsRetainIndependentTimesAndEmitKtOnlyWhenNeeded()
    {
        var line = new SubtitleLine
        {
            Text = "abcd", End = new(3), Style = new() { ShadowBlur = 0 },
            Karaoke =
            [
                new(0, 1, new(1), new(3, 2), SceneColor.White),
                new(1, 1, new(1, 2), new(5, 4), SceneColor.White),
                new(2, 1, new(1, 4), new(3, 4), SceneColor.White),
                new(3, 1, new(2), new(5, 2), SceneColor.White)
            ]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);

        Assert.Equal(line.Karaoke.Select(group => (group.Start, group.End)), imported.Karaoke.Select(group => (group.Start, group.End)));
        Assert.Contains("{\\kt50}", written.Text, StringComparison.Ordinal);
        Assert.Contains("{\\kt25}", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\kt100}", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\kt200}", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeClockCompatibility");
    }

    [Fact]
    public void PositiveGapsAndContiguousGroupsUseOrdinaryCumulativeKaraokeWithoutKt()
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(3), Style = new() { ShadowBlur = 0 },
            Karaoke = [new(0, 1, new(1, 4), new(3, 4), SceneColor.White),
                new(1, 1, new(3, 4), new(1), SceneColor.White), new(2, 1, new(3, 2), new(2), SceneColor.White)]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);

        Assert.DoesNotContain("\\kt", written.Text, StringComparison.Ordinal);
        Assert.Equal(line.Karaoke.Select(group => (group.Start, group.End)), imported.Karaoke.Select(group => (group.Start, group.End)));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeClockCompatibility");
    }

    [Fact]
    public void NegativeSecondGroupRebasesAllGroupsAndMoveFadeNumericAndMaskToTheSameClock()
    {
        var source = "{\\move(0,0,100,200,0,1000)\\fad(1000,0)\\clip(0,0,100,100)" +
            "\\t(0,1000,\\fsp10\\clip(100,0,200,100))\\k100}a{\\kt-50\\k100\\1c&H0000FF&\\2c&HFF0000&}b";
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File(source), 640, 360);
        var clip = Assert.Single(result.Clips);

        Assert.Equal(new MediaTime(1, 2), clip.ContentOffset);
        Assert.Equal(new[] { (new MediaTime(1, 2), new MediaTime(3, 2)), (MediaTime.Zero, new MediaTime(1)) },
            clip.Line.Karaoke.Select(group => (group.Start, group.End)));
        Assert.Equal(new SceneColor(1, 0, 0, 127d / 255), clip.Line.KaraokeStyleSpans[^1].ActiveStyle!.Fill);
        Assert.Equal(new SceneColor(0, 0, 1, 191d / 255), clip.Line.KaraokeStyleSpans[^1].InactiveStyle!.Fill);
        var tracks = clip.Tracks.ToDictionary(track => track.Property);
        Assert.Equal(new ScenePoint(50, 100), SceneEvaluator.EvaluateVectorTrack(tracks[AnimationProperty.POSITION], new(1)));
        Assert.Equal(0.5, SceneEvaluator.EvaluateScalarTrack(tracks[AnimationProperty.OPACITY], new(1)), 12);
        Assert.Equal(5, SceneEvaluator.EvaluateScalarTrack(tracks[AnimationProperty.LETTER_SPACING], new(1)), 12);
        Assert.Equal(new ScenePoint(50, 0), SceneEvaluator.EvaluateVectorTrack(tracks[AnimationProperty.MASK_RECTANGLE_TOP_LEFT], new(1)));

        var document = SubtitleFormatTestDocument.Create(clip.Line) with
        {
            Width = 640, Height = 360,
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = clip.Line.Id, Start = clip.Line.Start, End = clip.Line.End,
                Mask = clip.Mask, AnimationOffset = clip.ContentOffset, Transform = clip.Transform, Tracks = clip.Tracks }]
        };
        var written = AssSubtitleFormat.Write(document);
        var reread = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var rereadTracks = reread.Tracks.ToDictionary(track => track.Property);

        Assert.Equal(clip.ContentOffset, reread.ContentOffset);
        Assert.Equal(clip.Line.Karaoke.Select(group => (group.Start, group.End)), reread.Line.Karaoke.Select(group => (group.Start, group.End)));
        foreach (var property in tracks.Keys)
        {
            Assert.Equal(SceneEvaluator.EvaluateTrack(tracks[property], new(1)), SceneEvaluator.EvaluateTrack(rereadTracks[property], new(1)));
        }
    }

    [Fact]
    public void CollapsedRationalIntervalsKeepTheirIndependentStartsAndReportMinimumDuration()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(1, 1000), Style = new() { ShadowBlur = 0 },
            Karaoke = [new(0, 1, MediaTime.Zero, new(1, 2000), SceneColor.White),
                new(1, 1, new(1, 2000), new(1, 1000), SceneColor.White)]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);

        Assert.Equal(2, imported.Karaoke.Length);
        Assert.All(imported.Karaoke, group => Assert.Equal((MediaTime.Zero, new MediaTime(1, 100)), (group.Start, group.End)));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeQuantization");
        Assert.Equal(new MediaTime(1, 1000), line.End);
    }

    [Fact]
    public void KaraokeMoveNumericAndRectangleMaskUseTheActualQuantizedExternalEventOrigin()
    {
        var line = new SubtitleLine
        {
            Text = "ab", Start = new(1001, 1000), End = new(3001, 1000),
            Style = new() { Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }, ShadowBlur = 0, Alignment = TextAlignment.TOP_LEFT, Position = new() { Pivot = new(0, 0) } },
            Karaoke = [new(0, 2, new(1, 50), new(51, 50), SceneColor.White)]
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) },
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(100, 100))]),
                new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1), 10)]),
                new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(10, 10))])
            ]
        };
        var document = SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360, Layers = [layer] };
        var written = AssSubtitleFormat.Write(document, new(7, 1000));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);

        Assert.Equal(new MediaTime(1), imported.Line.Start);
        Assert.Equal(new MediaTime(3, 100), Assert.Single(imported.Line.Karaoke).Start);
        Assert.Equal(new MediaTime(103, 100), imported.Line.Karaoke[0].End);
        Assert.Contains(",8,1008)", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\t(8,1008,1,\\fsp10)", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\t(8,1008,1,\\clip(10,10,100,100))", written.Text, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeQuantization");
    }

    [Theory]
    [InlineData("{\\kt214748365\\k1}a")]
    [InlineData("{\\kt-214748365\\k1}a")]
    [InlineData("{\\k214748365}a")]
    [InlineData("{\\kt214748364\\k1}a")]
    public void ImportRejectsKaraokeCountsThatOverflowLibassMilliseconds(string text)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(AssBoundarySource.File(text), 640, 360));
    }

    [Fact]
    public void ExportRejectsNativeKaraokeThatExceedsTheLibassReadableClock()
    {
        var line = new SubtitleLine { Text = "a", Karaoke = [new(0, 1, new(214748364, 100), new(214748365, 100), SceneColor.White)] };
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)));
    }

    [Theory]
    [InlineData("{\\k214748364}a", 0)]
    [InlineData("{\\kt-214748364\\k214748364}a", 214748364)]
    public void MaximumReadableDurationAndNegativeClockRemainConvertible(string text, int offset)
    {
        var imported = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File(text), 640, 360).Clips);
        var document = SubtitleFormatTestDocument.Create(imported.Line) with
        {
            Width = 640, Height = 360,
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = imported.Line.Id, Start = imported.Line.Start,
                End = imported.Line.End, AnimationOffset = imported.ContentOffset }]
        };
        var written = AssSubtitleFormat.Write(document);
        var reread = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);

        Assert.Equal(new MediaTime(offset, 100), imported.ContentOffset);
        Assert.Equal(imported.ContentOffset, reread.ContentOffset);
        Assert.Equal(imported.Line.Karaoke[0].Start, reread.Line.Karaoke[0].Start);
        Assert.Equal(imported.Line.Karaoke[0].End, reread.Line.Karaoke[0].End);
    }

    [Fact]
    public void UnreadablyLargeNativeClockFailsWithAConversionErrorInsteadOfArithmeticOverflow()
    {
        var line = new SubtitleLine
        {
            Text = "a", Karaoke = [new(0, 1, new(long.MaxValue - 1), new(long.MaxValue), SceneColor.White)]
        };
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line)));
    }

    [Theory]
    [InlineData("k", KaraokeHighlightKind.STEP, -50)]
    [InlineData("ko", KaraokeHighlightKind.OUTLINE_STEP, -50)]
    public void NegativeActivationRetainsInactiveAndActiveEdgeStylesWithoutSplittingTheGroup(string tag, KaraokeHighlightKind kind, int start)
    {
        var milliseconds = start * 10;
        var source = "{\\kt" + start + "\\" + tag + "100\\t(" + milliseconds + "," + milliseconds +
            ",\\bord10\\xshad12\\yshad8)}ab";
        var imported = Assert.Single(AssSubtitleFormat.Parse(AssBoundarySource.File(source), 640, 360).Clips);
        var group = Assert.Single(imported.Line.Karaoke);
        var active = Assert.Single(imported.Line.KaraokeStyleSpans).ActiveStyle!;
        var ordinary = imported.Line.InlineSpans[0].Style.ApplyTo(imported.Line.Style);

        Assert.Equal(kind, group.HighlightKind);
        Assert.Equal((0, 2), (group.Utf16Start, group.Utf16Length));
        Assert.Equal(10, active.StrokeWidth);
        Assert.Equal(new ScenePoint(12, 8), active.ShadowOffset);
        Assert.Equal(2, ordinary.StrokeWidth);
        Assert.Equal(new ScenePoint(2, 2), ordinary.ShadowOffset);
        var document = SubtitleFormatTestDocument.Create(imported.Line) with
        {
            Width = 640, Height = 360,
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = imported.Line.Id, Start = imported.Line.Start,
                End = imported.Line.End, AnimationOffset = imported.ContentOffset }]
        };
        var written = AssSubtitleFormat.Write(document);
        var reread = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);

        Assert.Contains("\\t(" + milliseconds + "," + milliseconds + ",", written.Text, StringComparison.Ordinal);
        Assert.Single(reread.Line.Karaoke);
        Assert.Equal(imported.ContentOffset, reread.ContentOffset);
        Assert.Equal(group.Start, reread.Line.Karaoke[0].Start);
        Assert.Equal(group.End, reread.Line.Karaoke[0].End);
        Assert.Equal(active.StrokeWidth, reread.Line.KaraokeStyleSpans[0].ActiveStyle!.StrokeWidth);
        Assert.Equal(active.ShadowOffset, reread.Line.KaraokeStyleSpans[0].ActiveStyle!.ShadowOffset);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeVisual");
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void ZeroActivationUsesActiveEdgesAndDiagnosesTheHiddenInactiveStyleInsteadOfWritingAFullEventTransform(KaraokeHighlightKind kind)
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2), Style = new() { ShadowBlur = 0 },
            Karaoke = [new(0, 2, MediaTime.Zero, new(1), SceneColor.White) { HighlightKind = kind }],
            KaraokeStyleSpans = [new(0, 2, new() { StrokeWidth = 10, ShadowOffset = new(12, 8) },
                new() { StrokeWidth = 2, ShadowOffset = new(2, 2) })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var reread = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);
        var ordinary = reread.InlineSpans[0].Style.ApplyTo(reread.Style);
        var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, reread.KaraokeStyle, reread.Karaoke[0],
            KaraokeVisualStyleResolver.RangeStyleAt(reread, 0, KaraokeVisualState.ACTIVE));

        Assert.Single(reread.Karaoke);
        Assert.Equal(10, active.StrokeWidth);
        Assert.Equal(new ScenePoint(12, 8), active.ShadowOffset);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeVisual");
        Assert.DoesNotContain("\\t(0,0,", written.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void UniformZeroActivationDoesNotReportAHiddenStyleLossThatTheKaraokeTagAlreadyRepresents(KaraokeHighlightKind kind)
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2), Style = new() { ShadowBlur = 0 },
            Karaoke = [new(0, 2, MediaTime.Zero, new(1), SceneColor.White) { HighlightKind = kind }]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeVisual");
    }
}
