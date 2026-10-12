using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssDormantKaraokeLossTests
{
    [Fact]
    public void UntimedHighlightStylesAreReportedWithoutChangingOrdinaryTextOrCreatingTiming()
    {
        var line = Line() with
        {
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) }, new() { Fill = new(0, 0, 1) })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle" && diagnostic.SubtitleId == line.Id);
        Assert.Equal(line.Text, imported.Text);
        Assert.Empty(imported.Karaoke);
        Assert.Empty(imported.KaraokeStyleSpans);
        Assert.Equal(SceneColor.White, imported.Style.Fill);
        Assert.DoesNotContain(imported.InlineSpans, span => span.Style.Fill != SceneColor.White);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighlightRangePartiallyCoveredByTimingReportsItsEffectiveUntimedRemainder(bool inactive)
    {
        var paint = new KaraokeVisualStyleOverride { StrokeWidth = 6 };
        var line = Line() with
        {
            Karaoke = [Group(0, 1), Group(2, 2)],
            KaraokeStyleSpans = [new(0, 4, inactive ? null : paint, inactive ? paint : null)]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
        Assert.Equal(2, Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines).Karaoke.Length);
    }

    [Fact]
    public void HighlightRangeCoveredByTheUnionOfAdjacentGroupsDoesNotReportDormantLoss()
    {
        var line = Line() with
        {
            Karaoke = [Group(0, 1), Group(1, 2), Group(3, 1)],
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) }, new() { Fill = new(0, 0, 1) })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void DormantLossUsesEffectiveInlineAppearanceOnTheUncoveredPortion(double ordinaryStroke, bool loss)
    {
        var line = Line() with
        {
            InlineSpans = [new(0, 2, new() { StrokeWidth = ordinaryStroke }), new(2, 2, new() { StrokeWidth = 5 })],
            Karaoke = [Group(2, 2)],
            KaraokeStyleSpans = [new(0, 4, new() { StrokeWidth = 2 }, new() { StrokeWidth = 2 })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Equal(loss, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle"));
    }

    [Fact]
    public void EmptyOrRedundantStateOverridesDoNotReportDormantLoss()
    {
        var line = Line() with
        {
            KaraokeStyleSpans = [new(0, 2, new(), new()),
                new(2, 2, new() { Fill = KaraokeVisualStyleResolver.DefaultHighlightColor }, new() { Fill = SceneColor.White })]
        };
        var written = AssTextWriter.Write(line, MediaTime.Zero);

        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
    }

    [Fact]
    public void DisabledGroupTimesAndTheirSavedStateStylesAreBothReportedWithoutEnablingGroups()
    {
        var line = Line() with
        {
            InactiveKaraoke = [Group(0, 4)],
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) })]
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines);

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.InactiveKaraoke");
        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
        Assert.Empty(imported.Karaoke);
        Assert.Empty(imported.InactiveKaraoke);
        Assert.Single(line.InactiveKaraoke);
    }

    [Fact]
    public void ExplicitSentenceHighlightWithUntimedTextReportsItsDormantAppearance()
    {
        var line = Line() with
        {
            Karaoke = [Group(0, 2)],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "saved", new() { Fill = new(0, 0, 1) })
        };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
    }

    [Fact]
    public void DisabledTimingWithoutStoredPaintReportsOnlyTheSavedGroupLoss()
    {
        var line = Line() with { InactiveKaraoke = [Group(0, 4)] };
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(line));

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.InactiveKaraoke");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeStyle");
    }

    private static SubtitleLine Line() => new() { Text = "abcd", End = new(2), Style = new() { ShadowBlur = 0 } };

    private static KaraokeSegment Group(int start, int length) => new(start, length, new(1, 4), new(5, 4), SceneColor.White);
}
