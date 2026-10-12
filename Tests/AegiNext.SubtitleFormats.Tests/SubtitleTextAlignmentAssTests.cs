using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class SubtitleTextAlignmentAssTests
{
    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void AdvancedSourcePreservesLegacyAlignmentUntilThePlacementTagChanges(SubtitleTextAlignment textAlign)
    {
        var original = new SubtitleLine
        {
            Text = "Long line\nx",
            End = new(3),
            Style = new()
            {
                TextAlign = textAlign,
                Alignment = TextAlignment.BOTTOM_CENTER,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(17.125, -23.5) }
            },
            InlineSpans = [new(0, 4, new() { Italic = true })]
        };
        var projection = AssTextProjection.Create(original);
        Assert.DoesNotContain(projection.Diagnostics, diagnostic => diagnostic.Code == "Ass.TextAlign");
        Assert.Same(original, AssTextProjection.Apply(original, projection.Source).Line);

        var textEdit = AssTextProjection.Apply(original, projection.Source.Replace("Long", "Wide", StringComparison.Ordinal)).Line;
        Assert.Equal("Wide line\nx", textEdit.Text);
        Assert.Equal(original.Style, textEdit.Style);
        Assert.Equal(original.InlineSpans.ToArray(), textEdit.InlineSpans.ToArray());

        var styleEdit = AssTextProjection.Apply(original, projection.Source.Replace("\\b0", "\\b1", StringComparison.Ordinal)).Line;
        Assert.Equal(textAlign, styleEdit.Style.TextAlign);
        Assert.Equal(original.Style.Alignment, styleEdit.Style.Alignment);
        Assert.Equal(original.Style.Position, styleEdit.Style.Position);
        Assert.True(styleEdit.InlineSpans[0].Style.Bold);

        var placementEdit = AssTextProjection.Apply(original, projection.Source.Replace("\\an2", "\\an7", StringComparison.Ordinal)).Line;
        Assert.Equal(TextAlignment.TOP_LEFT, placementEdit.Style.Alignment);
        Assert.Null(placementEdit.Style.TextAlign);
        Assert.Equal(original.Style.Position, placementEdit.Style.Position);
        Assert.Equal(original.InlineSpans.ToArray(), placementEdit.InlineSpans.ToArray());
    }

    [Theory]
    [InlineData(TextAlignment.BOTTOM_LEFT, SubtitleTextAlignment.CENTER)]
    [InlineData(TextAlignment.BOTTOM_LEFT, SubtitleTextAlignment.RIGHT)]
    [InlineData(TextAlignment.BOTTOM_CENTER, SubtitleTextAlignment.LEFT)]
    [InlineData(TextAlignment.BOTTOM_CENTER, SubtitleTextAlignment.RIGHT)]
    [InlineData(TextAlignment.BOTTOM_RIGHT, SubtitleTextAlignment.LEFT)]
    [InlineData(TextAlignment.BOTTOM_RIGHT, SubtitleTextAlignment.CENTER)]
    public void ExportDiagnosesIndependentAlignmentLossWithoutChangingNativePlacement(TextAlignment alignment, SubtitleTextAlignment textAlign)
    {
        var original = new SubtitleLine
        {
            Text = "Long line\nx",
            End = new(3),
            Style = new()
            {
                Alignment = alignment,
                TextAlign = textAlign,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(17.125, -23.5) }
            }
        };
        var document = SubtitleFormatTestDocument.Create(original);
        var legacy = document with { Subtitles = [original with { Style = original.Style with { TextAlign = null } }] };
        var written = AssSubtitleFormat.Write(document);
        var diagnostic = Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TextAlign");
        Assert.Equal(original.Id, diagnostic.SubtitleId);
        Assert.Equal(AssSubtitleFormat.Write(legacy).Text, written.Text);
        Assert.Same(original, document.Subtitles[0]);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Lines);
        Assert.Equal(alignment, imported.Style.Alignment);
        Assert.Null(imported.Style.TextAlign);
    }

    [Theory]
    [InlineData(TextAlignment.TOP_LEFT, SubtitleTextAlignment.LEFT)]
    [InlineData(TextAlignment.TOP_CENTER, SubtitleTextAlignment.CENTER)]
    [InlineData(TextAlignment.TOP_RIGHT, SubtitleTextAlignment.RIGHT)]
    [InlineData(TextAlignment.MIDDLE_LEFT, SubtitleTextAlignment.LEFT)]
    [InlineData(TextAlignment.MIDDLE_CENTER, SubtitleTextAlignment.CENTER)]
    [InlineData(TextAlignment.MIDDLE_RIGHT, SubtitleTextAlignment.RIGHT)]
    [InlineData(TextAlignment.BOTTOM_LEFT, SubtitleTextAlignment.LEFT)]
    [InlineData(TextAlignment.BOTTOM_CENTER, SubtitleTextAlignment.CENTER)]
    [InlineData(TextAlignment.BOTTOM_RIGHT, SubtitleTextAlignment.RIGHT)]
    public void MatchingExplicitAlignmentAndLegacyPlacementExportWithoutAlignmentLoss(TextAlignment alignment, SubtitleTextAlignment textAlign)
    {
        var original = new SubtitleLine
        {
            Text = "Long line\nx",
            End = new(3),
            Style = new() { Alignment = alignment, TextAlign = textAlign }
        };
        var document = SubtitleFormatTestDocument.Create(original);
        var written = AssSubtitleFormat.Write(document);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TextAlign");
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Lines);
        Assert.Equal(alignment, imported.Style.Alignment);
        Assert.Null(imported.Style.TextAlign);

        var legacy = document with { Subtitles = [original with { Style = original.Style with { TextAlign = null } }] };
        var legacyWritten = AssSubtitleFormat.Write(legacy);
        Assert.DoesNotContain(legacyWritten.Diagnostics, diagnostic => diagnostic.Code == "Ass.TextAlign");
        Assert.Equal(legacyWritten.Text, written.Text);
    }
}
