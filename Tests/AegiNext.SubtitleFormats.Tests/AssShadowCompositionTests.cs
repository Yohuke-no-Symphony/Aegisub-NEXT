using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssShadowCompositionTests
{
    [Theory]
    [InlineData(0, 0, 0, 1, 1, 0, true)]
    [InlineData(0, 0, 0, 1, 1, 2, false)]
    [InlineData(0, 8, 4, 1, 1, 0, false)]
    [InlineData(6, 8, 4, 1, 1, 0, true)]
    [InlineData(6, 8, 4, 0, 1, 0, true)]
    [InlineData(6, 8, 4, 1, 0, 0, false)]
    [InlineData(0, 0, 0, 1, 0, 0, false)]
    public void ExportReportsOnlyTheObservedNontransparentShadowCompositionConditions(double width, double x, double y,
        double strokeAlpha, double shadowAlpha, double blur, bool loss)
    {
        var line = Line() with
        {
            Style = new() { StrokeWidth = width, Stroke = SceneColor.Black with { Alpha = strokeAlpha },
                ShadowColor = SceneColor.Black with { Alpha = shadowAlpha }, ShadowOffset = new(x, y), ShadowBlur = blur }
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Equal(loss, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.ShadowComposition" && diagnostic.SubtitleId == line.Id));
        Assert.Equal(width, line.Style.StrokeWidth);
        Assert.Equal(new ScenePoint(x, y), line.Style.ShadowOffset);
        Assert.Equal(shadowAlpha, line.Style.ShadowColor.Alpha);
    }

    [Theory]
    [InlineData("{\\bord0\\shad0}ab", true)]
    [InlineData("{\\bord0\\shad8}ab", false)]
    [InlineData("{\\bord6\\xshad8\\yshad4\\3a&HFF&}ab", true)]
    [InlineData("{\\bord6\\xshad8\\yshad4\\4a&HFF&}ab", false)]
    [InlineData("{\\bord0\\shad0\\4a&HFF&}ab", false)]
    public void ImportUsesActualInlineShadowAppearanceAndRetainsTheSourceParameters(string source, bool loss)
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(source), 640, 360);
        var line = Assert.Single(imported.Lines);
        var ordinary = OrdinaryAt(line);

        Assert.Equal(loss, imported.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.ShadowComposition" && diagnostic.SubtitleId == line.Id));
        Assert.Equal(source.Contains("\\4a", StringComparison.Ordinal) ? 0 : 159d / 255, ordinary.ShadowColor.Alpha);
        Assert.Equal(source.Contains("\\bord6", StringComparison.Ordinal) ? 6 : 0, ordinary.StrokeWidth);
    }

    [Fact]
    public void ImportDetectsActiveRangeShadowEvenWhenItsOrdinaryShadowIsTransparent()
    {
        var source = "{\\4a&HFF&\\kt25\\k100\\t(250,250,\\bord6\\xshad8\\yshad4\\4a&H00&)}ab";
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File(source), 640, 360);
        var line = Assert.Single(result.Lines);

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowComposition");
        Assert.Equal(0, OrdinaryAt(line).ShadowColor.Alpha);
        Assert.Equal(1, Assert.Single(line.KaraokeStyleSpans).ActiveStyle!.ShadowColor!.Value.Alpha);
        Assert.Single(line.Karaoke);
    }

    [Fact]
    public void FullyTransparentStateOverridesDoNotReportTheUnusedOrdinaryShadow()
    {
        var line = Line() with
        {
            Karaoke = [Group()],
            KaraokeStyleSpans = [new(0, 2, new() { ShadowColor = SceneColor.Transparent }, new() { ShadowColor = SceneColor.Transparent })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowComposition");
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP, false)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP, false)]
    [InlineData(KaraokeHighlightKind.SWEEP, true)]
    public void AHiddenInactiveShadowAtZeroActivationDoesNotTriggerUnlessTheModeActuallyDisplaysIt(KaraokeHighlightKind kind, bool loss)
    {
        var line = Line() with
        {
            Style = new() { ShadowColor = SceneColor.Transparent, ShadowBlur = 0, StrokeWidth = 0 },
            Karaoke = [Group() with { Start = MediaTime.Zero, HighlightKind = kind }],
            KaraokeStyleSpans = [new(0, 2, new() { ShadowColor = SceneColor.Transparent },
                new() { ShadowColor = SceneColor.Black, ShadowOffset = new(0, 0) })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Equal(loss, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.ShadowComposition"));
    }

    [Fact]
    public void AnActiveShadowOutsideTheVisibleWindowAndDormantPaintDoNotTriggerCompositionWarnings()
    {
        var line = Line() with
        {
            Text = "abcd", Style = new() { ShadowColor = SceneColor.Transparent, ShadowBlur = 0 },
            Karaoke = [Group() with { Start = new(3), End = new(4) }],
            KaraokeStyleSpans = [new(0, 4, new() { ShadowColor = SceneColor.Black, ShadowOffset = new(8, 4), StrokeWidth = 6 },
                new() { ShadowColor = SceneColor.Transparent })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowComposition");
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
    }

    [Fact]
    public void AnInactiveRangeShadowIsDiagnosedBeforeActivation()
    {
        var line = Line() with
        {
            Style = new() { ShadowColor = SceneColor.Transparent, ShadowBlur = 0, StrokeWidth = 0 },
            Karaoke = [Group()],
            KaraokeStyleSpans = [new(0, 2, new() { ShadowColor = SceneColor.Transparent },
                new() { ShadowColor = SceneColor.Black, ShadowOffset = new(8, 4), StrokeWidth = 6 })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowComposition");
    }

    private static SubtitleLine Line() => new() { Text = "ab", End = new(2), Style = new() { ShadowBlur = 0 } };

    private static KaraokeSegment Group() => new(0, 2, new(1, 4), new(5, 4), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP };

    private static SubtitleStyle OrdinaryAt(SubtitleLine line) => line.InlineSpans.FirstOrDefault()?.Style.ApplyTo(line.Style) ?? line.Style;
}
