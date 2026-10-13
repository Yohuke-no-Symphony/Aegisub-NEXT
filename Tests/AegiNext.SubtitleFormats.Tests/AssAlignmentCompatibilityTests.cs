using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssAlignmentCompatibilityTests
{
    private const string SOURCE = """
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, Outline, Shadow, Alignment, MarginV
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,2,2,2,20
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,
        """;

    [Theory]
    [InlineData(1, TextAlignment.BOTTOM_LEFT)]
    [InlineData(2, TextAlignment.BOTTOM_CENTER)]
    [InlineData(3, TextAlignment.BOTTOM_RIGHT)]
    [InlineData(4, TextAlignment.MIDDLE_LEFT)]
    [InlineData(5, TextAlignment.MIDDLE_CENTER)]
    [InlineData(6, TextAlignment.MIDDLE_RIGHT)]
    [InlineData(7, TextAlignment.TOP_LEFT)]
    [InlineData(8, TextAlignment.TOP_CENTER)]
    [InlineData(9, TextAlignment.TOP_RIGHT)]
    public void ModernAlignmentImportsAndExportsAllNinePositions(int value, TextAlignment alignment)
    {
        var imported = Assert.Single(AssSubtitleFormat.Parse(File("{\\an" + value + "}"), 640, 360).Lines);
        Assert.Equal(alignment, imported.Style.Alignment);
        Assert.Null(imported.Style.TextAlign);
        var document = SubtitleFormatTestDocument.Create(imported) with { Width = 640, Height = 360 };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains("\\an" + value, written.Text, StringComparison.Ordinal);
        var roundTripped = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Lines);
        Assert.Equal(imported.Style.Alignment, roundTripped.Style.Alignment);
        Assert.Null(imported.Style.Position);
        Assert.Null(roundTripped.Style.Position);
        Assert.Equal(imported.Style.Margins, roundTripped.Style.Margins);
        Assert.Equal(imported.Text, roundTripped.Text);
        Assert.Null(roundTripped.Style.TextAlign);
    }

    [Theory]
    [InlineData(1, 1, TextAlignment.BOTTOM_LEFT)]
    [InlineData(2, 2, TextAlignment.BOTTOM_CENTER)]
    [InlineData(3, 3, TextAlignment.BOTTOM_RIGHT)]
    [InlineData(5, 7, TextAlignment.TOP_LEFT)]
    [InlineData(6, 8, TextAlignment.TOP_CENTER)]
    [InlineData(7, 9, TextAlignment.TOP_RIGHT)]
    [InlineData(9, 4, TextAlignment.MIDDLE_LEFT)]
    [InlineData(10, 5, TextAlignment.MIDDLE_CENTER)]
    [InlineData(11, 6, TextAlignment.MIDDLE_RIGHT)]
    public void LegacyAlignmentMatchesModernPlacementAndExportsModernTags(int legacy, int modern, TextAlignment alignment)
    {
        var legacyResult = AssSubtitleFormat.Parse(File("{\\a" + legacy + "}"));
        var imported = Assert.Single(legacyResult.Lines);
        var reference = Assert.Single(AssSubtitleFormat.Parse(File("{\\an" + modern + "}")).Lines);
        Assert.Equal(alignment, imported.Style.Alignment);
        Assert.Equal(reference.Style, imported.Style);
        Assert.DoesNotContain(legacyResult.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        var written = AssSubtitleFormat.Write(SubtitleFormatTestDocument.Create(imported));
        Assert.Contains("\\an" + modern, written.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, TextAlignment.BOTTOM_LEFT)]
    [InlineData(2, TextAlignment.BOTTOM_CENTER)]
    [InlineData(3, TextAlignment.BOTTOM_RIGHT)]
    [InlineData(5, TextAlignment.TOP_LEFT)]
    [InlineData(6, TextAlignment.TOP_CENTER)]
    [InlineData(7, TextAlignment.TOP_RIGHT)]
    [InlineData(9, TextAlignment.MIDDLE_LEFT)]
    [InlineData(10, TextAlignment.MIDDLE_CENTER)]
    [InlineData(11, TextAlignment.MIDDLE_RIGHT)]
    public void LegacyProjectSourceUpdatesSharedAlignmentAndPreservesNativePosition(int value, TextAlignment alignment)
    {
        var original = new SubtitleLine
        {
            Text = "Long line\nx",
            End = new(3),
            Style = new()
            {
                TextAlign = SubtitleTextAlignment.RIGHT,
                Alignment = TextAlignment.BOTTOM_CENTER,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(17.125, -23.5) }
            }
        };
        var projection = AssTextProjection.Create(original);
        var edited = AssTextProjection.Apply(original,
            projection.Source.Replace("\\an2", "\\a" + value, StringComparison.Ordinal)).Line;
        Assert.Equal(alignment, edited.Style.Alignment);
        Assert.Equal(original.Style.Position, edited.Style.Position);
        Assert.Equal(original.Text, edited.Text);
        if (alignment == original.Style.Alignment)
        {
            Assert.Equal(original.Style.TextAlign, edited.Style.TextAlign);
        }
        else
        {
            Assert.Null(edited.Style.TextAlign);
        }
    }

    [Theory]
    [InlineData("{\\a5\\an3}", TextAlignment.TOP_LEFT)]
    [InlineData("{\\an3\\a5}", TextAlignment.BOTTOM_RIGHT)]
    [InlineData("{\\an2}{\\a5}", TextAlignment.BOTTOM_CENTER)]
    [InlineData("{\\a2}{\\an5}", TextAlignment.BOTTOM_CENTER)]
    public void LegacyAndModernTagsShareTheFirstAlignmentAndReportDuplicates(string tags, TextAlignment alignment)
    {
        var result = AssSubtitleFormat.Parse(File(tags));
        Assert.Equal(alignment, Assert.Single(result.Lines).Style.Alignment);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DuplicatePlacement");

        var original = new SubtitleLine
        {
            Text = "Long line\nx",
            End = new(3),
            Style = new()
            {
                TextAlign = SubtitleTextAlignment.RIGHT,
                Position = new() { Offset = new(17.125, -23.5) }
            }
        };
        var projection = AssTextProjection.Create(original);
        var source = projection.Source.Replace("{\\an2}", tags, StringComparison.Ordinal);
        Assert.NotEqual(projection.Source, source);
        var edit = AssTextProjection.Apply(original, source);
        Assert.Equal(alignment, edit.Line.Style.Alignment);
        Assert.Equal(original.Style.Position, edit.Line.Style.Position);
        Assert.Single(edit.Diagnostics, diagnostic => diagnostic.Code == "Ass.DuplicatePlacement");
        if (alignment == original.Style.Alignment)
        {
            Assert.Equal(original.Style.TextAlign, edit.Line.Style.TextAlign);
        }
        else
        {
            Assert.Null(edit.Line.Style.TextAlign);
        }
    }

    [Theory]
    [InlineData("{\\an0}", TextAlignment.BOTTOM_CENTER)]
    [InlineData("{\\an10}", TextAlignment.BOTTOM_CENTER)]
    [InlineData("{\\an7\\an0}", TextAlignment.TOP_LEFT)]
    [InlineData("{\\a5\\an10}", TextAlignment.TOP_LEFT)]
    public void InvalidModernAlignmentUsesExternalFallbackAndRejectsProjectSource(string tags, TextAlignment alignment)
    {
        Assert.Equal(alignment, Assert.Single(AssSubtitleFormat.Parse(File(tags)).Lines).Style.Alignment);
        var original = new SubtitleLine { Text = "Long line\nx", End = new(3) };
        var projection = AssTextProjection.Create(original);
        var source = projection.Source.Replace("{\\an2}", tags, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AssTextProjection.Apply(original, source));
    }

    [Theory]
    [InlineData(0, TextAlignment.BOTTOM_CENTER)]
    [InlineData(4, TextAlignment.TOP_LEFT)]
    [InlineData(8, TextAlignment.TOP_LEFT)]
    public void InvalidLegacyAlignmentUsesExternalFallbackAndRejectsProjectSource(int value, TextAlignment alignment)
    {
        var tags = "{\\a" + value + "}";
        Assert.Equal(alignment, Assert.Single(AssSubtitleFormat.Parse(File(tags)).Lines).Style.Alignment);
        var original = new SubtitleLine { Text = "Long line\nx", End = new(3) };
        var projection = AssTextProjection.Create(original);
        var source = projection.Source.Replace("{\\an2}", tags, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AssTextProjection.Apply(original, source));
    }

    private static string File(string tags)
    {
        return SOURCE + tags + @"Long line\Nx";
    }
}
